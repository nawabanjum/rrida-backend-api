START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyFeedbacks" DROP CONSTRAINT "FK_TeamDailyFeedbacks_BusinessUnits_BusinessUnitId";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyFeedbacks" DROP CONSTRAINT "FK_TeamDailyFeedbacks_TeamDailyQuestionDeliveries_TeamDailyQue~";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyFeedbacks" DROP CONSTRAINT "FK_TeamDailyFeedbacks_TeamDailyQuestions_TeamDailyQuestionId";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyFeedbacks" DROP CONSTRAINT "FK_TeamDailyFeedbacks_TeamDailyReasons_TeamDailyReasonId";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyQuestions" DROP CONSTRAINT "FK_TeamDailyQuestions_BusinessUnits_OwnerBusinessUnitId";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyQuestions" DROP CONSTRAINT "FK_TeamDailyQuestions_Companies_OwnerCompanyId";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyQuestions" DROP CONSTRAINT "FK_TeamDailyQuestions_TeamQuestionSubCategories_TeamQuestionSu~";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyQuestionSpecifications" DROP CONSTRAINT "FK_TeamDailyQuestionSpecifications_TeamDailyQuestions_BaseTeam~";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyQuestionSpecifications" DROP CONSTRAINT "FK_TeamDailyQuestionSpecifications_TeamDailyQuestions_TeamDail~";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyReasons" DROP CONSTRAINT "FK_TeamDailyReasons_TeamDailyQuestions_TeamDailyQuestionId";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyReasons" DROP CONSTRAINT "FK_TeamDailyReasons_TeamQuestionAttributionTypes_TeamQuestionA~";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyReasons" DROP CONSTRAINT "FK_TeamDailyReasons_TeamStateIndicators_TeamStateIndicatorId";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    DROP TABLE "TeamDailyQuestionResponseReceipts";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    DROP TABLE "TeamQuestionAttributionTypes";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    DROP TABLE "TeamQuestionSubCategories";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    DROP TABLE "TeamStateIndicators";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    DROP TABLE "TeamDailyQuestionDeliveries";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    DROP TABLE "TeamQuestionMainCategories";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    DROP TABLE "TeamQuestionSamplingModes";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    DROP TABLE "TeamCustomQuestionSchedules";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    DROP TABLE "TeamGoals";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    DROP INDEX "IX_TeamDailyReasons_StateIndicator";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    DROP INDEX "IX_TeamDailyReasons_TeamQuestionAttributionTypeId";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    DROP INDEX "UX_TeamDailyReasons_Question_Sentiment_Position";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyReasons" DROP CONSTRAINT "CK_TeamDailyReasons_Position";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    DROP INDEX "IX_TeamDailyQuestionSpecifications_BaseTeamDailyQuestionId";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    DROP INDEX "UX_TeamDailyQuestionSpecifications_Ask_Variant";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    DROP INDEX "UX_TeamDailyQuestionSpecifications_Question";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    DROP INDEX "UX_TeamDailyQuestionSpecifications_RotationOrder";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyQuestionSpecifications" DROP CONSTRAINT "CK_TeamDailyQuestionSpecifications_Variant";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    DROP INDEX "IX_TeamDailyQuestions_OwnerBusinessUnitId";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    DROP INDEX "IX_TeamDailyQuestions_OwnerCompany";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    DROP INDEX "IX_TeamDailyQuestions_SubCategory_Active";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyQuestions" DROP CONSTRAINT "CK_TeamDailyQuestions_CustomOwnership";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    DROP INDEX "UX_TeamDailyMicroActions_Reason";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyFeedbacks" DROP CONSTRAINT "PK_TeamDailyFeedbacks";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    DROP INDEX "IX_TeamDailyFeedbacks_Delivery";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyReasons" DROP COLUMN "IsActive";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyReasons" DROP COLUMN "IsDel";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyReasons" DROP COLUMN "Position";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyReasons" DROP COLUMN "TeamQuestionAttributionTypeId";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyReasons" DROP COLUMN "TeamStateIndicatorId";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyQuestionSpecifications" DROP COLUMN "AskNumber";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyQuestionSpecifications" DROP COLUMN "BaseTeamDailyQuestionId";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyQuestionSpecifications" DROP COLUMN "EffectiveFromUtc";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyQuestionSpecifications" DROP COLUMN "EffectiveToUtc";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyQuestionSpecifications" DROP COLUMN "IsActive";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyQuestionSpecifications" DROP COLUMN "IsDel";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyQuestionSpecifications" DROP COLUMN "RequiresGoal";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyQuestionSpecifications" DROP COLUMN "RotationOrder";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyQuestions" DROP COLUMN "CreatedAtUtc";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyQuestions" DROP COLUMN "CreatedBy";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyQuestions" DROP COLUMN "IsActive";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyQuestions" DROP COLUMN "IsDel";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyQuestions" DROP COLUMN "OriginType";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyQuestions" DROP COLUMN "OwnerBusinessUnitId";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyQuestions" DROP COLUMN "OwnerCompanyId";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyMicroActions" DROP COLUMN "Body";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyMicroActions" DROP COLUMN "IsActive";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyMicroActions" DROP COLUMN "IsDel";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyMicroActions" DROP COLUMN "RoutesToManager";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyMicroActions" DROP COLUMN "Title";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyFeedbacks" DROP COLUMN "TeamDailyQuestionDeliveryId";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyFeedbacks" RENAME TO "TeamDailyFeedback";
    ALTER TABLE "TeamDailyFeedback" SET SCHEMA public;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyReasons" RENAME COLUMN "TeamDailyQuestionId" TO "SpecificationId";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyReasons" RENAME COLUMN "SentimentType" TO "TeamDailyReasonSentimentType";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyReasons" RENAME COLUMN "RequiresFreeText" TO "RequiresFreeTextFeedback";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyReasons" RENAME COLUMN "ReasonText" TO "Text";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyQuestionSpecifications" RENAME COLUMN "VariantType" TO "Variant";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyQuestions" RENAME COLUMN "TeamQuestionSubCategoryId" TO "ContentCultureId";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyQuestions" RENAME COLUMN "QuestionText" TO "Text";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER INDEX public."IX_TeamDailyFeedbacks_Unit_Submitted" RENAME TO "IX_TeamDailyFeedback_Unit_Submitted";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER INDEX public."IX_TeamDailyFeedbacks_TeamDailyQuestionId" RENAME TO "IX_TeamDailyFeedback_Question";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER INDEX public."IX_TeamDailyFeedbacks_Reason" RENAME TO "IX_TeamDailyFeedback_Reason";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyReasons" ADD "ContentCultureId" bigint NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyReasons" ADD "TeamState" integer;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyQuestionSpecifications" ADD "ContentCultureId" bigint NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyQuestionSpecifications" ADD "Ordinal" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyQuestionSpecifications" ADD "SubCategory" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyQuestionSpecifications" ADD "TeamElement" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyMicroActions" ADD "ContentCultureId" bigint NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyMicroActions" ADD "Text" character varying(1000) NOT NULL DEFAULT '';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE public."TeamDailyFeedback" ADD CONSTRAINT "PK_TeamDailyFeedback" PRIMARY KEY ("Id");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    CREATE TABLE public."CustomQuestionAudits" (
        "AuditId" integer GENERATED BY DEFAULT AS IDENTITY,
        "CustomQuestionId" bigint NOT NULL,
        "Text" character varying(1000) NOT NULL,
        "ContentCultureId" bigint NOT NULL,
        "UsageContext" integer NOT NULL,
        "SentimentType" integer,
        "CloverCategory" integer,
        "TeamElement" integer,
        "SubCategory" integer,
        "Variant" integer,
        "Ordinal" integer,
        "EffectiveFromUtc" timestamp with time zone,
        "EffectiveToUtc" timestamp with time zone,
        "Status" integer NOT NULL,
        "ChangeSummary" character varying(1000),
        "CreatedBy" integer,
        "UpdatedBy" integer,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        "IsDel" boolean NOT NULL,
        "LoggedAt" timestamp with time zone NOT NULL,
        "VersionNumber" integer NOT NULL,
        "OperationType" character varying(20) NOT NULL,
        CONSTRAINT "PK_CustomQuestionAudits" PRIMARY KEY ("AuditId")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    CREATE TABLE public."CustomQuestions" (
        "Id" bigint GENERATED BY DEFAULT AS IDENTITY,
        "Text" character varying(1000) NOT NULL,
        "ContentCultureId" bigint NOT NULL,
        "UsageContext" integer NOT NULL,
        "SentimentType" integer,
        "CloverCategory" integer,
        "TeamElement" integer,
        "SubCategory" integer,
        "Variant" integer,
        "Ordinal" integer,
        "EffectiveFromUtc" timestamp with time zone,
        "EffectiveToUtc" timestamp with time zone,
        "Status" integer NOT NULL,
        "CreatedBy" integer,
        "UpdatedBy" integer,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        "IsDel" boolean NOT NULL,
        CONSTRAINT "PK_CustomQuestions" PRIMARY KEY ("Id"),
        CONSTRAINT "CK_CustomQuestions_DailyConfig" CHECK (("UsageContext" IN (1,3) AND "CloverCategory" IS NOT NULL) OR ("UsageContext" = 2 AND "CloverCategory" IS NULL)),
        CONSTRAINT "CK_CustomQuestions_TeamConfig" CHECK (("UsageContext" IN (2,3) AND "TeamElement" IS NOT NULL AND "SubCategory" IS NOT NULL AND "Variant" IS NOT NULL) OR ("UsageContext" = 1 AND "TeamElement" IS NULL AND "SubCategory" IS NULL AND "Variant" IS NULL)),
        CONSTRAINT "CK_CustomQuestions_Window" CHECK ("EffectiveFromUtc" IS NULL OR "EffectiveToUtc" IS NULL OR "EffectiveToUtc" >= "EffectiveFromUtc"),
        CONSTRAINT "FK_CustomQuestions_ContentCultures_ContentCultureId" FOREIGN KEY ("ContentCultureId") REFERENCES "ContentCultures" ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    CREATE INDEX "IX_TeamDailyReasons_ContentCultureId" ON "TeamDailyReasons" ("ContentCultureId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    CREATE INDEX "IX_TeamDailyReasons_Specification" ON "TeamDailyReasons" ("SpecificationId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    CREATE INDEX "IX_TeamDailyReasons_TeamState" ON "TeamDailyReasons" ("TeamState");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    CREATE INDEX "IX_TeamDailyQuestionSpecifications_ContentCultureId" ON "TeamDailyQuestionSpecifications" ("ContentCultureId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    CREATE INDEX "IX_TeamDailyQuestionSpecifications_TeamDailyQuestionId" ON "TeamDailyQuestionSpecifications" ("TeamDailyQuestionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    CREATE INDEX "IX_TeamDailyQuestionSpecifications_TeamElement" ON "TeamDailyQuestionSpecifications" ("TeamElement");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    CREATE UNIQUE INDEX "UX_TeamDailyQuestionSpecifications_SubCategory_Variant_Ordinal_Culture" ON "TeamDailyQuestionSpecifications" ("SubCategory", "Variant", "Ordinal", "ContentCultureId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    CREATE INDEX "IX_TeamDailyQuestions_ContentCultureId" ON "TeamDailyQuestions" ("ContentCultureId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    CREATE INDEX "IX_TeamDailyMicroActions_ContentCultureId" ON "TeamDailyMicroActions" ("ContentCultureId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    CREATE INDEX "IX_TeamDailyMicroActions_Reason" ON "TeamDailyMicroActions" ("TeamDailyReasonId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    CREATE INDEX "IX_CustomQuestionAudits_Operation" ON public."CustomQuestionAudits" ("OperationType");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    CREATE INDEX "IX_CustomQuestionAudits_Question_LoggedAt" ON public."CustomQuestionAudits" ("CustomQuestionId", "LoggedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    CREATE INDEX "IX_CustomQuestions_ContentCultureId" ON public."CustomQuestions" ("ContentCultureId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    CREATE INDEX "IX_CustomQuestions_Status_Window" ON public."CustomQuestions" ("Status", "EffectiveFromUtc", "EffectiveToUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    CREATE INDEX "IX_CustomQuestions_UsageContext" ON public."CustomQuestions" ("UsageContext") WHERE "IsDel" = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE public."TeamDailyFeedback" ADD CONSTRAINT "FK_TeamDailyFeedback_BusinessUnits_BusinessUnitId" FOREIGN KEY ("BusinessUnitId") REFERENCES "BusinessUnits" ("Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE public."TeamDailyFeedback" ADD CONSTRAINT "FK_TeamDailyFeedback_TeamDailyQuestions_TeamDailyQuestionId" FOREIGN KEY ("TeamDailyQuestionId") REFERENCES "TeamDailyQuestions" ("Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE public."TeamDailyFeedback" ADD CONSTRAINT "FK_TeamDailyFeedback_TeamDailyReasons_TeamDailyReasonId" FOREIGN KEY ("TeamDailyReasonId") REFERENCES "TeamDailyReasons" ("Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyMicroActions" ADD CONSTRAINT "FK_TeamDailyMicroActions_ContentCultures_ContentCultureId" FOREIGN KEY ("ContentCultureId") REFERENCES "ContentCultures" ("Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyQuestions" ADD CONSTRAINT "FK_TeamDailyQuestions_ContentCultures_ContentCultureId" FOREIGN KEY ("ContentCultureId") REFERENCES "ContentCultures" ("Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyQuestionSpecifications" ADD CONSTRAINT "FK_TeamDailyQuestionSpecifications_ContentCultures_ContentCult~" FOREIGN KEY ("ContentCultureId") REFERENCES "ContentCultures" ("Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyQuestionSpecifications" ADD CONSTRAINT "FK_TeamDailyQuestionSpecifications_TeamDailyQuestions_TeamDail~" FOREIGN KEY ("TeamDailyQuestionId") REFERENCES "TeamDailyQuestions" ("Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyReasons" ADD CONSTRAINT "FK_TeamDailyReasons_ContentCultures_ContentCultureId" FOREIGN KEY ("ContentCultureId") REFERENCES "ContentCultures" ("Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    ALTER TABLE "TeamDailyReasons" ADD CONSTRAINT "FK_TeamDailyReasons_TeamDailyQuestionSpecifications_Specificat~" FOREIGN KEY ("SpecificationId") REFERENCES "TeamDailyQuestionSpecifications" ("Id") ON DELETE CASCADE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260824155138_RedesignTeamQuestionsToMatchLegacy') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260824155138_RedesignTeamQuestionsToMatchLegacy', '9.0.5');
    END IF;
END $EF$;
COMMIT;

