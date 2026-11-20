-- =============================================================================
-- 06_PreCheck.sql
--
-- Read-only. Run this BEFORE 06_ReshapeCustomQuestions.idempotent.sql to see
-- whether the database is in the state that script expects.
--
-- Run ROLLBACK; first if a previous attempt left an aborted transaction open.
-- =============================================================================

-- 1. Which migrations does this database think it has?
--    01-05 must all be present before 06 runs.
SELECT "MigrationId", "ProductVersion"
FROM   "__EFMigrationsHistory"
WHERE  "MigrationId" >= '20260824'
ORDER  BY "MigrationId";

-- 2. Do the Custom Question tables exist yet?
SELECT table_name
FROM   information_schema.tables
WHERE  table_schema = 'public'
  AND  table_name IN ('CustomQuestions',
                      'CustomQuestionAudits',
                      'CustomQuestionDailyFeedback',
                      'CustomQuestionDailyFeedbackAudits',
                      'TeamQuestionCategories',
                      'TeamDailyFeedbackAuditLogs')
ORDER  BY table_name;

-- 3. What shape is CustomQuestions in right now?
SELECT column_name, data_type, is_nullable
FROM   information_schema.columns
WHERE  table_schema = 'public' AND table_name = 'CustomQuestions'
ORDER  BY ordinal_position;

-- 4. Are there rows? Anything above 0 stops the reshape: CompanyId and UnitId
--    are added NOT NULL DEFAULT 0 and the new foreign keys would reject them.
SELECT count(*) AS custom_question_rows FROM public."CustomQuestions";
SELECT count(*) AS custom_question_audit_rows FROM public."CustomQuestionAudits";

-- 5. Which constraints and indexes are still on the table?
SELECT conname, contype
FROM   pg_constraint
WHERE  conrelid = 'public."CustomQuestions"'::regclass
ORDER  BY conname;

SELECT indexname
FROM   pg_indexes
WHERE  schemaname = 'public' AND tablename = 'CustomQuestions'
ORDER  BY indexname;
