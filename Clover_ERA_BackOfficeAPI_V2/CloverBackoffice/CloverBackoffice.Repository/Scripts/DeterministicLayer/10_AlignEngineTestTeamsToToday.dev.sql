-- =============================================================================
-- 10_AlignEngineTestTeamsToToday.dev.sql          (DEV TEST DATA ONLY)
--
-- Moves the engine test teams' data forward by whole weeks, so each team's last
-- check-in lands on its last working day up to today — the same as the workbook
-- import's alignToToday. Nothing is invented: every answer keeps its member, its
-- question, its level and its chips, and keeps its place in the team's own week.
--
-- Why moving, not adding: each day's question comes from the engine's rotation for
-- that asked day, so a made-up check-in would carry the wrong question and break the
-- team's replay. Every engine day is counted from EngineTeamSettings.StartDate, so
-- moving the start date and the stored calendar dates by the same whole weeks moves
-- the whole history with it; cycles, states and Eran's messages, which are stored by
-- day number, need no change.
--
-- Teams: 307–314 (edit the list below). For each, shift = whole weeks between its
-- last answer and today. A team already up to date gets 0 and is left alone, so the
-- script is safe to run again.
--
-- Moved, by the same number of days:
--   EngineTeamSettings.StartDate          EngineAnswers.AnswerDate
--   EngineTeamMemberships.EffectiveFrom/To EngineConfirmations.ConfirmedDate
--   EngineDeclines.DeclinedDate           EngineAcknowledgements.AcknowledgedAt
--   TeamWeeklyFlags week dates            TeamActionStates.DueDate
--   the workbook's Team Health rows (DailyFeedback / TeamDailyFeedback, marker 42.424242s)
-- Not moved: custom questions and milestones (they are dated events of their own).
--
-- Also cleared: the engine's own states and Eran messages for days after yesterday,
-- which earlier runs produced past the last answer. They are derived from the answers.
--
-- AFTERWARDS run the engine once, so cycles, states, messages and weekly flags are
-- worked out on the new dates: POST api/rrida/engine/run-all (RRIDA Admin, e.g. from
-- Swagger), or wait for the daily run. Eran writes the new messages in the background.
--
-- One transaction. Run the whole file in pgAdmin (F5). It prints what it moved.
-- =============================================================================

BEGIN;

CREATE TEMP TABLE align_shift ON COMMIT DROP AS
SELECT s."BusinessUnitId" AS unit,
       last_answer,
       (floor((current_date - last_answer) / 7.0) * 7)::int AS shift_days
FROM "EngineTeamSettings" s
JOIN (SELECT "BusinessUnitId", max("AnswerDate") AS last_answer
      FROM "EngineAnswers" GROUP BY 1) a USING ("BusinessUnitId")
WHERE s."BusinessUnitId" IN (307, 308, 309, 310, 311, 312, 313, 314);

DELETE FROM align_shift WHERE shift_days <= 0;

-- Team clock and memberships.
UPDATE "EngineTeamSettings" t SET "StartDate" = t."StartDate" + a.shift_days, "UpdatedAt" = now()
FROM align_shift a WHERE t."BusinessUnitId" = a.unit;

UPDATE "EngineTeamMemberships" t
SET "EffectiveFrom" = t."EffectiveFrom" + a.shift_days,
    "EffectiveTo"   = t."EffectiveTo" + a.shift_days
FROM align_shift a WHERE t."BusinessUnitId" = a.unit;

-- Answers: unique per (team, member, track, date), so moved through a far date first;
-- a single move could land one row on another that has not moved yet.
UPDATE "EngineAnswers" t SET "AnswerDate" = t."AnswerDate" + 36500
FROM align_shift a WHERE t."BusinessUnitId" = a.unit;
UPDATE "EngineAnswers" t SET "AnswerDate" = t."AnswerDate" - 36500 + a.shift_days
FROM align_shift a WHERE t."BusinessUnitId" = a.unit;

-- The manager's taps.
UPDATE "EngineConfirmations" t SET "ConfirmedDate" = t."ConfirmedDate" + a.shift_days
FROM align_shift a WHERE t."BusinessUnitId" = a.unit;
UPDATE "EngineDeclines" t SET "DeclinedDate" = t."DeclinedDate" + a.shift_days
FROM align_shift a WHERE t."BusinessUnitId" = a.unit;
UPDATE "EngineAcknowledgements" t SET "AcknowledgedAt" = t."AcknowledgedAt" + make_interval(days => a.shift_days)
FROM align_shift a WHERE t."BusinessUnitId" = a.unit;

-- Weekly flags (one live row per team per week): through a far date as well.
UPDATE "TeamWeeklyFlags" t SET "WeekStartDate" = t."WeekStartDate" + 36400, "WeekEndDate" = t."WeekEndDate" + 36400
FROM align_shift a WHERE t."UnitId" = a.unit;
UPDATE "TeamWeeklyFlags" t
SET "WeekStartDate" = t."WeekStartDate" - 36400 + a.shift_days,
    "WeekEndDate"   = t."WeekEndDate" - 36400 + a.shift_days,
    "UpdatedAt"     = now()
FROM align_shift a WHERE t."UnitId" = a.unit;

-- Manager due dates on actions.
UPDATE "TeamActionStates" t SET "DueDate" = t."DueDate" + a.shift_days
FROM align_shift a WHERE t."UnitId" = a.unit AND t."DueDate" IS NOT NULL;

-- The workbook's copies in Team Health's own tables (only rows the import wrote).
UPDATE "DailyFeedback" t SET "SubmissionDateTime" = t."SubmissionDateTime" + make_interval(days => a.shift_days)
FROM align_shift a
WHERE t."BusinessUnitId" = a.unit AND extract(microseconds FROM t."SubmissionDateTime") = 42424242;
UPDATE "TeamDailyFeedback" t SET "SubmissionDateTime" = t."SubmissionDateTime" + make_interval(days => a.shift_days)
FROM align_shift a
WHERE t."BusinessUnitId" = a.unit AND extract(microseconds FROM t."SubmissionDateTime") = 42424242;

-- The engine's own results past yesterday: earlier runs went past the last answer, and
-- with the start date moved those days now fall on today or later. They are derived, so
-- they are cleared and the next engine run works them out again from the answers.
-- Day of yesterday = current_date - StartDate (day 1 is the start date).
DELETE FROM "EngineStateDaily" t USING align_shift a, "EngineTeamSettings" s
WHERE t."BusinessUnitId" = a.unit AND s."BusinessUnitId" = a.unit AND t."Day" > current_date - s."StartDate";
DELETE FROM "EngineFeedItems" t USING align_shift a, "EngineTeamSettings" s
WHERE t."BusinessUnitId" = a.unit AND s."BusinessUnitId" = a.unit AND t."Day" > current_date - s."StartDate";

-- What moved.
SELECT a.unit AS team, bu."Name", a.last_answer AS last_answer_before, a.shift_days,
       a.last_answer + a.shift_days AS last_answer_now, s."StartDate" AS new_start_date
FROM align_shift a
JOIN "BusinessUnits" bu ON bu."Id" = a.unit
JOIN "EngineTeamSettings" s ON s."BusinessUnitId" = a.unit
ORDER BY 1;

COMMIT;
