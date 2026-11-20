-- ===========================================================================
-- 02_RenameCompanyIdToProfileId.idempotent.sql
--
-- Second half of the Companies -> Profiles redesign. Script 01 renamed the
-- table and its own columns; this renames the foreign key column that points
-- at it, everywhere it still reads "CompanyId".
--
--   * 23 tables get CompanyId -> ProfileId
--   * the 7 audit tables are included, so each row snapshot keeps mirroring
--     its source table column for column
--   * 6 foreign keys and 7 indexes are renamed to match
--
-- DELIBERATELY NOT TOUCHED:
--
--   * public."CompanyTeams" - dead table, 0 rows, nothing writes it. Left
--     exactly as it is, along with the CompanyTeam entity, so the two still
--     agree.
--   * Table names. CompanyLicenses, CompanySubscriptions, CompanyLinks and
--     CompanyTeams keep their names; only the column changes. Renaming the
--     tables is a separate decision.
--   * Users.Type values, Stripe metadata keys and stored AuditLogs JSON, all
--     of which contain the text "company" as DATA rather than as a schema
--     name. See the notes in tools/rename-companyid-to-profileid.pl.
--
-- Safe to run more than once: every statement checks current state first.
--
-- Identical in effect to EF migration
-- 20260907102423_RenameCompanyIdToProfileId. Run either one; running the
-- second afterwards is a no-op by design.
--
-- Take a backup first. This renames columns on 23 tables.
-- ===========================================================================

BEGIN;

-- ---------------------------------------------------------------------------
-- 0. Guard: script 01 must have run, or the target of these keys does not
--    exist yet.
-- ---------------------------------------------------------------------------
DO $$
BEGIN
    IF to_regclass('public."Profiles"') IS NULL THEN
        RAISE EXCEPTION
            'public."Profiles" does not exist. Run 01_RenameCompaniesToProfiles.idempotent.sql first.';
    END IF;
END $$;

-- ---------------------------------------------------------------------------
-- 1. CompanyId -> ProfileId on every table that carries it, except
--    CompanyTeams.
--
--    The list is explicit rather than discovered, so a table added later is a
--    deliberate decision rather than something this script silently sweeps up.
--    LLMReportSchedules and LLMReportScheduleAudits are listed because the EF
--    model declares them; they do not exist in the database today, and the
--    guard below simply skips them.
-- ---------------------------------------------------------------------------
DO $$
DECLARE
    t text;
    targets text[] := ARRAY[
        -- source tables
        'ActionHubs',
        'AzureMarketplaceSubscription',
        'CloveraPersonalSnapshots',
        'CompanyLicenses',
        'CompanyLinks',
        'CompanySubscriptions',
        'CustomQuestions',
        'EranCoachConversations',
        'LLMReportSchedules',
        'Notifications',
        'Opportunities',
        'Payments',
        'PromoCodes',
        'PromoCodeUsages',
        'TeamGoals',
        'TeamSetups',
        'Users',
        -- audit tables: row snapshots that must keep mirroring their source
        'CompanyLicenseAuditLogs',
        'CompanySubscriptionAuditLogs',
        'CustomQuestionAudits',
        'LLMReportScheduleAudits',
        'PaymentAuditLogs',
        'PromoCodeAuditLogs',
        'PromoCodeUsageAuditLogs',
        'TeamGoalAudits'
    ];
BEGIN
    FOREACH t IN ARRAY targets
    LOOP
        IF to_regclass(format('public.%I', t)) IS NOT NULL
           AND EXISTS (SELECT 1 FROM information_schema.columns
                       WHERE table_schema = 'public' AND table_name = t AND column_name = 'CompanyId')
           AND NOT EXISTS (SELECT 1 FROM information_schema.columns
                       WHERE table_schema = 'public' AND table_name = t AND column_name = 'ProfileId')
        THEN
            EXECUTE format('ALTER TABLE public.%I RENAME COLUMN "CompanyId" TO "ProfileId"', t);
            RAISE NOTICE 'renamed %.CompanyId -> ProfileId', t;
        END IF;
    END LOOP;
END $$;

-- ---------------------------------------------------------------------------
-- 2. Foreign keys. The constraints still work after a column rename but keep
--    their old names, which would then disagree with what EF Core expects.
-- ---------------------------------------------------------------------------
DO $$
DECLARE
    r record;
