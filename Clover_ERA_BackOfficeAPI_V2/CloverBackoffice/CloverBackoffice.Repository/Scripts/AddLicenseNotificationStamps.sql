-- CompanyLicenses notification bookkeeping — license expiry / reactivation emails.
--
-- Background: SubscriptionLifecycleJob only announced licenses it had expired
-- itself (its candidate query filtered on !IsDel). But
-- SubscriptionService.ReconcileLicenseExpiryAsync flips IsDel on ~18 read paths
-- (every subscription / license page load), and the job ticks only twice a day.
-- Whoever got there first decided whether the notification existed at all:
--   * admin opens the page between real expiry and the next tick  -> IsDel already
--     true -> job skips the row -> expiry email lost permanently;
--   * with GracePeriod > 0 the read path used the RAW ExpiryDate while the job
--     used ExpiryDate + grace, so the read path ALWAYS won -> the email was never
--     sent for those subscriptions;
--   * mirror case on renewal -> "License Reactivated" email lost the same way.
--
-- Fix: the two columns below record whether the expiry / reactivation pair has
-- already been announced, so the job selects on notification state instead of
-- IsDel and picks the row up regardless of who flipped it. The read path keeps
-- mutating state for UI correctness but still sends nothing itself (it runs
-- inside request handling).
--
-- Purely additive: two nullable columns plus a one-time backfill of the new
-- column. Nothing is dropped, renamed, or altered. Safe to re-run.
--
-- RUN ORDER: execute this script BEFORE deploying the updated API. If the app
-- starts first, every query touching CompanyLicenses fails — EF selects the new
-- columns. STEP 2 must run before the job's first tick (see note in STEP 2).

--------------------------------------------------------------------------------
-- STEP 1 — add the columns
--
-- Nullable, no default, no constraints, no index: NULL means "not announced yet",
-- which is exactly the correct state for every live license.
--------------------------------------------------------------------------------
ALTER TABLE public."CompanyLicenses"
    ADD COLUMN IF NOT EXISTS "ExpiryNotifiedAt"       timestamp with time zone NULL,
    ADD COLUMN IF NOT EXISTS "ReactivationNotifiedAt" timestamp with time zone NULL;

--------------------------------------------------------------------------------
-- STEP 2 — backfill the historical backlog
--
-- Every license that is ALREADY expired is marked as announced. Without this the
-- first job tick after deploy treats the entire backlog as freshly expired and
-- mails every company admin about licenses that lapsed months ago.
--
-- Grace-adjusted, matching SubscriptionLifecycleJob.ComputeLicenseExpiry
-- (ExpiryDate + CompanySubscriptions.GracePeriod).
--
-- Licenses that are soft-deleted but whose date is still in the future are left
-- NULL on purpose: nothing was announced for them, and the reactivation pass only
-- fires for previously-announced licenses, so they stay silent.
--
-- Writes ONLY "ExpiryNotifiedAt", and only where it is still NULL.
--------------------------------------------------------------------------------
UPDATE public."CompanyLicenses" l
SET    "ExpiryNotifiedAt" = COALESCE(l."UpdatedAt", l."ExpiryDate")
FROM   public."CompanySubscriptions" s
WHERE  l."CompanySubscriptionId" = s."Id"
  AND  l."IsDel"             = TRUE
  AND  l."ExpiryDate"       IS NOT NULL
  AND  l."ExpiryNotifiedAt" IS NULL
  AND  l."ExpiryDate" + (COALESCE(s."GracePeriod", 0) || ' days')::interval <= NOW();

-- Same, for licenses with no subscription link (imports / manual rows). No grace
-- applies there — there is no subscription to read it from.
UPDATE public."CompanyLicenses"
SET    "ExpiryNotifiedAt" = COALESCE("UpdatedAt", "ExpiryDate")
WHERE  "CompanySubscriptionId" IS NULL
  AND  "IsDel"             = TRUE
  AND  "ExpiryDate"       IS NOT NULL
  AND  "ExpiryNotifiedAt" IS NULL
  AND  "ExpiryDate" <= NOW();

--------------------------------------------------------------------------------
-- STEP 3 — verify the columns landed
--------------------------------------------------------------------------------
SELECT column_name, data_type, is_nullable, column_default
FROM   information_schema.columns
WHERE  table_schema = 'public'
  AND  table_name   = 'CompanyLicenses'
  AND  column_name IN ('ExpiryNotifiedAt', 'ReactivationNotifiedAt');

--------------------------------------------------------------------------------
-- STEP 4 — verify the backfill
--
-- will_notify_next_tick = what the job will email about on its first run.
-- Expect 0, or only rows still inside their grace window.
--------------------------------------------------------------------------------
SELECT count(*) FILTER (WHERE "ExpiryNotifiedAt" IS NOT NULL)                AS marked_announced,
       count(*) FILTER (WHERE "ExpiryNotifiedAt" IS NULL
                          AND "IsDel"
                          AND "ExpiryDate" <= NOW())                         AS will_notify_next_tick,
       count(*) FILTER (WHERE "ExpiryDate" > NOW() AND NOT "IsDel")          AS live_licenses,
       count(*)                                                              AS total
FROM   public."CompanyLicenses";

--------------------------------------------------------------------------------
-- STEP 5 — grants
--
-- ADD COLUMN inherits the table's existing privileges, so normally nothing to do.
-- Rows returned below mean COLUMN-LEVEL grants are in use on this table, in which
-- case the two new columns need the same grants as the rest (see the prod grant
-- profile used for BusinessUnits). An empty result means table-level grants apply
-- and no action is required.
--------------------------------------------------------------------------------
SELECT grantee, privilege_type, column_name
FROM   information_schema.column_privileges
WHERE  table_schema = 'public'
  AND  table_name   = 'CompanyLicenses'
ORDER  BY grantee, column_name;
