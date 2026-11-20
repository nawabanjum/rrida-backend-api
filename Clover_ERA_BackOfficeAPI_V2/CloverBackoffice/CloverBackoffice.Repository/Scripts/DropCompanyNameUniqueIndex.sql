-- Companies.Name / Companies.NameInvariant — drop the uniqueness requirement.
--
-- Duplicate company names are allowed from now on: companies are resolved by Id
-- (and by DomainName during registration), never by name. The application-side
-- guards were removed from /api/company/Create, /api/company/CreateTrial,
-- /api/company/check-name-exist, /api/cloveracompanyadmin/create-company-admin
-- and the partner-customer create flow.
--
-- Run as the table owner (cloverera_admin on prod) — see CONNECT-PROD.md for the
-- tunnel. Order: run STEP 0, then the matching drop from STEP 1, then STEP 2.

-- ---------------------------------------------------------------------------
-- STEP 0 — inspect what actually exists before dropping anything.
-- ---------------------------------------------------------------------------
-- Unique INDEXES on Companies (indisunique = true, conname NULL):
SELECT  i.relname                AS index_name,
        ix.indisunique           AS is_unique,
        c.conname                AS backing_constraint,   -- NULL => plain index
        pg_get_indexdef(ix.indexrelid) AS definition
FROM    pg_index ix
JOIN    pg_class     i  ON i.oid = ix.indexrelid
JOIN    pg_class     t  ON t.oid = ix.indrelid
JOIN    pg_namespace n  ON n.oid = t.relnamespace
LEFT JOIN pg_constraint c ON c.conindid = ix.indexrelid
WHERE   t.relname = 'Companies'
  AND   n.nspname = 'public'
ORDER BY i.relname;

-- ---------------------------------------------------------------------------
-- STEP 1 — drop the uniqueness.
--
-- Which variant to run depends on STEP 0:
--   backing_constraint IS NULL  -> it is a plain unique index  -> use 1a
--   backing_constraint IS NOT NULL (UNIQUE / PRIMARY KEY)      -> use 1b
--
-- If is_unique came back false for both indexes, nothing needs dropping —
-- the names were never enforced as unique and you can stop here.
-- ---------------------------------------------------------------------------

-- 1a. Plain unique indexes.
--
--     NOTE: CONCURRENTLY is deliberately NOT used. pgAdmin (and most GUI
--     clients) wrap each execution in a transaction block, and
--     DROP/CREATE INDEX CONCURRENTLY is rejected there with:
--         ERROR: DROP INDEX CONCURRENTLY cannot run inside a transaction block
--         SQL state: 25001
--     Companies is a small table (~200 rows), so the plain form's ACCESS
--     EXCLUSIVE lock is held for milliseconds. If you ever need the online
--     variant, run it from psql with autocommit on, one statement per call.
DROP INDEX IF EXISTS public."IX_Companies_Name";
DROP INDEX IF EXISTS public."IX_Companies_NameInvariant";

-- 1b. Constraint-backed uniqueness. Takes a brief ACCESS EXCLUSIVE lock on
--     Companies; the index is dropped with the constraint.
--     Substitute the real conname values reported by STEP 0.
-- ALTER TABLE public."Companies" DROP CONSTRAINT IF EXISTS "IX_Companies_Name";
-- ALTER TABLE public."Companies" DROP CONSTRAINT IF EXISTS "IX_Companies_NameInvariant";

-- ---------------------------------------------------------------------------
-- STEP 2 — recreate as NON-unique indexes.
--
-- Optional but recommended: the partner/opportunity screens filter companies by
-- name, and the old unique indexes were also serving those lookups. Recreating
-- them non-unique keeps the read path indexed without enforcing uniqueness.
-- ---------------------------------------------------------------------------
CREATE INDEX IF NOT EXISTS "IX_Companies_Name"
    ON public."Companies" ("Name");

CREATE INDEX IF NOT EXISTS "IX_Companies_NameInvariant"
    ON public."Companies" ("NameInvariant");

-- ---------------------------------------------------------------------------
-- STEP 3 — verify: is_unique must now be false for both.
-- ---------------------------------------------------------------------------
SELECT  i.relname AS index_name, ix.indisunique AS is_unique
FROM    pg_index ix
JOIN    pg_class i ON i.oid = ix.indexrelid
JOIN    pg_class t ON t.oid = ix.indrelid
WHERE   t.relname = 'Companies'
  AND   i.relname IN ('IX_Companies_Name', 'IX_Companies_NameInvariant');

-- Sanity check — duplicate names that already exist (should be none today,
-- and is expected to grow once the guards are gone):
SELECT  lower("Name") AS name, count(*) AS copies
FROM    public."Companies"
WHERE   NOT "IsDel"
GROUP BY 1
HAVING  count(*) > 1
ORDER BY copies DESC;
