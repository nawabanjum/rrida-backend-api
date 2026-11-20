-- =============================================================================
-- 02_AddKitVersionColumns.idempotent.sql
--
-- Deterministic layer, module 1 step 1.2.
--
-- The reference data now comes from the vendored RRIDA kit (external/RRIDA-Engine/
-- kit/data) rather than from the loose handover pack, so a loaded version records
-- which kit it was: KitVersion, DataVersion and the upstream BaselineTag.
--
-- RefChips gains Ordinal, the chip's position within its polarity set (1-6). The
-- authored order is load-bearing: a member sees N1 to N6 as written.
--
-- Adds columns only. No existing column is altered and no row is changed.
-- Safe to run more than once (F5 in pgAdmin).
-- RUN 01_AddReferenceTables.idempotent.sql FIRST — this script needs those tables.
-- Mirrors EF migration 20260923090000_AddKitVersionToRefDataVersions.
-- =============================================================================

BEGIN;

-- Stop with a clear message rather than a missing-table error if 01 was skipped.
DO $$
BEGIN
    IF to_regclass('public."RefDataVersions"') IS NULL THEN
        RAISE EXCEPTION 'RefDataVersions does not exist. Run 01_AddReferenceTables.idempotent.sql first.';
    END IF;
END $$;

ALTER TABLE public."RefDataVersions"
    ADD COLUMN IF NOT EXISTS "KitVersion" character varying(20),
    ADD COLUMN IF NOT EXISTS "DataVersion" character varying(50),
    ADD COLUMN IF NOT EXISTS "BaselineTag" character varying(50);

ALTER TABLE public."RefChips"
    ADD COLUMN IF NOT EXISTS "Ordinal" integer NOT NULL DEFAULT 0;

-- The EF migration 20260930153617_SyncEngineModelSnapshot carries this same SQL and
-- records itself when the pipeline runs. Nothing is written to __EFMigrationsHistory
-- here: every statement above is idempotent, so whichever runs second does nothing.

COMMIT;

-- Summary: the four columns this script adds, and whether they are now present.
SELECT c.table_name AS "Table", c.column_name AS "Column", c.data_type AS "Type"
FROM information_schema.columns c
WHERE c.table_schema = 'public'
  AND ((c.table_name = 'RefDataVersions' AND c.column_name IN ('KitVersion', 'DataVersion', 'BaselineTag'))
    OR (c.table_name = 'RefChips' AND c.column_name = 'Ordinal'))
ORDER BY c.table_name, c.column_name;
