-- =============================================================================
-- 07_ManagerActionsTeamOnly.idempotent.sql
--
-- Manager actions (api/actions) are team-based only.
--
--   ManagerActions  drops "SubjectUserId" (the person an action was about) and
--                   "OwnerUserId" (an assigned owner) with their two indexes,
--                   makes "UnitId" required, and adds an index on
--                   (ProfileId, CreatedBy). CreatedBy stays: it is always the
--                   logged-in user who created the action.
--
-- Run AFTER 06_AddManagerActions.idempotent.sql (already applied on Dev). The
-- table was empty on Dev when this was written, so nothing is lost; the NOT NULL
-- step fails (and the whole script rolls back) if any row has no team.
--
-- Exact output of
--     dotnet ef migrations script 20261110133953_AddManagerActions \
--                                 20261110162523_ManagerActionsTeamOnly --idempotent
-- with only this header and the summary SELECT added.
--
-- Safe to run more than once (guarded on the migration history row), one
-- transaction. Run the whole file in pgAdmin (F5). Run BEFORE deploying.
-- =============================================================================

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261110162523_ManagerActionsTeamOnly') THEN
    DROP INDEX public."IX_ManagerActions_Profile_Owner";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261110162523_ManagerActionsTeamOnly') THEN
    DROP INDEX public."IX_ManagerActions_Subject";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261110162523_ManagerActionsTeamOnly') THEN
    ALTER TABLE public."ManagerActions" DROP COLUMN "OwnerUserId";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261110162523_ManagerActionsTeamOnly') THEN
    ALTER TABLE public."ManagerActions" DROP COLUMN "SubjectUserId";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261110162523_ManagerActionsTeamOnly') THEN
    ALTER TABLE public."ManagerActions" ALTER COLUMN "UnitId" SET NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261110162523_ManagerActionsTeamOnly') THEN
    CREATE INDEX "IX_ManagerActions_Profile_CreatedBy" ON public."ManagerActions" ("ProfileId", "CreatedBy") WHERE "IsDel" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261110162523_ManagerActionsTeamOnly') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261110162523_ManagerActionsTeamOnly', '9.0.5');
    END IF;
END $EF$;
COMMIT;



-- Summary
SELECT
    (SELECT COUNT(*) FROM "__EFMigrationsHistory"
      WHERE "MigrationId" = '20261110162523_ManagerActionsTeamOnly')              AS migration_applied,
    (SELECT string_agg(column_name, ', ' ORDER BY ordinal_position) FROM information_schema.columns
      WHERE table_schema = 'public' AND table_name = 'ManagerActions')           AS columns,
    (SELECT is_nullable FROM information_schema.columns
      WHERE table_schema = 'public' AND table_name = 'ManagerActions'
        AND column_name = 'UnitId')                                              AS unitid_nullable,
    (SELECT string_agg(indexname, ', ' ORDER BY indexname) FROM pg_indexes
      WHERE schemaname = 'public' AND tablename = 'ManagerActions')              AS indexes,
    (SELECT COUNT(*) FROM public."ManagerActions")                               AS action_rows;
