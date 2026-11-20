-- =============================================================================
-- 01_AddBusinessUnitIdToEranCoachMemories.idempotent.sql
--
-- ERAN Coach on Team Health is team-specific.
--   * EranCoachManagerMemories gets "BusinessUnitId": the coach's rolling memory
--     is kept per manager PER TEAM. Existing rows keep NULL (= no team selected).
--   * Index for listing a user's conversations of one team
--     (EranCoachConversations."BusinessUnitId" already exists).
--
-- Output of
--     dotnet ef migrations script 20260930153617_SyncEngineModelSnapshot \
--                                 20261006122004_AddBusinessUnitIdToEranCoachMemories --idempotent
-- with this header, IF NOT EXISTS on the three DDL statements (so it also runs
-- on a database where the column/indexes were added by hand), and the summary
-- SELECT added.
--
-- Safe to run more than once, one transaction. Run the whole file in pgAdmin (F5).
-- Rollback: DROP INDEX public."IX_EranCoachConversations_UserId_BusinessUnitId";
--           DROP INDEX public."IX_EranCoachManagerMemories_ManagerUserId_BusinessUnitId";
--           ALTER TABLE public."EranCoachManagerMemories" DROP COLUMN "BusinessUnitId";
--           DELETE FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261006122004_AddBusinessUnitIdToEranCoachMemories';
-- =============================================================================

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261006122004_AddBusinessUnitIdToEranCoachMemories') THEN
    ALTER TABLE public."EranCoachManagerMemories" ADD COLUMN IF NOT EXISTS "BusinessUnitId" bigint;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261006122004_AddBusinessUnitIdToEranCoachMemories') THEN
    CREATE INDEX IF NOT EXISTS "IX_EranCoachManagerMemories_ManagerUserId_BusinessUnitId" ON public."EranCoachManagerMemories" ("ManagerUserId", "BusinessUnitId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261006122004_AddBusinessUnitIdToEranCoachMemories') THEN
    CREATE INDEX IF NOT EXISTS "IX_EranCoachConversations_UserId_BusinessUnitId" ON public."EranCoachConversations" ("UserId", "BusinessUnitId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261006122004_AddBusinessUnitIdToEranCoachMemories') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261006122004_AddBusinessUnitIdToEranCoachMemories', '9.0.1');
    END IF;
END $EF$;
COMMIT;

-- Summary
SELECT 'EranCoachManagerMemories' AS "Table", count(*) AS "Rows", count("BusinessUnitId") AS "RowsWithTeam"
FROM public."EranCoachManagerMemories"
UNION ALL
SELECT 'EranCoachConversations', count(*), count("BusinessUnitId")
FROM public."EranCoachConversations" WHERE NOT "IsDel"
UNION ALL
SELECT 'Migration recorded', count(*), NULL
FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261006122004_AddBusinessUnitIdToEranCoachMemories';
