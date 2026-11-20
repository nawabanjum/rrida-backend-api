-- =============================================================================
-- 06_ReshapeCustomQuestions.verbose.sql
--
-- DIAGNOSTIC COPY of 06_ReshapeCustomQuestions.idempotent.sql. Identical
-- operations, plus a RAISE NOTICE before each step.
--
-- Run this in pgAdmin and open the Messages panel. The notices print in order,
-- so the LAST notice shown is the step that failed, and the error printed right
-- after it is the real cause. Everything reporting 25P02 after that is noise.
--
-- Run ROLLBACK; first if an earlier attempt left an aborted transaction open.
-- Still one transaction, so a failure changes nothing.
-- =============================================================================

START TRANSACTION;

-- Pre-flight: the table this script reshapes has to exist, and it has to be empty.
-- CompanyId and UnitId are added NOT NULL DEFAULT 0, so existing rows would land on
-- company 0 / unit 0 and break the new foreign keys. Failing here says that plainly
-- instead of surfacing as a constraint violation forty statements later.
DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    RAISE NOTICE 'step %: %', 1, 'pre-flight: CustomQuestions exists and is empty';
        IF NOT EXISTS (SELECT 1 FROM information_schema.tables
                       WHERE table_schema = 'public' AND table_name = 'CustomQuestions') THEN
            RAISE EXCEPTION 'CustomQuestions does not exist. Run scripts 01 to 05 in this folder first.';
        END IF;

        IF EXISTS (SELECT 1 FROM public."CustomQuestions") THEN
            RAISE EXCEPTION 'CustomQuestions is not empty. Backfill CompanyId and UnitId for the existing rows before running this script, or the new foreign keys will reject them.';
        END IF;
    END IF;
