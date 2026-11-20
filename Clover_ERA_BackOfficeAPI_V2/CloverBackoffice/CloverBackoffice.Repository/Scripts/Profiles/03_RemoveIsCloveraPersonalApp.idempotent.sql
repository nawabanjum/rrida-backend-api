-- ===========================================================================
-- 03_RemoveIsCloveraPersonalApp.idempotent.sql
--
-- Drops public."BusinessUnits"."IsCloveraPersonalApp".
--
-- The Clovera Personal App sign-up route has been removed. The mobile
-- registration endpoint no longer accepts a "personal" flag, nothing reads this
-- column, and the filters that excluded flagged units from the unit lists and
-- the scheduled-report job are gone with it.
--
-- No data is lost: zero rows had the flag set when this was written. Confirm for
-- yourself before running:
--
--     SELECT count(*) FROM public."BusinessUnits" WHERE "IsCloveraPersonalApp";
--
-- NOT touched, and not to be confused with this:
--
--   * config key CloveraPersonalApp:ApiToken - despite the name, this is the
--     ONLY credential the whole mobile API has. It gates every api/v1/* route,
--     the two app-facing team-daily-question routes and the snapshot endpoints.
--     Deleting it takes the mobile app offline.
--   * public."CloveraPersonalSnapshots" - the Snapshot Report feature and its
--     rows are left alone. It no longer references the dropped flag.
--
-- Safe to run more than once.
--
-- Identical in effect to EF migration
-- 20260907155849_RemoveIsCloveraPersonalApp. Run either one.
-- ===========================================================================

BEGIN;

DO $$
BEGIN
    IF to_regclass('public."BusinessUnits"') IS NULL THEN
        RAISE EXCEPTION 'public."BusinessUnits" does not exist. This is not the Clover back-office schema.';
    END IF;

    -- Refuse rather than silently discard, in case an environment still uses it.
    --
    -- Nested rather than one AND: PostgreSQL does not promise to short-circuit
    -- AND, so a single expression would plan the row check even on a re-run where
    -- the column is already gone, and fail with "column does not exist".
    IF EXISTS (SELECT 1 FROM information_schema.columns
               WHERE table_schema = 'public'
                 AND table_name = 'BusinessUnits'
                 AND column_name = 'IsCloveraPersonalApp')
    THEN
        IF EXISTS (SELECT 1 FROM public."BusinessUnits" WHERE "IsCloveraPersonalApp") THEN
            RAISE EXCEPTION
                'Some BusinessUnits still have IsCloveraPersonalApp = true. Review them before dropping the column.';
        END IF;
    END IF;
END $$;

ALTER TABLE public."BusinessUnits" DROP COLUMN IF EXISTS "IsCloveraPersonalApp";

COMMIT;

-- ---------------------------------------------------------------------------
-- Verification. Should return no rows.
-- ---------------------------------------------------------------------------
-- SELECT column_name FROM information_schema.columns
--  WHERE table_schema = 'public' AND column_name = 'IsCloveraPersonalApp';
