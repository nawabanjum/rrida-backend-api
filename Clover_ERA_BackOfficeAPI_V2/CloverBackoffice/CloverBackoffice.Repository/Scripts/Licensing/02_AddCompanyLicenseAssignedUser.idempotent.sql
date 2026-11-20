-- =============================================================================
-- 02_AddCompanyLicenseAssignedUser.idempotent.sql
--
-- Licence counting fix (registration / purchase / Add User).
--   * CompanyLicenses."AssignedUserId": the Team Manager holding the licence,
--     with or without a team yet (1 licence = 1 Team Manager). Set for the new
--     Profile Admin's trial licence, for a licence auto-assigned when a Team
--     Manager is created, and for Assign by managerId. Consumed = held by a live
--     Team Manager OR on a live team, each licence counted once.
--   * CompanyLicenses."AssignedUserAt": when it was given to that user.
--
-- Same statements as migration 20261013114942_AddCompanyLicenseAssignedUser,
-- with IF NOT EXISTS on the DDL and the summary SELECT added. Existing rows get
-- NULL (no holder), so existing profiles' counts do not change.
--
-- Safe to run more than once, one transaction. Run the whole file in pgAdmin (F5).
-- Rollback: ALTER TABLE public."CompanyLicenses" DROP CONSTRAINT IF EXISTS "FK_CompanyLicenses_Users_AssignedUserId";
--           DROP INDEX IF EXISTS public."IX_CompanyLicenses_AssignedUserId";
--           ALTER TABLE public."CompanyLicenses" DROP COLUMN "AssignedUserAt";
--           ALTER TABLE public."CompanyLicenses" DROP COLUMN "AssignedUserId";
--           DELETE FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261013114942_AddCompanyLicenseAssignedUser';
-- =============================================================================

START TRANSACTION;

ALTER TABLE public."CompanyLicenses" ADD COLUMN IF NOT EXISTS "AssignedUserAt" timestamp with time zone;
ALTER TABLE public."CompanyLicenses" ADD COLUMN IF NOT EXISTS "AssignedUserId" integer;

CREATE INDEX IF NOT EXISTS "IX_CompanyLicenses_AssignedUserId" ON public."CompanyLicenses" ("AssignedUserId");

DO $EF$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_CompanyLicenses_Users_AssignedUserId') THEN
        ALTER TABLE public."CompanyLicenses" ADD CONSTRAINT "FK_CompanyLicenses_Users_AssignedUserId"
            FOREIGN KEY ("AssignedUserId") REFERENCES public."Users" ("UserId") ON DELETE SET NULL;
    END IF;
END $EF$;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
SELECT '20261013114942_AddCompanyLicenseAssignedUser', '9.0.5'
WHERE NOT EXISTS (SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261013114942_AddCompanyLicenseAssignedUser');

COMMIT;

-- Summary
SELECT 'CompanyLicenses' AS "Table", count(*) AS "Rows", count("AssignedUserId") AS "HeldByUser"
FROM public."CompanyLicenses"
UNION ALL
SELECT 'Migration recorded', count(*), NULL
FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261013114942_AddCompanyLicenseAssignedUser';
