-- =============================================================================
-- 03_AddTeamWeeklyFlagCycleId.idempotent.sql
--
-- RRIDA Team Health Weekly Flag: which engine cycle a week's action came from
-- (EngineCycles.Id), so GET api/teams/{teamId}/weekly-flags can return cycleId
-- and the front end can open GET api/eran-coach/timeline?cycleId= for that
-- cycle. One nullable column; NOTHING EXISTING IS CHANGED.
--
-- Exact output of
--     dotnet ef migrations script 20261014125445_AddDeliveredWorksheetCard \
--                                 20261015131106_AddTeamWeeklyFlagCycleId --idempotent
-- with only this header added.
--
-- Existing Engine flags keep CycleId NULL until the engine next runs for the
-- team (POST api/rrida/engine/run or run-all); the writer fills it then.
--
-- Safe to run more than once, one transaction. Run the whole file in pgAdmin (F5).
-- Rollback: ALTER TABLE public."TeamWeeklyFlags" DROP COLUMN "CycleId";
--           DELETE FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261015131106_AddTeamWeeklyFlagCycleId';
-- =============================================================================

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261015131106_AddTeamWeeklyFlagCycleId') THEN
    ALTER TABLE public."TeamWeeklyFlags" ADD "CycleId" bigint;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261015131106_AddTeamWeeklyFlagCycleId') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261015131106_AddTeamWeeklyFlagCycleId', '9.0.5');
    END IF;
END $EF$;
COMMIT;