END $EF$;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    RAISE NOTICE 'step %: %', 2, 'DROP INDEX IF EXISTS public."IX_CustomQuestions_Status_Window";';
    DROP INDEX IF EXISTS public."IX_CustomQuestions_Status_Window";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    RAISE NOTICE 'step %: %', 3, 'DROP INDEX IF EXISTS public."IX_CustomQuestions_UsageContext";';
    DROP INDEX IF EXISTS public."IX_CustomQuestions_UsageContext";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    RAISE NOTICE 'step %: %', 4, 'ALTER TABLE public."CustomQuestions" DROP CONSTRAINT IF EXISTS "CK_CustomQuestions_DailyConfig";';
    ALTER TABLE public."CustomQuestions" DROP CONSTRAINT IF EXISTS "CK_CustomQuestions_DailyConfig";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    RAISE NOTICE 'step %: %', 5, 'ALTER TABLE public."CustomQuestions" DROP CONSTRAINT IF EXISTS "CK_CustomQuestions_TeamConfig";';
    ALTER TABLE public."CustomQuestions" DROP CONSTRAINT IF EXISTS "CK_CustomQuestions_TeamConfig";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    RAISE NOTICE 'step %: %', 6, 'ALTER TABLE public."CustomQuestions" DROP CONSTRAINT IF EXISTS "CK_CustomQuestions_Window";';
    ALTER TABLE public."CustomQuestions" DROP CONSTRAINT IF EXISTS "CK_CustomQuestions_Window";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    RAISE NOTICE 'step %: %', 7, 'ALTER TABLE public."CustomQuestions" DROP COLUMN IF EXISTS "CloverCategory";';
    ALTER TABLE public."CustomQuestions" DROP COLUMN IF EXISTS "CloverCategory";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    RAISE NOTICE 'step %: %', 8, 'ALTER TABLE public."CustomQuestions" DROP COLUMN IF EXISTS "EffectiveFromUtc";';
    ALTER TABLE public."CustomQuestions" DROP COLUMN IF EXISTS "EffectiveFromUtc";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    RAISE NOTICE 'step %: %', 9, 'ALTER TABLE public."CustomQuestions" DROP COLUMN IF EXISTS "Ordinal";';
    ALTER TABLE public."CustomQuestions" DROP COLUMN IF EXISTS "Ordinal";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    RAISE NOTICE 'step %: %', 10, 'ALTER TABLE public."CustomQuestions" DROP COLUMN IF EXISTS "SentimentType";';
    ALTER TABLE public."CustomQuestions" DROP COLUMN IF EXISTS "SentimentType";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    RAISE NOTICE 'step %: %', 11, 'ALTER TABLE public."CustomQuestions" DROP COLUMN IF EXISTS "SubCategory";';
    ALTER TABLE public."CustomQuestions" DROP COLUMN IF EXISTS "SubCategory";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    RAISE NOTICE 'step %: %', 12, 'ALTER TABLE public."CustomQuestions" DROP COLUMN IF EXISTS "TeamElement";';
    ALTER TABLE public."CustomQuestions" DROP COLUMN IF EXISTS "TeamElement";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    RAISE NOTICE 'step %: %', 13, 'ALTER TABLE public."CustomQuestions" DROP COLUMN IF EXISTS "UsageContext";';
    ALTER TABLE public."CustomQuestions" DROP COLUMN IF EXISTS "UsageContext";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    RAISE NOTICE 'step %: %', 14, 'ALTER TABLE public."CustomQuestions" DROP COLUMN IF EXISTS "Variant";';
    ALTER TABLE public."CustomQuestions" DROP COLUMN IF EXISTS "Variant";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    RAISE NOTICE 'step %: %', 15, 'ALTER TABLE public."CustomQuestionAudits" DROP COLUMN IF EXISTS "CloverCategory";';
    ALTER TABLE public."CustomQuestionAudits" DROP COLUMN IF EXISTS "CloverCategory";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    RAISE NOTICE 'step %: %', 16, 'ALTER TABLE public."CustomQuestionAudits" DROP COLUMN IF EXISTS "EffectiveFromUtc";';
    ALTER TABLE public."CustomQuestionAudits" DROP COLUMN IF EXISTS "EffectiveFromUtc";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    RAISE NOTICE 'step %: %', 17, 'ALTER TABLE public."CustomQuestionAudits" DROP COLUMN IF EXISTS "Ordinal";';
    ALTER TABLE public."CustomQuestionAudits" DROP COLUMN IF EXISTS "Ordinal";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    RAISE NOTICE 'step %: %', 18, 'ALTER TABLE public."CustomQuestionAudits" DROP COLUMN IF EXISTS "SentimentType";';
    ALTER TABLE public."CustomQuestionAudits" DROP COLUMN IF EXISTS "SentimentType";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    RAISE NOTICE 'step %: %', 19, 'ALTER TABLE public."CustomQuestionAudits" DROP COLUMN IF EXISTS "SubCategory";';
    ALTER TABLE public."CustomQuestionAudits" DROP COLUMN IF EXISTS "SubCategory";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    RAISE NOTICE 'step %: %', 20, 'ALTER TABLE public."CustomQuestionAudits" DROP COLUMN IF EXISTS "TeamElement";';
    ALTER TABLE public."CustomQuestionAudits" DROP COLUMN IF EXISTS "TeamElement";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    RAISE NOTICE 'step %: %', 21, 'ALTER TABLE public."CustomQuestionAudits" DROP COLUMN IF EXISTS "UsageContext";';
    ALTER TABLE public."CustomQuestionAudits" DROP COLUMN IF EXISTS "UsageContext";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    RAISE NOTICE 'step %: %', 22, 'ALTER TABLE public."CustomQuestionAudits" DROP COLUMN IF EXISTS "Variant";';
    ALTER TABLE public."CustomQuestionAudits" DROP COLUMN IF EXISTS "Variant";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    RAISE NOTICE 'step %: %', 23, 'IF EXISTS (SELECT 1 FROM information_schema.columns';
    IF EXISTS (SELECT 1 FROM information_schema.columns
               WHERE table_schema = 'public' AND table_name = 'CustomQuestions'
                 AND column_name = 'EffectiveToUtc') THEN
        ALTER TABLE public."CustomQuestions" RENAME COLUMN "EffectiveToUtc" TO "ScheduledAt";
    ELSE
        ALTER TABLE public."CustomQuestions" ADD COLUMN IF NOT EXISTS "ScheduledAt" timestamp with time zone;
    END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    RAISE NOTICE 'step %: %', 24, 'IF EXISTS (SELECT 1 FROM information_schema.columns';
    IF EXISTS (SELECT 1 FROM information_schema.columns
               WHERE table_schema = 'public' AND table_name = 'CustomQuestionAudits'
                 AND column_name = 'EffectiveToUtc') THEN
        ALTER TABLE public."CustomQuestionAudits" RENAME COLUMN "EffectiveToUtc" TO "ScheduledAt";
    ELSE
        ALTER TABLE public."CustomQuestionAudits" ADD COLUMN IF NOT EXISTS "ScheduledAt" timestamp with time zone;
    END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    RAISE NOTICE 'step %: %', 25, 'ALTER TABLE public."CustomQuestions" ADD COLUMN IF NOT EXISTS "CompanyId" bigint NOT NULL DEFAULT 0;';
    ALTER TABLE public."CustomQuestions" ADD COLUMN IF NOT EXISTS "CompanyId" bigint NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    RAISE NOTICE 'step %: %', 26, 'ALTER TABLE public."CustomQuestions" ADD COLUMN IF NOT EXISTS "UnitId" bigint NOT NULL DEFAULT 0;';
    ALTER TABLE public."CustomQuestions" ADD COLUMN IF NOT EXISTS "UnitId" bigint NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    RAISE NOTICE 'step %: %', 27, 'ALTER TABLE public."CustomQuestionAudits" ADD COLUMN IF NOT EXISTS "CompanyId" bigint NOT NULL DEFAULT 0;';
    ALTER TABLE public."CustomQuestionAudits" ADD COLUMN IF NOT EXISTS "CompanyId" bigint NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    RAISE NOTICE 'step %: %', 28, 'ALTER TABLE public."CustomQuestionAudits" ADD COLUMN IF NOT EXISTS "UnitId" bigint NOT NULL DEFAULT 0;';
    ALTER TABLE public."CustomQuestionAudits" ADD COLUMN IF NOT EXISTS "UnitId" bigint NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    RAISE NOTICE 'step %: %', 29, 'CREATE TABLE IF NOT EXISTS public."CustomQuestionDailyFeedback" (';
    CREATE TABLE IF NOT EXISTS public."CustomQuestionDailyFeedback" (
        "Id" bigint GENERATED BY DEFAULT AS IDENTITY,
        "CustomQuestionId" bigint NOT NULL,
        "FreeText" character varying(300),
        "CreatedBy" integer,
        "UpdatedBy" integer,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_CustomQuestionDailyFeedback" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_CustomQuestionDailyFeedback_CustomQuestions_CustomQuestionId" FOREIGN KEY ("CustomQuestionId") REFERENCES public."CustomQuestions" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    RAISE NOTICE 'step %: %', 30, 'CREATE TABLE IF NOT EXISTS public."CustomQuestionDailyFeedbackAudits" (';
    CREATE TABLE IF NOT EXISTS public."CustomQuestionDailyFeedbackAudits" (
        "AuditId" integer GENERATED BY DEFAULT AS IDENTITY,
        "CustomQuestionDailyFeedbackId" bigint NOT NULL,
        "CustomQuestionId" bigint NOT NULL,
        "FreeText" character varying(300),
        "ChangeSummary" character varying(1000),
        "CreatedBy" integer,
        "UpdatedBy" integer,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        "LoggedAt" timestamp with time zone NOT NULL,
        "VersionNumber" integer NOT NULL,
        "OperationType" character varying(20) NOT NULL,
        CONSTRAINT "PK_CustomQuestionDailyFeedbackAudits" PRIMARY KEY ("AuditId")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    RAISE NOTICE 'step %: %', 31, 'CREATE INDEX IF NOT EXISTS "IX_CustomQuestions_Company_Unit" ON public."CustomQuestions" ("CompanyId", "UnitId';
    CREATE INDEX IF NOT EXISTS "IX_CustomQuestions_Company_Unit" ON public."CustomQuestions" ("CompanyId", "UnitId") WHERE "IsDel" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    RAISE NOTICE 'step %: %', 32, 'CREATE INDEX IF NOT EXISTS "IX_CustomQuestions_Status_ScheduledAt" ON public."CustomQuestions" ("Status", "Sch';
    CREATE INDEX IF NOT EXISTS "IX_CustomQuestions_Status_ScheduledAt" ON public."CustomQuestions" ("Status", "ScheduledAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    RAISE NOTICE 'step %: %', 33, 'CREATE INDEX IF NOT EXISTS "IX_CustomQuestions_UnitId" ON public."CustomQuestions" ("UnitId");';
    CREATE INDEX IF NOT EXISTS "IX_CustomQuestions_UnitId" ON public."CustomQuestions" ("UnitId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    RAISE NOTICE 'step %: %', 34, 'CREATE INDEX IF NOT EXISTS "IX_CustomQuestionDailyFeedback_CreatedAt" ON public."CustomQuestionDailyFeedback" ';
    CREATE INDEX IF NOT EXISTS "IX_CustomQuestionDailyFeedback_CreatedAt" ON public."CustomQuestionDailyFeedback" ("CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    RAISE NOTICE 'step %: %', 35, 'CREATE INDEX IF NOT EXISTS "IX_CustomQuestionDailyFeedback_Question" ON public."CustomQuestionDailyFeedback" (';
    CREATE INDEX IF NOT EXISTS "IX_CustomQuestionDailyFeedback_Question" ON public."CustomQuestionDailyFeedback" ("CustomQuestionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    RAISE NOTICE 'step %: %', 36, 'CREATE INDEX IF NOT EXISTS "IX_CustomQuestionDailyFeedbackAudits_Feedback_Version" ON public."CustomQuestionDa';
    CREATE INDEX IF NOT EXISTS "IX_CustomQuestionDailyFeedbackAudits_Feedback_Version" ON public."CustomQuestionDailyFeedbackAudits" ("CustomQuestionDailyFeedbackId", "VersionNumber");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    RAISE NOTICE 'step %: %', 37, 'CREATE INDEX IF NOT EXISTS "IX_CustomQuestionDailyFeedbackAudits_Operation" ON public."CustomQuestionDailyFeed';
    CREATE INDEX IF NOT EXISTS "IX_CustomQuestionDailyFeedbackAudits_Operation" ON public."CustomQuestionDailyFeedbackAudits" ("OperationType");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    RAISE NOTICE 'step %: %', 38, 'IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = ''FK_CustomQuestions_BusinessUnits_UnitId'') THEN';
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_CustomQuestions_BusinessUnits_UnitId') THEN
        ALTER TABLE public."CustomQuestions" ADD CONSTRAINT "FK_CustomQuestions_BusinessUnits_UnitId" FOREIGN KEY ("UnitId") REFERENCES "BusinessUnits" ("Id") ON DELETE RESTRICT;
    END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    RAISE NOTICE 'step %: %', 39, 'IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = ''FK_CustomQuestions_Companies_CompanyId'') THEN';
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_CustomQuestions_Companies_CompanyId') THEN
        ALTER TABLE public."CustomQuestions" ADD CONSTRAINT "FK_CustomQuestions_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE RESTRICT;
    END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    RAISE NOTICE 'step %: %', 40, 'INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")';
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260826090839_ReshapeCustomQuestions', '9.0.5');
    END IF;
END $EF$;
COMMIT;

