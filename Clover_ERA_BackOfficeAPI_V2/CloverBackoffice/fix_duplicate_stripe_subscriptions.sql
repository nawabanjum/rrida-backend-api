-- ============================================================================
-- CompanySubscriptions.StripeSubscriptionId: one live row per Stripe subscription.
--
-- WHY
-- One Stripe purchase must create exactly one CompanySubscription and one batch
-- of licences. Two webhook handlers can create that row:
--   * checkout.session.completed    -> CreateAdditionalSubscriptionAsync
--   * customer.subscription.updated -> SelfHealMissingSubscriptionAsync
-- Both "check by StripeSubscriptionId, then insert", with nothing underneath.
-- Stripe delivers both events within a second, so both inserted and the
-- purchase's licences were minted twice (Dev profile 132: subscriptions
-- 112 + 113, licences 297 + 298, ONE payment; profile 130: 106 + 107).
-- The code now serializes the two handlers (advisory lock 16025); this index
-- is the database guarantee behind it.
--
-- WHAT THIS SCRIPT DOES (re-runnable, run top to bottom with F5)
--   1. For every Stripe subscription with more than one live row, keeps ONE:
--      the row holding the payment (most payments), else the lowest Id.
--   2. Removes the extra rows' licences: IsDel = true and ExpiryDate = now()
--      (so the expiry reconciler can never revive them), closes any active
--      team assignment / complimentary grant, writes a licence audit row.
--   3. Moves any payment on an extra row to the kept row, then soft-deletes
--      the extra subscription row.
--   4. Creates the partial unique index (live rows with a Stripe id only).
--   5. Prints a summary.
--
-- Not CONCURRENTLY: pgAdmin runs the script as one batch and CONCURRENTLY
-- cannot run inside a transaction. The table is small, so the brief lock is fine.
-- Run on EVERY environment before deploying the code change.
-- ============================================================================

BEGIN;

-- 1. Duplicate rows to remove.
CREATE TEMP TABLE dup_subs ON COMMIT DROP AS
WITH ranked AS (
    SELECT s."Id",
           s."StripeSubscriptionId",
           s."ProfileId",
           row_number() OVER (
               PARTITION BY s."StripeSubscriptionId"
               ORDER BY (SELECT count(*) FROM "Payments" y WHERE y."CompanySubscriptionId" = s."Id") DESC,
                        s."Id"
           ) AS rn,
           first_value(s."Id") OVER (
               PARTITION BY s."StripeSubscriptionId"
               ORDER BY (SELECT count(*) FROM "Payments" y WHERE y."CompanySubscriptionId" = s."Id") DESC,
                        s."Id"
           ) AS keep_id
    FROM "CompanySubscriptions" s
    WHERE s."StripeSubscriptionId" IS NOT NULL
      AND s."IsDel" = false
)
SELECT "Id" AS dup_id, keep_id, "StripeSubscriptionId", "ProfileId"
FROM ranked
WHERE rn > 1;

CREATE TEMP TABLE dup_licenses ON COMMIT DROP AS
SELECT l."Id" AS license_id, d.dup_id, d."ProfileId"
FROM "CompanyLicenses" l
JOIN dup_subs d ON d.dup_id = l."CompanySubscriptionId"
WHERE l."IsDel" = false;

-- 2. Remove the extra licences.
UPDATE "AssignedCompanyLicenses" a
SET "IsActive" = false, "UnassignedAt" = now(), "UpdatedAt" = now()
WHERE a."CompanyLicenseId" IN (SELECT license_id FROM dup_licenses)
  AND a."IsActive" = true AND a."IsDel" = false;

UPDATE "ComplimentaryLicenseGrants" g
SET "IsActive" = false, "RevokedAt" = now(), "UpdatedAt" = now()
WHERE g."CompanyLicenseId" IN (SELECT license_id FROM dup_licenses)
  AND g."IsActive" = true AND g."IsDel" = false;

