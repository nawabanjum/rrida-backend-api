using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CloverBackoffice.Repository.Migrations
{
    /// <summary>
    /// CompanyId -> ProfileId, the second half of the Companies -> Profiles redesign.
    ///
    /// Hand-written rather than left as scaffolded, for the same reason as
    /// 20260907091110_RenameCompaniesToProfiles. EF generated a mixture: plain
    /// RenameColumn for the tables that only had a foreign key property, but
    /// AddColumn("ProfileId") + AlterColumn("CompanyId") for the six that also had a
    /// navigation property. That pattern adds an empty column and leaves the
    /// populated one behind, so CompanyLicenses, CompanySubscriptions, Payments,
    /// PromoCodes, PromoCodeUsages and AzureMarketplaceSubscription would have lost
    /// their foreign keys' values. It also dropped indexes and constraints that this
    /// database does not have.
    ///
    /// Every statement below is explicit SQL that checks current state first, so the
    /// migration is re-runnable and cannot lose data. It is identical to
    /// Scripts/Profiles/02_RenameCompanyIdToProfileId.idempotent.sql, which is the
    /// copy to run by hand in pgAdmin; applying one makes the other a no-op.
    ///
    /// CompanyTeams is deliberately excluded - dead table, 0 rows, nothing writes it
    /// - and the CompanyTeam entity keeps its CompanyId property to match.
    /// </summary>
    public partial class RenameCompanyIdToProfileId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 0. Guard: the first migration must have run, or there is nothing for
            //    these foreign keys to point at.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF to_regclass('public."Profiles"') IS NULL THEN
                        RAISE EXCEPTION
                            'public."Profiles" does not exist. Apply RenameCompaniesToProfiles first.';
                    END IF;
                END $$;
                """);

            // 1. The column rename itself, across 23 tables. The list is explicit so
            //    that a table added later is a deliberate decision rather than
            //    something this migration silently sweeps up. LLMReportSchedules and
            //    LLMReportScheduleAudits are listed because the EF model declares
            //    them; they do not exist in this database, and the guard skips them.
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    t text;
                    targets text[] := ARRAY[
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
                        END IF;
                    END LOOP;
                END $$;
                """);

            // 2. Foreign key names. A column rename leaves them working but keeps
            //    their old names, which would then disagree with the EF model.
            migrationBuilder.Sql("""
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
                """);

            // 3. Index names. Four single-column, plus the three composite
            //    (CompanyId, UnitId) indexes carrying the ownership pattern that
            //    CustomQuestions, TeamGoals and TeamSetups share. The last line
            //    clears a leftover from the first migration.
            migrationBuilder.Sql("""
                ALTER INDEX IF EXISTS public."IX_AzureMarketplaceSubscription_CompanyId" RENAME TO "IX_AzureMarketplaceSubscription_ProfileId";
                ALTER INDEX IF EXISTS public."IX_CompanyLicenses_CompanyId"              RENAME TO "IX_CompanyLicenses_ProfileId";
                ALTER INDEX IF EXISTS public."IX_CompanySubscriptions_CompanyId"         RENAME TO "IX_CompanySubscriptions_ProfileId";
                ALTER INDEX IF EXISTS public."IX_Payments_CompanyId"                     RENAME TO "IX_Payments_ProfileId";

                ALTER INDEX IF EXISTS public."IX_CustomQuestions_Company_Unit"           RENAME TO "IX_CustomQuestions_Profile_Unit";
                ALTER INDEX IF EXISTS public."IX_TeamGoals_Company_Unit"                 RENAME TO "IX_TeamGoals_Profile_Unit";
                ALTER INDEX IF EXISTS public."IX_TeamSetups_Company_Unit"                RENAME TO "IX_TeamSetups_Profile_Unit";

                ALTER INDEX IF EXISTS public."IX_Companies_CompanyShortCode"             RENAME TO "IX_Profiles_ProfileShortCode";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER INDEX IF EXISTS public."IX_Profiles_ProfileShortCode"              RENAME TO "IX_Companies_CompanyShortCode";

                ALTER INDEX IF EXISTS public."IX_CustomQuestions_Profile_Unit"           RENAME TO "IX_CustomQuestions_Company_Unit";
                ALTER INDEX IF EXISTS public."IX_TeamGoals_Profile_Unit"                 RENAME TO "IX_TeamGoals_Company_Unit";
                ALTER INDEX IF EXISTS public."IX_TeamSetups_Profile_Unit"                RENAME TO "IX_TeamSetups_Company_Unit";

                ALTER INDEX IF EXISTS public."IX_AzureMarketplaceSubscription_ProfileId" RENAME TO "IX_AzureMarketplaceSubscription_CompanyId";
                ALTER INDEX IF EXISTS public."IX_CompanyLicenses_ProfileId"              RENAME TO "IX_CompanyLicenses_CompanyId";
                ALTER INDEX IF EXISTS public."IX_CompanySubscriptions_ProfileId"         RENAME TO "IX_CompanySubscriptions_CompanyId";
                ALTER INDEX IF EXISTS public."IX_Payments_ProfileId"                     RENAME TO "IX_Payments_CompanyId";
                """);

            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    r record;
                BEGIN
                    FOR r IN
                        SELECT * FROM (VALUES
                            ('CompanyLicenses',      'FK_CompanyLicenses_Profiles_ProfileId',      'FK_CompanyLicenses_Profiles_CompanyId'),
                            ('CompanySubscriptions', 'FK_CompanySubscriptions_Profiles_ProfileId', 'FK_CompanySubscriptions_Profiles_CompanyId'),
                            ('CustomQuestions',      'FK_CustomQuestions_Profiles_ProfileId',      'FK_CustomQuestions_Profiles_CompanyId'),
                            ('Payments',             'FK_Payments_Profiles_ProfileId',             'FK_Payments_Profiles_CompanyId'),
                            ('TeamGoals',            'FK_TeamGoals_Profiles_ProfileId',            'FK_TeamGoals_Profiles_CompanyId'),
                            ('TeamSetups',           'FK_TeamSetups_Profiles_ProfileId',           'FK_TeamSetups_Profiles_CompanyId'),
                            ('AzureMarketplaceSubscription', 'FK_AzureMarketplaceSubscription_Profiles_ProfileId', 'FK_AzureMarketplaceSubscription_Profiles_CompanyId'),
                            ('PromoCodes',           'FK_PromoCodes_Profiles_ProfileId',           'FK_PromoCodes_Profiles_CompanyId'),
                            ('PromoCodeUsages',      'FK_PromoCodeUsages_Profiles_ProfileId',      'FK_PromoCodeUsages_Profiles_CompanyId'),
                            ('LLMReportSchedules',   'FK_LLMReportSchedules_Profiles_ProfileId',   'FK_LLMReportSchedules_Profiles_CompanyId')
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
                """);

            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    t text;
                    targets text[] := ARRAY[
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
                                       WHERE table_schema = 'public' AND table_name = t AND column_name = 'ProfileId')
                           AND NOT EXISTS (SELECT 1 FROM information_schema.columns
                                       WHERE table_schema = 'public' AND table_name = t AND column_name = 'CompanyId')
                        THEN
                            EXECUTE format('ALTER TABLE public.%I RENAME COLUMN "ProfileId" TO "CompanyId"', t);
                        END IF;
                    END LOOP;
                END $$;
                """);
        }
    }
}
