-- =============================================================================
-- 09_AddManagerActionStartDate.idempotent.sql
--
-- Start date on a manager's own (manual) action: POST api/actions "startDate".
--
--   ManagerActions.StartDate  date, NULL. Set on create; existing rows stay NULL.
--
-- One nullable column only; nothing existing is changed.
--
-- Run AFTER 06, 07 and 08 in this folder.
--
-- Exact output of
--     dotnet ef migrations script 20261118095655_AddFeedItemPromptFingerprint \
--                                 20261118113237_AddManagerActionStartDate --idempotent
-- with only this header and the summary SELECT added.
--
-- Safe to run more than once (guarded on the migration history row), one
-- transaction. Run the whole file in pgAdmin (F5). Run BEFORE deploying: every
-- api/actions path reads this column.
-- =============================================================================

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261118113237_AddManagerActionStartDate') THEN
    ALTER TABLE public."ManagerActions" ADD "StartDate" date;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261118113237_AddManagerActionStartDate') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261118113237_AddManagerActionStartDate', '9.0.5');
    END IF;
END $EF$;
COMMIT;



-- Summary
SELECT
    (SELECT COUNT(*) FROM "__EFMigrationsHistory"
      WHERE "MigrationId" = '20261118113237_AddManagerActionStartDate')          AS migration_applied,
    (SELECT data_type FROM information_schema.columns
      WHERE table_schema = 'public' AND table_name = 'ManagerActions'
        AND column_name = 'StartDate')                                         AS startdate_type,
    (SELECT COUNT(*) FROM public."ManagerActions" WHERE "StartDate" IS NOT NULL) AS rows_with_start_date;
