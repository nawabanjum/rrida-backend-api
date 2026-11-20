-- =============================================================================
-- 01_AllowThreeTeamsPerLicense.idempotent.sql
--
-- A manager licence now covers up to 3 teams: the purchased team plus up to 2
-- complimentary (same-manager) teams. Replaces the UNIQUE "one active grant per
-- licence" index on ComplimentaryLicenseGrants with a non-unique one. The
-- limit itself is enforced by the API.
--
-- Existing data is not changed. Safe to run more than once (F5 in pgAdmin).
-- Mirrors EF migration 20260917090000_AllowThreeTeamsPerLicense.
-- =============================================================================

BEGIN;

-- Drop any UNIQUE index on ComplimentaryLicenseGrants(CompanyLicenseId, IsActive),
-- whatever it is named.
DO $$
DECLARE
    idx record;
BEGIN
    FOR idx IN
        SELECT i.relname AS index_name
        FROM pg_index x
        JOIN pg_class i ON i.oid = x.indexrelid
        JOIN pg_class t ON t.oid = x.indrelid
        JOIN pg_namespace n ON n.oid = t.relnamespace
        WHERE n.nspname = 'public'
          AND t.relname = 'ComplimentaryLicenseGrants'
          AND x.indisunique
          AND NOT x.indisprimary
          AND pg_get_indexdef(x.indexrelid) LIKE '%("CompanyLicenseId", "IsActive")%'
    LOOP
        EXECUTE format('DROP INDEX public.%I', idx.index_name);
        RAISE NOTICE 'Dropped unique index %', idx.index_name;
    END LOOP;
END $$;

CREATE INDEX IF NOT EXISTS "IX_ComplimentaryLicenseGrants_CompanyLicenseId_IsActive"
    ON public."ComplimentaryLicenseGrants" ("CompanyLicenseId", "IsActive")
    WHERE "IsActive" = true;

INSERT INTO public."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
SELECT '20260917090000_AllowThreeTeamsPerLicense', '9.0.1'
WHERE NOT EXISTS (
    SELECT 1 FROM public."__EFMigrationsHistory"
    WHERE "MigrationId" = '20260917090000_AllowThreeTeamsPerLicense');

COMMIT;

-- Summary
SELECT i.relname AS index_name, x.indisunique AS is_unique, pg_get_indexdef(x.indexrelid) AS definition
FROM pg_index x
JOIN pg_class i ON i.oid = x.indexrelid
JOIN pg_class t ON t.oid = x.indrelid
WHERE t.relname = 'ComplimentaryLicenseGrants'
ORDER BY i.relname;