BEGIN
    FOR r IN
        SELECT * FROM (VALUES
            ('CompanyLicenses',      'FK_CompanyLicenses_Profiles_CompanyId',      'FK_CompanyLicenses_Profiles_ProfileId'),
            ('CompanySubscriptions', 'FK_CompanySubscriptions_Profiles_CompanyId', 'FK_CompanySubscriptions_Profiles_ProfileId'),
            ('CustomQuestions',      'FK_CustomQuestions_Profiles_CompanyId',      'FK_CustomQuestions_Profiles_ProfileId'),
            ('Payments',             'FK_Payments_Profiles_CompanyId',             'FK_Payments_Profiles_ProfileId'),
            ('TeamGoals',            'FK_TeamGoals_Profiles_CompanyId',            'FK_TeamGoals_Profiles_ProfileId'),
            ('TeamSetups',           'FK_TeamSetups_Profiles_CompanyId',           'FK_TeamSetups_Profiles_ProfileId'),
            -- declared by the EF model, absent from this database today
            ('AzureMarketplaceSubscription', 'FK_AzureMarketplaceSubscription_Profiles_CompanyId', 'FK_AzureMarketplaceSubscription_Profiles_ProfileId'),
            ('PromoCodes',           'FK_PromoCodes_Profiles_CompanyId',           'FK_PromoCodes_Profiles_ProfileId'),
            ('PromoCodeUsages',      'FK_PromoCodeUsages_Profiles_CompanyId',      'FK_PromoCodeUsages_Profiles_ProfileId'),
            ('LLMReportSchedules',   'FK_LLMReportSchedules_Profiles_CompanyId',   'FK_LLMReportSchedules_Profiles_ProfileId')
        ) AS t(table_name, old_name, new_name)
    LOOP
        IF EXISTS (SELECT 1 FROM pg_constraint c
                   JOIN pg_class rel ON rel.oid = c.conrelid
                   JOIN pg_namespace n ON n.oid = rel.relnamespace
                   WHERE n.nspname = 'public'
                     AND rel.relname = r.table_name
                     AND c.conname = r.old_name)
           AND NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = r.new_name)
        THEN
            EXECUTE format('ALTER TABLE public.%I RENAME CONSTRAINT %I TO %I',
                           r.table_name, r.old_name, r.new_name);
        END IF;
    END LOOP;
END $$;

-- ---------------------------------------------------------------------------
-- 3. Indexes. Four single-column, and the three composite
--    (CompanyId, UnitId) indexes that carry the ownership pattern
--    CustomQuestions, TeamGoals and TeamSetups share.
-- ---------------------------------------------------------------------------
ALTER INDEX IF EXISTS public."IX_AzureMarketplaceSubscription_CompanyId" RENAME TO "IX_AzureMarketplaceSubscription_ProfileId";
ALTER INDEX IF EXISTS public."IX_CompanyLicenses_CompanyId"              RENAME TO "IX_CompanyLicenses_ProfileId";
ALTER INDEX IF EXISTS public."IX_CompanySubscriptions_CompanyId"         RENAME TO "IX_CompanySubscriptions_ProfileId";
ALTER INDEX IF EXISTS public."IX_Payments_CompanyId"                     RENAME TO "IX_Payments_ProfileId";

ALTER INDEX IF EXISTS public."IX_CustomQuestions_Company_Unit"           RENAME TO "IX_CustomQuestions_Profile_Unit";
ALTER INDEX IF EXISTS public."IX_TeamGoals_Company_Unit"                 RENAME TO "IX_TeamGoals_Profile_Unit";
ALTER INDEX IF EXISTS public."IX_TeamSetups_Company_Unit"                RENAME TO "IX_TeamSetups_Profile_Unit";

-- Left over from script 01: a unique index on ProfileShortCode that still
-- carries the old table name. Cosmetic, but this is the tidy-up pass.
ALTER INDEX IF EXISTS public."IX_Companies_CompanyShortCode"             RENAME TO "IX_Profiles_ProfileShortCode";

COMMIT;

-- ---------------------------------------------------------------------------
-- Verification. Run after the transaction commits.
-- ---------------------------------------------------------------------------
-- -- Should return only CompanyTeams:
-- SELECT table_name FROM information_schema.columns
--  WHERE table_schema = 'public' AND column_name = 'CompanyId'
--  ORDER BY table_name;
--
-- -- Should return 23 rows:
-- SELECT table_name FROM information_schema.columns
--  WHERE table_schema = 'public' AND column_name = 'ProfileId'
--  ORDER BY table_name;
--
-- -- Should return nothing:
-- SELECT conname FROM pg_constraint WHERE conname LIKE '%\_CompanyId';
-- SELECT indexname FROM pg_indexes
--  WHERE schemaname = 'public' AND indexname LIKE '%CompanyId%';
