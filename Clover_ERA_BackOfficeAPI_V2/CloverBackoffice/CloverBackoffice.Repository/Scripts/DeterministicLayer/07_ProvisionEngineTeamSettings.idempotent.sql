-- =============================================================================
-- 07_ProvisionEngineTeamSettings.idempotent.sql
--
-- Turns the engine on by default: every live team gets engine settings.
--
--   StartDate    the team's earliest engine answer, or today if it has none.
--                A team that has been answering already has a clock, and starting
--                it from today would renumber the history it already has.
--   WorkingWeek  'mon-fri'
--   IsEnabled    true
--
-- INSERTS ONLY. A team that already has a settings row is left exactly as it is,
-- so anything an admin has set — a different working week, or the engine switched
-- off for that team — survives this script and every later run.
--
-- The application does the same thing by itself: the daily job provisions any team
-- without settings, and enrolling a member provisions that team. This script is
-- only here so existing teams are ready before the first run rather than after it.
--
-- No schema change. Safe to run more than once (F5 in pgAdmin).
-- RUN 03_AddEngineIdentityAndAnswers.idempotent.sql FIRST.
-- =============================================================================

BEGIN;

DO $$
BEGIN
    IF to_regclass('public."EngineTeamSettings"') IS NULL THEN
        RAISE EXCEPTION 'EngineTeamSettings does not exist. Run 03_AddEngineIdentityAndAnswers.idempotent.sql first.';
    END IF;
END $$;

INSERT INTO public."EngineTeamSettings"
    ("BusinessUnitId", "StartDate", "WorkingWeek", "IsEnabled", "CreatedAt")
SELECT b."Id",
       COALESCE(
           (SELECT min(a."AnswerDate") FROM public."EngineAnswers" a WHERE a."BusinessUnitId" = b."Id"),
           (now() AT TIME ZONE 'utc')::date),
       'mon-fri',
       true,
       now() AT TIME ZONE 'utc'
FROM public."BusinessUnits" b
WHERE b."IsDel" IS DISTINCT FROM true
  AND NOT EXISTS (SELECT 1 FROM public."EngineTeamSettings" s WHERE s."BusinessUnitId" = b."Id");

COMMIT;

-- Summary: how many teams are set up, and how many are on.
SELECT (SELECT count(*) FROM public."BusinessUnits" WHERE "IsDel" IS DISTINCT FROM true) AS live_teams,
       (SELECT count(*) FROM public."EngineTeamSettings")                                AS with_settings,
       (SELECT count(*) FROM public."EngineTeamSettings" WHERE "IsEnabled")              AS enabled,
       (SELECT count(*) FROM public."EngineTeamSettings" WHERE NOT "IsEnabled")          AS switched_off;
