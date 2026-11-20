-- Baseline the EF migration history after restoring the CloverEra production
-- dump into an RRIDA database.
--
-- Why this exists: the production database was built by hand-run SQL scripts,
-- so the dump contains no __EFMigrationsHistory table at all (verified: zero
-- matching TOC entries). Without baselining, the EF bundle in the pipeline
-- tries to apply all 15 migrations against a schema that already has those
-- objects and fails on the first CREATE.
--
-- Dump layout: tables live in "public" (79 of them) and a secondary "export"
-- schema (13 copies). cloverera_admin is the owning role, not a schema, so EF's
-- default public schema is already correct and needs no search_path override.
--
-- Run once, immediately after pg_restore, connected to the RRIDA database:
--   psql "host=... dbname=rrida user=rridaadmin sslmode=require" -f baseline-migrations.sql

-- 1. Verify the restore actually landed before claiming anything about it.
DO $$
DECLARE
    table_count int;
BEGIN
    SELECT count(*) INTO table_count
    FROM information_schema.tables
    WHERE table_schema = 'public';

    IF table_count < 50 THEN
        RAISE EXCEPTION 'Only % tables in public — restore looks incomplete, stopping.', table_count;
    END IF;

    RAISE NOTICE 'public has % tables.', table_count;
END $$;

-- 2. Spot-check objects created by the most recent migrations. A warning here
--    means the dump predates that migration, so it must NOT be marked applied:
--    remove it from the INSERT below and let EF apply it for real.
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'BusinessUnits' AND column_name = 'TeamSize'
    ) THEN
        RAISE WARNING 'BusinessUnits.TeamSize missing — 20260811123252_AddTeamSizeToBusinessUnit is NOT applied.';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM information_schema.tables
        WHERE table_schema = 'public' AND table_name = 'EranCoachManagerMemories'
    ) THEN
        RAISE WARNING 'EranCoachManagerMemories missing — 20260717093650_AddEranCoachManagerMemory is NOT applied.';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM information_schema.tables
        WHERE table_schema = 'public' AND table_name = 'DocumentEmbeddings'
    ) THEN
        RAISE WARNING 'DocumentEmbeddings missing — the pgvector migrations are NOT applied.';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'vector') THEN
        RAISE WARNING 'vector extension missing — CREATE EXTENSION vector before the embeddings migrations.';
    END IF;
END $$;

-- 3. Create the history table EF expects, in its default schema.
CREATE TABLE IF NOT EXISTS public."__EFMigrationsHistory" (
    "MigrationId"    character varying(150) NOT NULL,
    "ProductVersion" character varying(32)  NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

-- 4. Mark every migration already reflected in the restored schema as applied.
--    ON CONFLICT makes this safe to re-run.
INSERT INTO public."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES
    ('20260207225815_AddDocumentEmbeddingsWithPgVector',           '9.0.4'),
    ('20260208122000_AddFileNameAndChunkIndexToDocumentEmbedding', '9.0.4'),
    ('20260427100000_AddInsightPublishLog',                        '9.0.4'),
    ('20260427110000_AddAuditFieldsToInsightPublishLog',           '9.0.4'),
    ('20260427120000_AddOperationTypeToInsightPublishLog',         '9.0.4'),
    ('20260427130000_RemoveInsightPublishLogCascade',              '9.0.4'),
    ('20260512103740_AddSystemDefaultSettings',                    '9.0.4'),
    ('20260512122112_AddScheduleDateFromTo',                       '9.0.4'),
    ('20260512130818_RemoveScheduleDateFromTo',                    '9.0.4'),
    ('20260512144744_AddLastReportRunAt',                          '9.0.4'),
    ('20260513145426_RenameToLLMReportSchedules',                  '9.0.4'),
    ('20260601133926_AddStripeProductIdToCompanySubscription',     '9.0.4'),
    ('20260603094118_AddNotesToCompanyLicense',                    '9.0.4'),
    ('20260717093650_AddEranCoachManagerMemory',                   '9.0.4'),
    ('20260811123252_AddTeamSizeToBusinessUnit',                   '9.0.4')
ON CONFLICT ("MigrationId") DO NOTHING;

-- 5. Report the result.
SELECT "MigrationId" FROM public."__EFMigrationsHistory" ORDER BY "MigrationId";
