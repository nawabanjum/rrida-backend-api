-- ════════════════════════════════════════════════════════════════════════════
-- Team Health — custom question demo data (DEV ONLY).
--
-- Teams 307–314 (profile 52, the imported engine workbook teams). Each team gets
-- three custom questions that were "asked" on working days inside the window the
-- teams already have check-in data for (2026-09-17 .. 2026-10-02), so the custom
-- question dots line up with the Team Health timeline chart:
--
--   even team ids  Tue 22 Sep, Fri 25 Sep, Wed 30 Sep
--   odd team ids   Wed 23 Sep, Mon 28 Sep, Thu 01 Oct
--
-- Each question is stored the way POST api/team-health/custom-questions stores it
-- (ScheduledAt = day 16:00 UTC), but as Expired because its day is over. Answers
-- go into CustomQuestionDailyFeedback a few minutes after 16:00; the number of
-- answers never exceeds the team's TeamSize, so possible / responseRate are real.
-- Every row gets its INSERT audit, as the service writes.
--
-- Visible in:
--   GET api/team-health/timeline?teamId=307          timeline.customQuestions[]
--   GET api/team-health/custom-questions?teamId=307  previouslyAsked[]
-- The timeline shows the last 30 days, so these appear there until about
-- 2026-10-21 (the 22 Sep questions drop off first).
--
-- Idempotent: a team/day that already has a live custom question is skipped.
-- Single run, one transaction. Cleanup at the bottom (commented out).
-- ════════════════════════════════════════════════════════════════════════════

BEGIN;

DO $$
DECLARE
    seed_tag CONSTANT text := 'Team Health demo seed';

    questions text[] := ARRAY[
        'How useful was this week''s team meeting?',
        'What is one thing slowing you down right now?',
        'How confident are you about hitting this month''s goals?'
    ];

    -- 11 answers per question (largest team is 11).
    answers text[][] := ARRAY[
        ARRAY[
            'Useful. We finally agreed who owns the handover.',
            'Good, but it ran 20 minutes over.',
            'Helpful to hear what the other half of the team is working on.',
            'Too much status update, not enough discussion.',
            'Clear actions at the end, which is new and welcome.',
            'Fine. Could have been an email for the first half.',
            'Really useful, the demo made the priorities obvious.',
            'I left knowing what to do on Monday.',
            'Would be better with an agenda sent the day before.',
            'Good energy this week.',
            'Useful, though a few people stayed quiet.'
        ],
        ARRAY[
            'Waiting on approvals from another team.',
            'Too many tools to update for the same piece of work.',
            'Unclear priorities when two requests land at once.',
            'Slow test environment.',
            'Meetings in the middle of the afternoon break my focus.',
            'Not knowing who to ask about the new process.',
            'Handover notes are often missing details.',
            'Access requests take days.',
            'Context switching between projects.',
            'Nothing major this week.',
            'Rework because requirements changed late.'
        ],
        ARRAY[
            'Fairly confident if nothing new lands on us.',
            'Confident. We are ahead on the main deliverable.',
            'Not very. We lost a week to the outage.',
            'About 70% sure.',
            'Confident on the team goal, less on my own.',
            'Worried about the dependency on the platform team.',
            'Confident now the scope was cut back.',
            'It depends on getting the extra person we asked for.',
            'Quite confident, the plan is realistic.',
            'Unsure, the targets moved twice this month.',
            'Confident.'
        ]
    ];

    -- Share of the team that answered, per question (before the per-team nudge).
    shares numeric[] := ARRAY[0.80, 0.65, 1.00];

    even_days date[] := ARRAY[DATE '2026-09-22', DATE '2026-09-25', DATE '2026-09-30'];
    odd_days  date[] := ARRAY[DATE '2026-09-23', DATE '2026-09-28', DATE '2026-10-01'];

    t record;
    qi int;
    ai int;
    ask_day date;
    asked_at timestamptz;
    answer_count int;
    q_id int;
    f_id int;
    f_at timestamptz;
    q_added int := 0;
    f_added int := 0;
