using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CloverBackoffice.Repository.Migrations
{
    /// <summary>
    /// Companies -> Profiles.
    ///
    /// Hand-written rather than left as scaffolded. EF sees a renamed entity type
    /// as "drop Companies, create Profiles" and generated exactly that, which would
    /// have destroyed every company row. It also generated drops for several
    /// foreign keys that the EF model declares but this database never had. Every
    /// statement below is therefore explicit SQL that checks the object's current
    /// state first, so the migration is re-runnable and cannot delete data.
    ///
    /// The statements are identical to
    /// Scripts/Profiles/01_RenameCompaniesToProfiles.idempotent.sql, which is the
    /// copy to run by hand in pgAdmin. Applying one makes the other a no-op.
    /// </summary>
    public partial class RenameCompaniesToProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 0. Guard: refuse to run against a database that is not this schema.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF to_regclass('public."Companies"') IS NULL
                       AND to_regclass('public."Profiles"') IS NULL THEN
                        RAISE EXCEPTION
                            'Neither public."Companies" nor public."Profiles" exists. This is not the Clover back-office schema.';
                    END IF;
                END $$;
                """);

            // 1. Companies -> Profiles, and its primary key.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF to_regclass('public."Companies"') IS NOT NULL
                       AND to_regclass('public."Profiles"') IS NULL THEN
                        ALTER TABLE public."Companies" RENAME TO "Profiles";
                    END IF;

                    IF EXISTS (SELECT 1 FROM pg_constraint
                               WHERE conname = 'PK_Companies'
                                 AND conrelid = 'public."Profiles"'::regclass) THEN
                        ALTER TABLE public."Profiles" RENAME CONSTRAINT "PK_Companies" TO "PK_Profiles";
                    END IF;
                END $$;
                """);

            // 2. Column renames on Profiles: Name -> ProfileName,
            //    CompanyShortCode -> ProfileShortCode.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_schema = 'public' AND table_name = 'Profiles' AND column_name = 'Name')
                       AND NOT EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_schema = 'public' AND table_name = 'Profiles' AND column_name = 'ProfileName') THEN
                        ALTER TABLE public."Profiles" RENAME COLUMN "Name" TO "ProfileName";
                    END IF;

                    IF EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_schema = 'public' AND table_name = 'Profiles' AND column_name = 'CompanyShortCode')
                       AND NOT EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_schema = 'public' AND table_name = 'Profiles' AND column_name = 'ProfileShortCode') THEN
                        ALTER TABLE public."Profiles" RENAME COLUMN "CompanyShortCode" TO "ProfileShortCode";
                    END IF;
                END $$;
                """);

            // IX_Companies_NameInvariant is not renamed: its column is dropped in
            // step 7, which drops the index with it.
            migrationBuilder.Sql("""
                ALTER INDEX IF EXISTS public."IX_Companies_Name"      RENAME TO "IX_Profiles_ProfileName";
                ALTER INDEX IF EXISTS public."IX_Companies_PartnerId" RENAME TO "IX_Profiles_PartnerId";
                ALTER INDEX IF EXISTS public."IX_Companies_ContactId" RENAME TO "IX_Profiles_ContactId";
                """);

            // 3. Merge the Contacts row into Profiles.
            migrationBuilder.Sql("""
                ALTER TABLE public."Profiles" ADD COLUMN IF NOT EXISTS "ContactEmailAddress" character varying(254);
                ALTER TABLE public."Profiles" ADD COLUMN IF NOT EXISTS "ContactFirstName"    character varying(60);
                ALTER TABLE public."Profiles" ADD COLUMN IF NOT EXISTS "ContactLastName"     character varying(60);
                """);

            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF to_regclass('public."Contacts"') IS NOT NULL
                       AND EXISTS (SELECT 1 FROM information_schema.columns
                                   WHERE table_schema = 'public' AND table_name = 'Profiles' AND column_name = 'ContactId') THEN
                        UPDATE public."Profiles" p
                           SET "ContactEmailAddress" = c."EmailAddress",
                               "ContactFirstName"    = c."FirstName",
                               "ContactLastName"     = c."LastName"
                          FROM public."Contacts" c
                         WHERE c."Id" = p."ContactId"
                           AND p."ContactEmailAddress" IS NULL;
                    END IF;
                END $$;
                """);

            // 4. Merge the Contacts row into BusinessUnits. Rows were shared there -
            //    one contact could serve dozens of units - so copying the values in
            //    gives each unit its own and lets Contacts go.
            migrationBuilder.Sql("""
                ALTER TABLE public."BusinessUnits" ADD COLUMN IF NOT EXISTS "ContactEmailAddress" character varying(254);
                ALTER TABLE public."BusinessUnits" ADD COLUMN IF NOT EXISTS "ContactFirstName"    character varying(60);
                ALTER TABLE public."BusinessUnits" ADD COLUMN IF NOT EXISTS "ContactLastName"     character varying(60);
                """);

            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF to_regclass('public."Contacts"') IS NOT NULL
                       AND EXISTS (SELECT 1 FROM information_schema.columns
                                   WHERE table_schema = 'public' AND table_name = 'BusinessUnits' AND column_name = 'ContactId') THEN
                        UPDATE public."BusinessUnits" b
                           SET "ContactEmailAddress" = c."EmailAddress",
                               "ContactFirstName"    = c."FirstName",
                               "ContactLastName"     = c."LastName"
                          FROM public."Contacts" c
                         WHERE c."Id" = b."ContactId"
                           AND b."ContactEmailAddress" IS NULL;
                    END IF;
                END $$;
                """);

            // 5. BusinessUnits.CompanyId -> ProfileId, and Subscriptions.CompanyId
            //    too. Only these two: every other table keeps "CompanyId", because
            //    that name reaches API payloads the admin front end and the mobile
            //    app read.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_schema = 'public' AND table_name = 'BusinessUnits' AND column_name = 'CompanyId')
                       AND NOT EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_schema = 'public' AND table_name = 'BusinessUnits' AND column_name = 'ProfileId') THEN
                        ALTER TABLE public."BusinessUnits" RENAME COLUMN "CompanyId" TO "ProfileId";
                    END IF;

                    IF EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_schema = 'public' AND table_name = 'Subscriptions' AND column_name = 'CompanyId')
                       AND NOT EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_schema = 'public' AND table_name = 'Subscriptions' AND column_name = 'ProfileId') THEN
                        ALTER TABLE public."Subscriptions" RENAME COLUMN "CompanyId" TO "ProfileId";
                    END IF;
                END $$;
                """);

            migrationBuilder.Sql("""
                ALTER INDEX IF EXISTS public."IX_BusinessUnits_CompanyId" RENAME TO "IX_BusinessUnits_ProfileId";
                ALTER INDEX IF EXISTS public."IX_Subscriptions_CompanyId" RENAME TO "IX_Subscriptions_ProfileId";
                CREATE INDEX IF NOT EXISTS "IX_BusinessUnits_ProfileId" ON public."BusinessUnits" ("ProfileId");
                CREATE INDEX IF NOT EXISTS "IX_Subscriptions_ProfileId" ON public."Subscriptions" ("ProfileId");
                """);

            // 6. Drop the Contacts table and everything pointing at it.
            migrationBuilder.Sql("""
                ALTER TABLE public."BusinessUnits" DROP CONSTRAINT IF EXISTS "FK_BusinessUnits_Contacts_ContactId";
                DROP INDEX IF EXISTS public."IX_BusinessUnits_ContactId";
                ALTER TABLE public."BusinessUnits" DROP COLUMN IF EXISTS "ContactId";

                ALTER TABLE public."Profiles" DROP CONSTRAINT IF EXISTS "FK_Companies_Contacts_ContactId";
                DROP INDEX IF EXISTS public."IX_Profiles_ContactId";
                ALTER TABLE public."Profiles" DROP COLUMN IF EXISTS "ContactId";

                DROP TABLE IF EXISTS public."Contacts";
                """);

            // 7. Drop the columns the Profile concept does not carry.
            //    IsCloveraPersonalApp moves to configuration key
            //    CloveraPersonalApp:PersonalProfileId - it identified a single row
            //    and nothing else read it. BusinessUnits keeps its own copy.
            migrationBuilder.Sql("""
                ALTER TABLE public."Profiles" DROP COLUMN IF EXISTS "CompanyCode";
                ALTER TABLE public."Profiles" DROP COLUMN IF EXISTS "CountryCode";
                ALTER TABLE public."Profiles" DROP COLUMN IF EXISTS "NameInvariant";
                ALTER TABLE public."Profiles" DROP COLUMN IF EXISTS "IsCloveraPersonalApp";
                """);

            // 8. Parent profile. Separate from PartnerId, which is the commercial
            //    relationship and is untouched. RESTRICT so removing a parent never
            //    silently takes its children with it.
            migrationBuilder.Sql("""
                ALTER TABLE public."Profiles" ADD COLUMN IF NOT EXISTS "ParentId" bigint;

                CREATE INDEX IF NOT EXISTS "IX_Profiles_ParentId" ON public."Profiles" ("ParentId");

                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_constraint
                                   WHERE conname = 'FK_Profiles_Profiles_ParentId'
                                     AND conrelid = 'public."Profiles"'::regclass) THEN
                        ALTER TABLE public."Profiles"
                            ADD CONSTRAINT "FK_Profiles_Profiles_ParentId"
                            FOREIGN KEY ("ParentId") REFERENCES public."Profiles"("Id") ON DELETE RESTRICT;
                    END IF;
                END $$;
                """);

            // 9. Rename the foreign keys that still carry the old table name. A
            //    table rename leaves them working but keeps their old names, which
            //    would then disagree with what EF expects. Only constraints that
            //    actually exist are renamed: several the model declares were never
            //    created in this database.
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    r record;
                BEGIN
                    FOR r IN
                        SELECT * FROM (VALUES
                            ('BusinessUnits',                 'FK_BusinessUnits_Companies_CompanyId',                'FK_BusinessUnits_Profiles_ProfileId'),
                            ('Subscriptions',                 'FK_Subscriptions_Companies_CompanyId',                'FK_Subscriptions_Profiles_ProfileId'),
                            ('Profiles',                      'FK_Companies_Companies_PartnerId',                    'FK_Profiles_Profiles_PartnerId'),
                            ('CompanyLicenses',               'FK_CompanyLicenses_Companies_CompanyId',              'FK_CompanyLicenses_Profiles_CompanyId'),
                            ('CompanySubscriptions',          'FK_CompanySubscriptions_Companies',                   'FK_CompanySubscriptions_Profiles_CompanyId'),
                            ('CustomQuestions',               'FK_CustomQuestions_Companies_CompanyId',              'FK_CustomQuestions_Profiles_CompanyId'),
                            ('Payments',                      'FK_Payments_Companies',                               'FK_Payments_Profiles_CompanyId'),
                            ('TeamGoals',                     'FK_TeamGoals_Companies_CompanyId',                    'FK_TeamGoals_Profiles_CompanyId'),
                            ('TeamSetups',                    'FK_TeamSetups_Companies_CompanyId',                   'FK_TeamSetups_Profiles_CompanyId'),
                            ('AzureMarketplaceSubscription',  'FK_AzureMarketplaceSubscription_Companies_CompanyId', 'FK_AzureMarketplaceSubscription_Profiles_CompanyId'),
                            ('PromoCodes',                    'FK_PromoCodes_Companies_CompanyId',                   'FK_PromoCodes_Profiles_CompanyId'),
                            ('PromoCodeUsages',               'FK_PromoCodeUsages_Companies_CompanyId',              'FK_PromoCodeUsages_Profiles_CompanyId'),
                            ('LLMReportSchedules',            'FK_LLMReportSchedules_Companies_CompanyId',           'FK_LLMReportSchedules_Profiles_CompanyId')
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rebuilds the old shape. Contacts is reconstructed from the inline
            // columns, so it comes back with one row per distinct email address
            // rather than the exact original row ids - the merge is not reversible
            // at that level of fidelity. Restore the pre-change backup if the
            // original Contacts ids matter.

            // 9. Foreign key names back.
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    r record;
                BEGIN
                    FOR r IN
                        SELECT * FROM (VALUES
                            ('BusinessUnits',                 'FK_BusinessUnits_Profiles_ProfileId',                'FK_BusinessUnits_Companies_CompanyId'),
                            ('Subscriptions',                 'FK_Subscriptions_Profiles_ProfileId',                'FK_Subscriptions_Companies_CompanyId'),
                            ('Profiles',                      'FK_Profiles_Profiles_PartnerId',                     'FK_Companies_Companies_PartnerId'),
                            ('CompanyLicenses',               'FK_CompanyLicenses_Profiles_CompanyId',              'FK_CompanyLicenses_Companies_CompanyId'),
                            ('CompanySubscriptions',          'FK_CompanySubscriptions_Profiles_CompanyId',         'FK_CompanySubscriptions_Companies'),
                            ('CustomQuestions',               'FK_CustomQuestions_Profiles_CompanyId',              'FK_CustomQuestions_Companies_CompanyId'),
                            ('Payments',                      'FK_Payments_Profiles_CompanyId',                     'FK_Payments_Companies'),
                            ('TeamGoals',                     'FK_TeamGoals_Profiles_CompanyId',                    'FK_TeamGoals_Companies_CompanyId'),
                            ('TeamSetups',                    'FK_TeamSetups_Profiles_CompanyId',                   'FK_TeamSetups_Companies_CompanyId'),
                            ('AzureMarketplaceSubscription',  'FK_AzureMarketplaceSubscription_Profiles_CompanyId', 'FK_AzureMarketplaceSubscription_Companies_CompanyId'),
                            ('PromoCodes',                    'FK_PromoCodes_Profiles_CompanyId',                   'FK_PromoCodes_Companies_CompanyId'),
                            ('PromoCodeUsages',               'FK_PromoCodeUsages_Profiles_CompanyId',              'FK_PromoCodeUsages_Companies_CompanyId'),
                            ('LLMReportSchedules',            'FK_LLMReportSchedules_Profiles_CompanyId',           'FK_LLMReportSchedules_Companies_CompanyId')
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

            // 8. Parent profile away.
            migrationBuilder.Sql("""
                ALTER TABLE public."Profiles" DROP CONSTRAINT IF EXISTS "FK_Profiles_Profiles_ParentId";
                DROP INDEX IF EXISTS public."IX_Profiles_ParentId";
                ALTER TABLE public."Profiles" DROP COLUMN IF EXISTS "ParentId";
                """);

            // 7. Bring the dropped columns back. The values are gone, so each gets
            //    the default the creation paths used to write.
            migrationBuilder.Sql("""
                ALTER TABLE public."Profiles" ADD COLUMN IF NOT EXISTS "CompanyCode"          character varying(36);
                ALTER TABLE public."Profiles" ADD COLUMN IF NOT EXISTS "CountryCode"          character varying(2);
                ALTER TABLE public."Profiles" ADD COLUMN IF NOT EXISTS "NameInvariant"        character varying(140);
                ALTER TABLE public."Profiles" ADD COLUMN IF NOT EXISTS "IsCloveraPersonalApp" boolean;

                UPDATE public."Profiles"
                   SET "CompanyCode"          = COALESCE("CompanyCode", gen_random_uuid()::text),
                       "CountryCode"          = COALESCE("CountryCode", 'US'),
                       "NameInvariant"        = COALESCE("NameInvariant", "ProfileName"),
                       "IsCloveraPersonalApp" = COALESCE("IsCloveraPersonalApp", false);

                ALTER TABLE public."Profiles" ALTER COLUMN "CompanyCode"          SET NOT NULL;
                ALTER TABLE public."Profiles" ALTER COLUMN "CountryCode"          SET NOT NULL;
                ALTER TABLE public."Profiles" ALTER COLUMN "NameInvariant"        SET NOT NULL;
                ALTER TABLE public."Profiles" ALTER COLUMN "IsCloveraPersonalApp" SET NOT NULL;
                ALTER TABLE public."Profiles" ALTER COLUMN "IsCloveraPersonalApp" SET DEFAULT false;
                """);

            // 6. Rebuild Contacts from the inline columns and re-point both tables
            //    at it.
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS public."Contacts" (
                    "Id"           integer GENERATED BY DEFAULT AS IDENTITY,
                    "EmailAddress" character varying(254) NOT NULL,
                    "FirstName"    character varying(60)  NOT NULL,
                    "LastName"     character varying(60)  NOT NULL,
                    CONSTRAINT "PK_Contacts" PRIMARY KEY ("Id")
                );

                INSERT INTO public."Contacts" ("EmailAddress", "FirstName", "LastName")
                SELECT DISTINCT ON (lower(email))
                       email, COALESCE(first_name, ''), COALESCE(last_name, '')
                  FROM (
                        SELECT "ContactEmailAddress" AS email, "ContactFirstName" AS first_name, "ContactLastName" AS last_name
                          FROM public."Profiles"      WHERE "ContactEmailAddress" IS NOT NULL
                        UNION ALL
                        SELECT "ContactEmailAddress", "ContactFirstName", "ContactLastName"
                          FROM public."BusinessUnits" WHERE "ContactEmailAddress" IS NOT NULL
                       ) s
                 WHERE NOT EXISTS (
                        SELECT 1 FROM public."Contacts" c WHERE lower(c."EmailAddress") = lower(s.email))
                 ORDER BY lower(email);
                """);

            migrationBuilder.Sql("""
                ALTER TABLE public."Profiles"      ADD COLUMN IF NOT EXISTS "ContactId" integer;
                ALTER TABLE public."BusinessUnits" ADD COLUMN IF NOT EXISTS "ContactId" integer;

                UPDATE public."Profiles" p
                   SET "ContactId" = c."Id"
                  FROM public."Contacts" c
                 WHERE lower(c."EmailAddress") = lower(p."ContactEmailAddress")
                   AND p."ContactId" IS NULL;

                UPDATE public."BusinessUnits" b
                   SET "ContactId" = c."Id"
                  FROM public."Contacts" c
                 WHERE lower(c."EmailAddress") = lower(b."ContactEmailAddress")
                   AND b."ContactId" IS NULL;
                """);

            // Rows whose contact was never captured get a placeholder, because the
            // original columns were NOT NULL.
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    placeholder_id integer;
                BEGIN
                    IF EXISTS (SELECT 1 FROM public."Profiles"      WHERE "ContactId" IS NULL)
                       OR EXISTS (SELECT 1 FROM public."BusinessUnits" WHERE "ContactId" IS NULL) THEN

                        SELECT "Id" INTO placeholder_id
                          FROM public."Contacts"
                         WHERE "EmailAddress" = 'unknown@invalid';

                        IF placeholder_id IS NULL THEN
                            INSERT INTO public."Contacts" ("EmailAddress", "FirstName", "LastName")
                            VALUES ('unknown@invalid', 'Unknown', 'Unknown')
                            RETURNING "Id" INTO placeholder_id;
                        END IF;

                        UPDATE public."Profiles"      SET "ContactId" = placeholder_id WHERE "ContactId" IS NULL;
                        UPDATE public."BusinessUnits" SET "ContactId" = placeholder_id WHERE "ContactId" IS NULL;
                    END IF;
                END $$;

                ALTER TABLE public."Profiles"      ALTER COLUMN "ContactId" SET NOT NULL;
                ALTER TABLE public."BusinessUnits" ALTER COLUMN "ContactId" SET NOT NULL;

                CREATE INDEX IF NOT EXISTS "IX_Profiles_ContactId"      ON public."Profiles"      ("ContactId");
                CREATE INDEX IF NOT EXISTS "IX_BusinessUnits_ContactId" ON public."BusinessUnits" ("ContactId");

                ALTER TABLE public."Profiles"
                    ADD CONSTRAINT "FK_Companies_Contacts_ContactId"
                    FOREIGN KEY ("ContactId") REFERENCES public."Contacts"("Id") ON DELETE CASCADE;

                ALTER TABLE public."BusinessUnits"
                    ADD CONSTRAINT "FK_BusinessUnits_Contacts_ContactId"
                    FOREIGN KEY ("ContactId") REFERENCES public."Contacts"("Id") ON DELETE CASCADE;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE public."Profiles"      DROP COLUMN IF EXISTS "ContactEmailAddress";
                ALTER TABLE public."Profiles"      DROP COLUMN IF EXISTS "ContactFirstName";
                ALTER TABLE public."Profiles"      DROP COLUMN IF EXISTS "ContactLastName";
                ALTER TABLE public."BusinessUnits" DROP COLUMN IF EXISTS "ContactEmailAddress";
                ALTER TABLE public."BusinessUnits" DROP COLUMN IF EXISTS "ContactFirstName";
                ALTER TABLE public."BusinessUnits" DROP COLUMN IF EXISTS "ContactLastName";
                """);

            // 5. ProfileId -> CompanyId.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_schema = 'public' AND table_name = 'BusinessUnits' AND column_name = 'ProfileId')
                       AND NOT EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_schema = 'public' AND table_name = 'BusinessUnits' AND column_name = 'CompanyId') THEN
                        ALTER TABLE public."BusinessUnits" RENAME COLUMN "ProfileId" TO "CompanyId";
                    END IF;

                    IF EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_schema = 'public' AND table_name = 'Subscriptions' AND column_name = 'ProfileId')
                       AND NOT EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_schema = 'public' AND table_name = 'Subscriptions' AND column_name = 'CompanyId') THEN
                        ALTER TABLE public."Subscriptions" RENAME COLUMN "ProfileId" TO "CompanyId";
                    END IF;
                END $$;

                ALTER INDEX IF EXISTS public."IX_BusinessUnits_ProfileId" RENAME TO "IX_BusinessUnits_CompanyId";
                ALTER INDEX IF EXISTS public."IX_Subscriptions_ProfileId" RENAME TO "IX_Subscriptions_CompanyId";
                """);

            // 2 and 1. Column names, index names, table name.
            migrationBuilder.Sql("""
                ALTER INDEX IF EXISTS public."IX_Profiles_ProfileName" RENAME TO "IX_Companies_Name";
                ALTER INDEX IF EXISTS public."IX_Profiles_PartnerId"   RENAME TO "IX_Companies_PartnerId";
                ALTER INDEX IF EXISTS public."IX_Profiles_ContactId"   RENAME TO "IX_Companies_ContactId";

                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_schema = 'public' AND table_name = 'Profiles' AND column_name = 'ProfileName') THEN
                        ALTER TABLE public."Profiles" RENAME COLUMN "ProfileName" TO "Name";
                    END IF;

                    IF EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_schema = 'public' AND table_name = 'Profiles' AND column_name = 'ProfileShortCode') THEN
                        ALTER TABLE public."Profiles" RENAME COLUMN "ProfileShortCode" TO "CompanyShortCode";
                    END IF;

                    IF EXISTS (SELECT 1 FROM pg_constraint
                               WHERE conname = 'PK_Profiles'
                                 AND conrelid = 'public."Profiles"'::regclass) THEN
                        ALTER TABLE public."Profiles" RENAME CONSTRAINT "PK_Profiles" TO "PK_Companies";
                    END IF;

                    IF to_regclass('public."Profiles"') IS NOT NULL
                       AND to_regclass('public."Companies"') IS NULL THEN
                        ALTER TABLE public."Profiles" RENAME TO "Companies";
                    END IF;
                END $$;
                """);
        }
    }
}
