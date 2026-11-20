-- =============================================================================
-- 07_AddCompanyLicenseTrialTermination.idempotent.sql
--
-- Trial -> paid conversion on licence assignment.
--   * CompanyLicenses."TerminatedAt": set when a Team Manager's TRIAL licence is
--     ended early because a purchased licence was assigned to them
--     (POST /api/license-assignments, Action=Assign + managerId). The row is
--     also expired (ExpiryDate = TerminatedAt, IsDel = true) and is never
--     revived by expiry reconciliation, renewal, trial extension or
--     extend-license.
--   * CompanyLicenses."ReplacedByLicenseId": the purchased licence that
--     replaced it.
--
-- Same statements as migration 20261109110958_AddCompanyLicenseTrialTermination,
-- with IF NOT EXISTS on the DDL and the summary SELECT added. Existing rows get
-- NULL (not terminated), so nothing changes for existing licences.
--
-- Safe to run more than once, one transaction. Run the whole file in pgAdmin (F5).
-- Run BEFORE deploying the API build that contains this change.
-- Rollback: ALTER TABLE public."CompanyLicenses" DROP COLUMN IF EXISTS "TerminatedAt";
--           ALTER TABLE public."CompanyLicenses" DROP COLUMN IF EXISTS "ReplacedByLicenseId";
--           DELETE FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261109110958_AddCompanyLicenseTrialTermination';
-- =============================================================================

START TRANSACTION;

ALTER TABLE public."CompanyLicenses" ADD COLUMN IF NOT EXISTS "ReplacedByLicenseId" integer;
ALTER TABLE public."CompanyLicenses" ADD COLUMN IF NOT EXISTS "TerminatedAt" timestamp with time zone;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
SELECT '20261109110958_AddCompanyLicenseTrialTermination', '9.0.5'
WHERE NOT EXISTS (SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261109110958_AddCompanyLicenseTrialTermination');

COMMIT;

-- Summary
SELECT 'CompanyLicenses' AS "Table", count(*) AS "Rows", count("TerminatedAt") AS "TerminatedTrials"
FROM public."CompanyLicenses"
UNION ALL
SELECT 'Migration recorded', count(*), NULL
FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261109110958_AddCompanyLicenseTrialTermination';
