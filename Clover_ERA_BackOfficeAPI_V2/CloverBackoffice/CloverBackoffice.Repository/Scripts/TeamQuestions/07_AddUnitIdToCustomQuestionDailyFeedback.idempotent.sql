-- =============================================================================
-- 07_AddUnitIdToCustomQuestionDailyFeedback.idempotent.sql
--
-- Records which business unit an answer to a custom question came from.
--
--   CustomQuestionDailyFeedback        gains UnitId NOT NULL, indexed with CreatedAt
--                                      and foreign-keyed to BusinessUnits ON DELETE
--                                      RESTRICT.
--   CustomQuestionDailyFeedbackAudits  gains the same UnitId in its snapshot. No
--                                      foreign key - an audit row outlives what it
--                                      describes, as every audit table here does.
--
-- The value is reachable through CustomQuestions.UnitId, and stored on the answer
-- anyway: answers are reported per unit, and TeamDailyFeedback already carries its own
-- unit for the same reason. The repository refuses an answer whose UnitId is not the
-- owning question's, so the two can never disagree.
--
-- Existing rows are backfilled from the owning question BEFORE the foreign key goes on.
-- Without that they land on unit 0 - the column default - and the key rejects them.
--
-- Safe to run more than once: every statement is guarded, and the whole script is
-- one transaction, so a failure leaves the database untouched.
--
-- Prerequisites: script 06 in this folder must already have been run.
-- Target: PostgreSQL, database rrida. Run the file as a whole in pgAdmin, not
-- statement by statement, or the transaction boundary is lost.
-- =============================================================================

START TRANSACTION;

-- Pre-flight: 06 has to have run, or there is nothing here to alter.
DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260827114040_AddUnitIdToCustomQuestionDailyFeedback') THEN
        IF NOT EXISTS (SELECT 1 FROM information_schema.tables
                       WHERE table_schema = 'public' AND table_name = 'CustomQuestionDailyFeedback') THEN
            RAISE EXCEPTION 'CustomQuestionDailyFeedback does not exist. Run 06_ReshapeCustomQuestions.idempotent.sql first.';
        END IF;
    END IF;
END $EF$;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260827114040_AddUnitIdToCustomQuestionDailyFeedback') THEN
    ALTER TABLE public."CustomQuestionDailyFeedbackAudits" ADD COLUMN IF NOT EXISTS "UnitId" bigint NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260827114040_AddUnitIdToCustomQuestionDailyFeedback') THEN
    ALTER TABLE public."CustomQuestionDailyFeedback" ADD COLUMN IF NOT EXISTS "UnitId" bigint NOT NULL DEFAULT 0;
    END IF;
END $EF$;

-- Backfill from the owning question. This has to happen before the foreign key below.
DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260827114040_AddUnitIdToCustomQuestionDailyFeedback') THEN
    UPDATE public."CustomQuestionDailyFeedback" f
    SET "UnitId" = q."UnitId"
    FROM public."CustomQuestions" q
    WHERE q."Id" = f."CustomQuestionId"
      AND f."UnitId" = 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260827114040_AddUnitIdToCustomQuestionDailyFeedback') THEN
    UPDATE public."CustomQuestionDailyFeedbackAudits" a
    SET "UnitId" = q."UnitId"
    FROM public."CustomQuestions" q
    WHERE q."Id" = a."CustomQuestionId"
      AND a."UnitId" = 0;
    END IF;
END $EF$;

-- Anything still on 0 is an answer whose question has been hard-deleted. Say so rather
-- than letting the foreign key fail with a constraint name and no explanation.
DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260827114040_AddUnitIdToCustomQuestionDailyFeedback') THEN
        IF EXISTS (SELECT 1 FROM public."CustomQuestionDailyFeedback" WHERE "UnitId" = 0) THEN
            RAISE EXCEPTION 'Some CustomQuestionDailyFeedback rows have no matching CustomQuestion to take UnitId from. Set their UnitId by hand before running this script.';
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260827114040_AddUnitIdToCustomQuestionDailyFeedback') THEN
    CREATE INDEX IF NOT EXISTS "IX_CustomQuestionDailyFeedback_Unit_CreatedAt" ON public."CustomQuestionDailyFeedback" ("UnitId", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260827114040_AddUnitIdToCustomQuestionDailyFeedback') THEN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_CustomQuestionDailyFeedback_BusinessUnits_UnitId') THEN
        ALTER TABLE public."CustomQuestionDailyFeedback" ADD CONSTRAINT "FK_CustomQuestionDailyFeedback_BusinessUnits_UnitId" FOREIGN KEY ("UnitId") REFERENCES "BusinessUnits" ("Id") ON DELETE RESTRICT;
    END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260827114040_AddUnitIdToCustomQuestionDailyFeedback') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260827114040_AddUnitIdToCustomQuestionDailyFeedback', '9.0.5');
    END IF;
END $EF$;
COMMIT;
