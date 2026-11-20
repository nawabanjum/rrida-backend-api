-- =============================================================================
-- 06_ReshapeCustomQuestions.idempotent.sql
--
-- Reshapes Custom Questions around tenant ownership, and adds the answer table.
--
--   CustomQuestions            keeps Id, Text, ContentCultureId, Status, IsDel and the
--                              common audit columns; gains CompanyId and UnitId; the
--                              EffectiveFrom/To window collapses to one ScheduledAt.
--                              UsageContext, SentimentType, CloverCategory, TeamElement,
--                              SubCategory, Variant and Ordinal are dropped.
--   CustomQuestionAudits       follows the same reshape, one snapshot row per operation.
--   CustomQuestionDailyFeedback        NEW - free-text answers to a custom question.
--   CustomQuestionDailyFeedbackAudits  NEW - snapshot history of those answers.
--
-- Safe to run more than once: every statement is guarded, and the whole script is
-- one transaction, so a failure leaves the database untouched.
--
-- Prerequisites: scripts 01 to 05 in this folder must already have been run.
-- Target: PostgreSQL, database rrida. Run the file as a whole in pgAdmin, not
-- statement by statement, or the transaction boundary is lost.
-- =============================================================================

START TRANSACTION;

-- Pre-flight: the table this script reshapes has to exist, and it has to be empty.
-- CompanyId and UnitId are added NOT NULL DEFAULT 0, so existing rows would land on
-- company 0 / unit 0 and break the new foreign keys. Failing here says that plainly
-- instead of surfacing as a constraint violation forty statements later.
DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
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
    DROP INDEX IF EXISTS public."IX_CustomQuestions_Status_Window";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    DROP INDEX IF EXISTS public."IX_CustomQuestions_UsageContext";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    ALTER TABLE public."CustomQuestions" DROP CONSTRAINT IF EXISTS "CK_CustomQuestions_DailyConfig";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    ALTER TABLE public."CustomQuestions" DROP CONSTRAINT IF EXISTS "CK_CustomQuestions_TeamConfig";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    ALTER TABLE public."CustomQuestions" DROP CONSTRAINT IF EXISTS "CK_CustomQuestions_Window";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    ALTER TABLE public."CustomQuestions" DROP COLUMN IF EXISTS "CloverCategory";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    ALTER TABLE public."CustomQuestions" DROP COLUMN IF EXISTS "EffectiveFromUtc";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    ALTER TABLE public."CustomQuestions" DROP COLUMN IF EXISTS "Ordinal";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    ALTER TABLE public."CustomQuestions" DROP COLUMN IF EXISTS "SentimentType";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    ALTER TABLE public."CustomQuestions" DROP COLUMN IF EXISTS "SubCategory";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    ALTER TABLE public."CustomQuestions" DROP COLUMN IF EXISTS "TeamElement";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    ALTER TABLE public."CustomQuestions" DROP COLUMN IF EXISTS "UsageContext";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    ALTER TABLE public."CustomQuestions" DROP COLUMN IF EXISTS "Variant";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    ALTER TABLE public."CustomQuestionAudits" DROP COLUMN IF EXISTS "CloverCategory";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    ALTER TABLE public."CustomQuestionAudits" DROP COLUMN IF EXISTS "EffectiveFromUtc";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    ALTER TABLE public."CustomQuestionAudits" DROP COLUMN IF EXISTS "Ordinal";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    ALTER TABLE public."CustomQuestionAudits" DROP COLUMN IF EXISTS "SentimentType";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    ALTER TABLE public."CustomQuestionAudits" DROP COLUMN IF EXISTS "SubCategory";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    ALTER TABLE public."CustomQuestionAudits" DROP COLUMN IF EXISTS "TeamElement";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    ALTER TABLE public."CustomQuestionAudits" DROP COLUMN IF EXISTS "UsageContext";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    ALTER TABLE public."CustomQuestionAudits" DROP COLUMN IF EXISTS "Variant";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
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
    ALTER TABLE public."CustomQuestions" ADD COLUMN IF NOT EXISTS "CompanyId" bigint NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    ALTER TABLE public."CustomQuestions" ADD COLUMN IF NOT EXISTS "UnitId" bigint NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    ALTER TABLE public."CustomQuestionAudits" ADD COLUMN IF NOT EXISTS "CompanyId" bigint NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    ALTER TABLE public."CustomQuestionAudits" ADD COLUMN IF NOT EXISTS "UnitId" bigint NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
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
    CREATE INDEX IF NOT EXISTS "IX_CustomQuestions_Company_Unit" ON public."CustomQuestions" ("CompanyId", "UnitId") WHERE "IsDel" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    CREATE INDEX IF NOT EXISTS "IX_CustomQuestions_Status_ScheduledAt" ON public."CustomQuestions" ("Status", "ScheduledAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    CREATE INDEX IF NOT EXISTS "IX_CustomQuestions_UnitId" ON public."CustomQuestions" ("UnitId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    CREATE INDEX IF NOT EXISTS "IX_CustomQuestionDailyFeedback_CreatedAt" ON public."CustomQuestionDailyFeedback" ("CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    CREATE INDEX IF NOT EXISTS "IX_CustomQuestionDailyFeedback_Question" ON public."CustomQuestionDailyFeedback" ("CustomQuestionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    CREATE INDEX IF NOT EXISTS "IX_CustomQuestionDailyFeedbackAudits_Feedback_Version" ON public."CustomQuestionDailyFeedbackAudits" ("CustomQuestionDailyFeedbackId", "VersionNumber");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    CREATE INDEX IF NOT EXISTS "IX_CustomQuestionDailyFeedbackAudits_Operation" ON public."CustomQuestionDailyFeedbackAudits" ("OperationType");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_CustomQuestions_BusinessUnits_UnitId') THEN
        ALTER TABLE public."CustomQuestions" ADD CONSTRAINT "FK_CustomQuestions_BusinessUnits_UnitId" FOREIGN KEY ("UnitId") REFERENCES "BusinessUnits" ("Id") ON DELETE RESTRICT;
    END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_CustomQuestions_Companies_CompanyId') THEN
        ALTER TABLE public."CustomQuestions" ADD CONSTRAINT "FK_CustomQuestions_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE RESTRICT;
    END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260826090839_ReshapeCustomQuestions') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260826090839_ReshapeCustomQuestions', '9.0.5');
    END IF;
END $EF$;
COMMIT;

