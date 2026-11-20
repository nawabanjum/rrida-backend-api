-- ===========================================================================
-- 01_RenameCompaniesToProfiles.idempotent.sql
--
-- Redesigns public."Companies" into public."Profiles":
--
--   * renames the table and the columns that carried company-specific names
--   * merges the one-to-one public."Contacts" row into inline columns, on
--     BOTH "Profiles" and "BusinessUnits" (both had a ContactId), then drops
--     the "Contacts" table
--   * drops the columns that the Profile concept does not need
--   * adds the self-referencing "ParentId"
--   * repoints and renames the affected indexes and foreign keys
--
-- Safe to run more than once: every statement checks the object's current
-- state first, so a partially-applied run can simply be re-run.
--
-- This is the same set of changes as EF migration
-- 20260907091110_RenameCompaniesToProfiles, which executes this script's
-- statements verbatim. Run EITHER the migration OR this script, not both -
-- though running the second one after the first is a no-op by design.
--
-- Take a backup first. This script drops columns and a table; the data in
-- "Contacts" survives only as the copies made into "Profiles" and
-- "BusinessUnits" here.
-- ===========================================================================

BEGIN;

-- ---------------------------------------------------------------------------
-- 0. Guard: refuse to run against a database that is not this schema.
-- ---------------------------------------------------------------------------
DO $$
BEGIN
    IF to_regclass('public."Companies"') IS NULL
       AND to_regclass('public."Profiles"') IS NULL THEN
        RAISE EXCEPTION
            'Neither public."Companies" nor public."Profiles" exists. This is not the Clover back-office schema.';
    END IF;
END $$;

-- ---------------------------------------------------------------------------
-- 1. Companies -> Profiles, and its primary key.
-- ---------------------------------------------------------------------------
DO $$
BEGIN
    IF to_regclass('public."Companies"') IS NOT NULL
       AND to_regclass('public."Profiles"') IS NULL THEN
        ALTER TABLE public."Companies" RENAME TO "Profiles";
    END IF;
END $$;

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_constraint
               WHERE conname = 'PK_Companies'
                 AND conrelid = 'public."Profiles"'::regclass) THEN
        ALTER TABLE public."Profiles" RENAME CONSTRAINT "PK_Companies" TO "PK_Profiles";
    END IF;
END $$;

-- ---------------------------------------------------------------------------
-- 2. Column renames on Profiles.
--      Name             -> ProfileName
--      CompanyShortCode -> ProfileShortCode
-- ---------------------------------------------------------------------------
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

-- Indexes carried over from the old table name. IX_Companies_NameInvariant is
-- not renamed: its column is dropped in step 7, which drops the index with it.
ALTER INDEX IF EXISTS public."IX_Companies_Name"      RENAME TO "IX_Profiles_ProfileName";
ALTER INDEX IF EXISTS public."IX_Companies_PartnerId" RENAME TO "IX_Profiles_PartnerId";
ALTER INDEX IF EXISTS public."IX_Companies_ContactId" RENAME TO "IX_Profiles_ContactId";

-- ---------------------------------------------------------------------------
-- 3. Merge the Contacts row into Profiles.
-- ---------------------------------------------------------------------------
ALTER TABLE public."Profiles" ADD COLUMN IF NOT EXISTS "ContactEmailAddress" character varying(254);
ALTER TABLE public."Profiles" ADD COLUMN IF NOT EXISTS "ContactFirstName"    character varying(60);
ALTER TABLE public."Profiles" ADD COLUMN IF NOT EXISTS "ContactLastName"     character varying(60);

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

-- ---------------------------------------------------------------------------
-- 4. Merge the Contacts row into BusinessUnits.
--
--    BusinessUnits.ContactId pointed at the same table, and rows were shared:
--    one contact could serve dozens of units, so editing a unit's contact
--    edited every unit sharing it. Copying the values in gives each unit its
--    own, which is why Contacts can be dropped rather than kept for units.
-- ---------------------------------------------------------------------------
ALTER TABLE public."BusinessUnits" ADD COLUMN IF NOT EXISTS "ContactEmailAddress" character varying(254);
ALTER TABLE public."BusinessUnits" ADD COLUMN IF NOT EXISTS "ContactFirstName"    character varying(60);
ALTER TABLE public."BusinessUnits" ADD COLUMN IF NOT EXISTS "ContactLastName"     character varying(60);

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

