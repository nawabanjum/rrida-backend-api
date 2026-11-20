-- =============================================================================
-- 01_AddCompanyLicenseCancelAtPeriodEnd.idempotent.sql
--
-- Cancel License API (POST /api/license-assignments/cancel).
--   * CompanyLicenses."CancelAtPeriodEnd": the seat was taken out of its Stripe
--     subscription's quantity. It stays live until its own ExpiryDate and is no
--     longer extended on renewal. Existing rows get false (= renewing).
--   * CompanyLicenses."CanceledAt": when that cancellation was requested.
--
-- Same statements as migration 20261007093325_AddCompanyLicenseCancelAtPeriodEnd,
-- with IF NOT EXISTS on the DDL (so it also runs where the columns were added by
-- hand) and the summary SELECT added.
--
-- Safe to run more than once, one transaction. Run the whole file in pgAdmin (F5).
-- Rollback: ALTER TABLE public."CompanyLicenses" DROP COLUMN "CanceledAt";
--           ALTER TABLE public."CompanyLicenses" DROP COLUMN "CancelAtPeriodEnd";
--           DELETE FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007093325_AddCompanyLicenseCancelAtPeriodEnd';
-- =============================================================================

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007093325_AddCompanyLicenseCancelAtPeriodEnd') THEN
    ALTER TABLE public."CompanyLicenses" ADD COLUMN IF NOT EXISTS "CancelAtPeriodEnd" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007093325_AddCompanyLicenseCancelAtPeriodEnd') THEN
    ALTER TABLE public."CompanyLicenses" ADD COLUMN IF NOT EXISTS "CanceledAt" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007093325_AddCompanyLicenseCancelAtPeriodEnd') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261007093325_AddCompanyLicenseCancelAtPeriodEnd', '9.0.5');
    END IF;
END $EF$;
COMMIT;

-- Summary
SELECT 'CompanyLicenses' AS "Table", count(*) AS "Rows", count(*) FILTER (WHERE "CancelAtPeriodEnd") AS "CancelledAtPeriodEnd"
FROM public."CompanyLicenses"
UNION ALL
SELECT 'Migration recorded', count(*), NULL
FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007093325_AddCompanyLicenseCancelAtPeriodEnd';