BEGIN
    FOR t IN
        SELECT "Id", "ProfileId", "TeamSize"
        FROM "BusinessUnits"
        WHERE "Id" BETWEEN 307 AND 314 AND "IsDel" IS NOT TRUE
        ORDER BY "Id"
    LOOP
        FOR qi IN 1..3 LOOP
            ask_day := CASE WHEN t."Id" % 2 = 0 THEN even_days[qi] ELSE odd_days[qi] END;
            asked_at := (ask_day::timestamp + time '16:00') AT TIME ZONE 'UTC';

            -- Skip a team/day that already has a live question (re-run safe).
            IF EXISTS (
                SELECT 1 FROM "CustomQuestions"
                WHERE "UnitId" = t."Id" AND NOT "IsDel"
                  AND ("ScheduledAt" AT TIME ZONE 'UTC')::date = ask_day
            ) THEN
                CONTINUE;
            END IF;

            INSERT INTO "CustomQuestions"
                ("Text", "ContentCultureId", "ProfileId", "UnitId", "ScheduledAt", "Status",
                 "CreatedBy", "UpdatedBy", "CreatedAt", "UpdatedAt", "IsDel")
            VALUES
                (questions[qi], 1, t."ProfileId", t."Id", asked_at, 4,
                 NULL, NULL, asked_at - interval '3 days', asked_at + interval '1 day', false)
            RETURNING "Id" INTO q_id;

            INSERT INTO "CustomQuestionAudits"
                ("CustomQuestionId", "Text", "ContentCultureId", "ProfileId", "UnitId", "ScheduledAt", "Status",
                 "ChangeSummary", "CreatedBy", "UpdatedBy", "CreatedAt", "UpdatedAt", "IsDel",
                 "LoggedAt", "VersionNumber", "OperationType")
            VALUES
                (q_id, questions[qi], 1, t."ProfileId", t."Id", asked_at, 4,
                 seed_tag, NULL, NULL, asked_at - interval '3 days', asked_at + interval '1 day', false,
                 now(), 1, 'INSERT');
            q_added := q_added + 1;

            -- Answers: share of the team, nudged down a little per team so rates
            -- differ, at least 1, never more than the team size (or 11 texts).
            answer_count := GREATEST(1, LEAST(
                COALESCE(t."TeamSize", 5),
                11,
                round(COALESCE(t."TeamSize", 5) * GREATEST(0.3, shares[qi] - (t."Id" % 3) * 0.1))::int));

            FOR ai IN 1..answer_count LOOP
                -- Rotate the texts per team so teams don't all read the same.
                f_at := asked_at + make_interval(mins => 4 + ai * 6);

                INSERT INTO "CustomQuestionDailyFeedback"
                    ("CustomQuestionId", "UnitId", "FreeText", "CreatedBy", "UpdatedBy", "CreatedAt", "UpdatedAt")
                VALUES
                    (q_id, t."Id", answers[qi][((ai - 1 + t."Id"::int) % 11) + 1], NULL, NULL, f_at, f_at)
                RETURNING "Id" INTO f_id;

                INSERT INTO "CustomQuestionDailyFeedbackAudits"
                    ("CustomQuestionDailyFeedbackId", "CustomQuestionId", "UnitId", "FreeText", "ChangeSummary",
                     "CreatedBy", "UpdatedBy", "CreatedAt", "UpdatedAt", "LoggedAt", "VersionNumber", "OperationType")
                VALUES
                    (f_id, q_id, t."Id", answers[qi][((ai - 1 + t."Id"::int) % 11) + 1], seed_tag,
                     NULL, NULL, f_at, f_at, now(), 1, 'INSERT');
                f_added := f_added + 1;
            END LOOP;
        END LOOP;
    END LOOP;

    RAISE NOTICE 'Custom question demo seed: % questions, % answers added.', q_added, f_added;
END $$;

-- What was seeded, per team (question, day, answers of team size).
SELECT q."UnitId" AS team, bu."TeamSize" AS team_size,
       (q."ScheduledAt" AT TIME ZONE 'UTC')::date AS asked_on, q."Id" AS question_id, q."Text",
       count(f."Id") AS answers,
       round(count(f."Id") * 100.0 / NULLIF(bu."TeamSize", 0)) AS response_rate
FROM "CustomQuestions" q
JOIN "BusinessUnits" bu ON bu."Id" = q."UnitId"
LEFT JOIN "CustomQuestionDailyFeedback" f ON f."CustomQuestionId" = q."Id"
WHERE q."Id" IN (SELECT "CustomQuestionId" FROM "CustomQuestionAudits" WHERE "ChangeSummary" = 'Team Health demo seed')
GROUP BY q."UnitId", bu."TeamSize", q."ScheduledAt", q."Id", q."Text"
ORDER BY 1, 3;

COMMIT;

-- ── Cleanup (run by hand to remove only this seed) ──────────────────────────
-- BEGIN;
-- WITH seeded AS (SELECT "CustomQuestionId" AS id FROM "CustomQuestionAudits" WHERE "ChangeSummary" = 'Team Health demo seed')
-- DELETE FROM "CustomQuestionDailyFeedbackAudits" WHERE "CustomQuestionId" IN (SELECT id FROM seeded);
-- WITH seeded AS (SELECT "CustomQuestionId" AS id FROM "CustomQuestionAudits" WHERE "ChangeSummary" = 'Team Health demo seed')
-- DELETE FROM "CustomQuestionDailyFeedback" WHERE "CustomQuestionId" IN (SELECT id FROM seeded);
-- DELETE FROM "CustomQuestions" WHERE "Id" IN (SELECT "CustomQuestionId" FROM "CustomQuestionAudits" WHERE "ChangeSummary" = 'Team Health demo seed');
-- DELETE FROM "CustomQuestionAudits" WHERE "ChangeSummary" = 'Team Health demo seed';
-- COMMIT;