-- ---------------------------------------------------------------------------
-- 5. BusinessUnits.CompanyId -> ProfileId, and Subscriptions.CompanyId too.
--
--    Only these two are renamed. Every other table keeps its "CompanyId"
--    column: those names reach the API payloads that the admin front end and
--    the mobile app read, and renaming them is a separate, breaking change.
-- ---------------------------------------------------------------------------
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

ALTER INDEX IF EXISTS public."IX_BusinessUnits_CompanyId" RENAME TO "IX_BusinessUnits_ProfileId";
ALTER INDEX IF EXISTS public."IX_Subscriptions_CompanyId" RENAME TO "IX_Subscriptions_ProfileId";

-- The live database has no index behind FK_BusinessUnits_Companies_CompanyId
-- even though the EF model declares one, so create it rather than assume the
-- rename above found something.
CREATE INDEX IF NOT EXISTS "IX_BusinessUnits_ProfileId" ON public."BusinessUnits" ("ProfileId");
CREATE INDEX IF NOT EXISTS "IX_Subscriptions_ProfileId" ON public."Subscriptions" ("ProfileId");

-- ---------------------------------------------------------------------------
-- 6. Drop the Contacts table and everything pointing at it.
-- ---------------------------------------------------------------------------
ALTER TABLE public."BusinessUnits" DROP CONSTRAINT IF EXISTS "FK_BusinessUnits_Contacts_ContactId";
DROP INDEX IF EXISTS public."IX_BusinessUnits_ContactId";
ALTER TABLE public."BusinessUnits" DROP COLUMN IF EXISTS "ContactId";

ALTER TABLE public."Profiles" DROP CONSTRAINT IF EXISTS "FK_Companies_Contacts_ContactId";
DROP INDEX IF EXISTS public."IX_Profiles_ContactId";
ALTER TABLE public."Profiles" DROP COLUMN IF EXISTS "ContactId";

DROP TABLE IF EXISTS public."Contacts";

-- ---------------------------------------------------------------------------
-- 7. Drop the columns the Profile concept does not carry.
--
--    IsCloveraPersonalApp moves to configuration key
--    CloveraPersonalApp:PersonalProfileId - the flag identified a single row,
--    and nothing else read it. BusinessUnits keeps its own copy of the flag.
-- ---------------------------------------------------------------------------
ALTER TABLE public."Profiles" DROP COLUMN IF EXISTS "CompanyCode";
ALTER TABLE public."Profiles" DROP COLUMN IF EXISTS "CountryCode";
ALTER TABLE public."Profiles" DROP COLUMN IF EXISTS "NameInvariant";
ALTER TABLE public."Profiles" DROP COLUMN IF EXISTS "IsCloveraPersonalApp";

-- ---------------------------------------------------------------------------
-- 8. Parent profile. Separate from PartnerId, which is the commercial
--    partner relationship and stays as it is.
--
--    ON DELETE RESTRICT: removing a parent must not silently take its
--    children, and everything hanging off them, with it.
-- ---------------------------------------------------------------------------
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

-- ---------------------------------------------------------------------------
-- 9. Rename the foreign keys that still carry the old table name.
--
--    A table rename in PostgreSQL leaves foreign keys working but keeps their
--    old names, which would then disagree with what EF Core expects. Only
--    constraints that actually exist are renamed - several that the EF model
--    declares were never created in this database.
-- ---------------------------------------------------------------------------
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

COMMIT;

-- ---------------------------------------------------------------------------
-- Verification. Run after the transaction commits.
-- ---------------------------------------------------------------------------
-- SELECT column_name, data_type, is_nullable
--   FROM information_schema.columns
--  WHERE table_schema = 'public' AND table_name = 'Profiles'
--  ORDER BY ordinal_position;
--
-- SELECT count(*) FILTER (WHERE "ContactEmailAddress" IS NOT NULL) AS with_contact,
--        count(*)                                                  AS total
--   FROM public."Profiles";
--
-- SELECT conname, pg_get_constraintdef(oid)
--   FROM pg_constraint
--  WHERE conrelid = 'public."Profiles"'::regclass;