UPDATE "CompanyLicenses" l
SET "IsDel" = true,
    "ExpiryDate" = now(),
    "UpdatedAt" = now(),
    "Notes" = 'Removed: duplicate licence from a double-processed Stripe purchase (fix_duplicate_stripe_subscriptions.sql)'
WHERE l."Id" IN (SELECT license_id FROM dup_licenses);

INSERT INTO "CompanyLicenseAuditLogs"
    ("Id", "ProfileId", "LicenseKey", "LicenseType", "CompanySubscriptionId", "PlanId",
     "CreatedBy", "UpdatedBy", "CreatedAt", "UpdatedAt", "IsDel", "LoggedAt",
     "VersionNumber", "OperationType", "ExpiryDate", "Notes")
SELECT l."Id", l."ProfileId", l."LicenseKey", l."LicenseType", l."CompanySubscriptionId", l."PlanId",
       l."CreatedBy", l."UpdatedBy", l."CreatedAt", l."UpdatedAt", l."IsDel", now(),
       coalesce((SELECT max(al."VersionNumber") FROM "CompanyLicenseAuditLogs" al WHERE al."Id" = l."Id"), 0) + 1,
       'UPDATE', l."ExpiryDate", l."Notes"
FROM "CompanyLicenses" l
WHERE l."Id" IN (SELECT license_id FROM dup_licenses);

-- 3. Payments follow the kept row; the extra row is soft-deleted.
UPDATE "Payments" y
SET "CompanySubscriptionId" = d.keep_id
FROM dup_subs d
WHERE y."CompanySubscriptionId" = d.dup_id;

UPDATE "CompanySubscriptions" s
SET "IsDel" = true, "UpdatedAt" = now()
WHERE s."Id" IN (SELECT dup_id FROM dup_subs);

-- Kept for the summary (temp tables drop at COMMIT).
CREATE TEMP TABLE fix_summary AS
SELECT d."ProfileId", d."StripeSubscriptionId", d.keep_id AS kept_subscription, d.dup_id AS removed_subscription,
       (SELECT string_agg(x.license_id::text, ',') FROM dup_licenses x WHERE x.dup_id = d.dup_id) AS removed_licences
FROM dup_subs d;

COMMIT;

-- 4. Database guarantee: one live row per Stripe subscription.
CREATE UNIQUE INDEX IF NOT EXISTS "UX_CompanySubscriptions_StripeSubscriptionId_Live"
    ON "CompanySubscriptions" ("StripeSubscriptionId")
    WHERE "StripeSubscriptionId" IS NOT NULL AND "IsDel" = false;

-- 5. Summary. First result: what this run removed (empty on a re-run).
SELECT * FROM fix_summary ORDER BY "ProfileId", removed_subscription;

-- Remaining duplicates: must be 0 rows.
SELECT "StripeSubscriptionId", count(*) AS live_rows
FROM "CompanySubscriptions"
WHERE "StripeSubscriptionId" IS NOT NULL AND "IsDel" = false
GROUP BY 1 HAVING count(*) > 1;

-- Index present.
SELECT indexname, indexdef FROM pg_indexes
WHERE tablename = 'CompanySubscriptions' AND indexname = 'UX_CompanySubscriptions_StripeSubscriptionId_Live';

-- Live licence counts of the affected Dev profiles (expected: 130 -> 7 live; 132 -> 3 live).
SELECT l."ProfileId",
       count(*) FILTER (WHERE l."IsDel" = false AND (l."ExpiryDate" IS NULL OR l."ExpiryDate" > now())) AS live_licences,
       count(*) FILTER (WHERE l."IsDel" = true) AS removed_or_expired
FROM "CompanyLicenses" l
WHERE l."ProfileId" IN (130, 132)
GROUP BY 1 ORDER BY 1;

DROP TABLE IF EXISTS fix_summary;
