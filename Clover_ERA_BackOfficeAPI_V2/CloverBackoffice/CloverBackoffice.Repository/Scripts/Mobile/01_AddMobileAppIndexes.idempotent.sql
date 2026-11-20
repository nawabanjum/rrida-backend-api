-- =============================================================================
-- 01_AddMobileAppIndexes.idempotent.sql
--
-- Supporting indexes for the four tables the mobile app owns, and the history
-- row that tells EF the AddMobileAppTables migration is applied.
--
-- The tables themselves are NOT created here: DailyReflections, TimeAllocation,
-- FeedBack and Subscriptions were created by the old Azure Functions API and
-- already hold live data. This script only adds what that API never created -
-- an index on each foreign key, plus the composite the reporting queries need.
-- Without them, loading a reflection's time allocations is a sequential scan.
--
-- Safe to run more than once, and safe to run before or after the API is
-- deployed. Adding an index does not block reads or writes in PostgreSQL for
-- the brief moment these take at current row counts.
--
-- Target: PostgreSQL, database rrida. Run the file as a whole in pgAdmin.
-- =============================================================================

START TRANSACTION;

-- Guard: this must be the database the mobile app writes to.
--
-- to_regclass rather than information_schema.tables: information_schema hides
-- objects the current role has no privilege on, so it can answer 'missing' for a
-- table that is really there. to_regclass answers the question actually being
-- asked - does this relation exist.
--
-- The message names the database and role it found itself in, because the usual
-- cause of this firing is a query tool pointed at the wrong connection rather
-- than a genuinely missing table.
DO $EF$
BEGIN
    IF to_regclass('public."DailyReflections"') IS NULL THEN
        RAISE EXCEPTION
            'DailyReflections does not exist in database "%" (connected as "%"). Expected the mobile app database, normally "rrida". Check which connection this query tool is using.',
            current_database(), current_user;
    END IF;

    IF to_regclass('public."TimeAllocation"') IS NULL THEN
        RAISE EXCEPTION
            'TimeAllocation does not exist in database "%". DailyReflections was found, so this database is only partly set up.',
            current_database();
    END IF;

    IF to_regclass('public."FeedBack"') IS NULL THEN
        RAISE EXCEPTION
            'FeedBack does not exist in database "%". DailyReflections was found, so this database is only partly set up.',
            current_database();
    END IF;

    RAISE NOTICE 'Target confirmed: database "%", role "%".', current_database(), current_user;
END $EF$;

-- Every dashboard and export reads a team's reflections for a date range.
CREATE INDEX IF NOT EXISTS "IX_DailyReflections_Unit_Date"
    ON public."DailyReflections" ("BusinessUnitId", "ReflectionLocalDate");

CREATE INDEX IF NOT EXISTS "IX_DailyReflections_BusinessUnitId"
    ON public."DailyReflections" ("BusinessUnitId");

-- The child lookup on every single reflection read.
CREATE INDEX IF NOT EXISTS "IX_TimeAllocation_DailyReflectionId"
    ON public."TimeAllocation" ("DailyReflectionId");

CREATE INDEX IF NOT EXISTS "IX_FeedBack_Unit"
    ON public."FeedBack" ("BusinessUnitId");

-- Recorded so EF treats the migration as applied. The migration's Up() creates
-- exactly the four indexes above and nothing else, so running either one is
-- equivalent; this row stops it being attempted twice.
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260826122923_AddMobileAppTables', '9.0.5')
ON CONFLICT ("MigrationId") DO NOTHING;

COMMIT;
