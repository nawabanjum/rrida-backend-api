# Testing the deterministic engine and Weekly Flags, end to end

Dev environment. Branch `deterministic-layer-engine` (main is merged in).

**Test team: unit 216 "QA TM1 Team" (profile 56).** It has no weekly flags and no
milestones, so nothing it produces can be confused with existing data.

**Control team: unit 188.** It holds 4 `DemoSeed` weekly flags and 2 milestones. Check it
before and after: it must not change. That is the proof the integration did not disturb
what was already there.

**Token: RRIDA Admin, `nawab.anjum@rrida.com` (user 87).** It is the only verified account
that can reach every endpoint here; unit 216's own manager (user 141) has never accepted
their invitation and cannot log in.

```
POST /api/profile/login          (form fields: email, password)
→ use the returned token as:  Authorization: Bearer <token>
```

---

## Step 0. Before you start

Take the "before" picture of the control team, so the comparison at the end is real:

```sql
SELECT "UnitId", "WeekStartDate", "Source", left("WhatCameUp", 40) AS what
FROM "TeamWeeklyFlags" WHERE NOT "IsDel" ORDER BY "UnitId", "WeekStartDate";
```

Expect **8 rows, all `DemoSeed`**, on units 188 and 211.

Deploy the branch to Dev and open Swagger. These endpoints should be listed:

- `POST /api/rrida/reference-data/load`, `GET /api/rrida/reference-data/active`
- `GET /api/rrida/engine/scenarios`, `POST /api/rrida/engine/seed-scenario`
- `POST /api/rrida/engine/run`, `POST /api/rrida/engine/run-all`, `POST /api/rrida/engine/narrate`
- `PUT|GET /api/rrida/engine/team-settings`
- `GET /api/rrida/manager/state`, `active-action`, `feed`, `roll-up`, `POST confirm`, `decline`
- `GET|POST /api/v1/engine/...` (member app)

If they are missing, the deployed build is not this branch.

---

## Step 1. Run the database scripts

In pgAdmin, **in this order**, from `CloverBackoffice.Repository/Scripts/DeterministicLayer/`:

| # | Script | Creates |
|---|---|---|
| 1 | `01_AddReferenceTables.idempotent.sql` | 8 reference tables |
| 2 | `02_AddKitVersionColumns.idempotent.sql` | 4 columns |
| 3 | `03_AddEngineIdentityAndAnswers.idempotent.sql` | members, membership, settings, answers, chips |
| 4 | `04_AddEngineRunResults.idempotent.sql` | state, cycles, deliveries, feed, notes |
| 5 | `05_AddEngineConfirmationsAndDeclines.idempotent.sql` | confirmations, declines |
| 6 | `06_AddEngineLlmCalls.idempotent.sql` | Eran call log + 2 feed columns |

Each ends with its own summary. **Expected: 20 new tables, all with 0 rows.** Nothing
existing is altered.

Verify:

```sql
SELECT count(*) AS engine_tables
FROM information_schema.tables
WHERE table_schema = 'public'
  AND (table_name LIKE 'Ref%' OR table_name LIKE 'Engine%');
-- expect 20

SELECT "MigrationId" FROM "__EFMigrationsHistory"
WHERE "MigrationId" = '20260930153617_SyncEngineModelSnapshot';
-- present once the pipeline has deployed; the tables exist either way
```

Restart the API afterwards so EF sees the new tables cleanly.

---

## Step 2. Load the coaching content

```
POST /api/rrida/reference-data/load
```

**Expected** (`content.counts`):

| Item | Count |
|---|---|
| questions | 180 (90 individual + 90 team) |
| chips | 3,240 |
| actions | 2,919 |
| issueFiles | 118 |
| worksheets | 56 |
| worksheetPhases | 210 |
| capabilityFlags | 85 |

Also `specVersion: "4.7"`, `dataVersion: "2026-09-21-team-bank"`.

Then check the content is readable:

```
GET /api/rrida/reference-data/active
GET /api/rrida/reference-data/question/191
```

Question 191 is *"Are blockers cleared quickly when they come up?"* with **6 negative,
6 indifferent and 6 positive** chips. The response must carry chip text and order only —
**no locus tag and no routing columns.** Those are the engine's business, not the member's.

> If the counts disagree the load refuses and writes nothing. That is the kit's own
> assertion doing its job, not a bug.

---

## Step 3. See the scenarios

```
GET /api/rrida/engine/scenarios
```

**Expected: 8 scenarios.** `team-platform` shows roster 7, working week mon-fri,
90 scripted days, horizon 124.

---

## Step 4. Seed the test team

```
POST /api/rrida/engine/seed-scenario
{ "businessUnitId": 216, "scenarioId": "team-platform", "autoConfirm": true, "reset": false }
```

This writes 7 members, their membership, 90 days of answers, then replays the engine and
plays the manager's confirmations.

**Expected in the response:**

| Field | Value |
|---|---|
| membersSeeded | 7 |
| answersSeeded | 1,260 |
| chipsSeeded | 2,265 |
| run.askedDays | 90 |
| run.days | 124 |
| run.enrolled | 7 |
| run.stateRecords | 90 |
| confirmationsSeeded | 8 or 9 (one per delivered rung) |
| cycleOutcomes | about `{ closed: 5, failed: 2, running: 1 }` |
| run.configId | a 12-character hash, the same every time |

The reference host produces **8 deliveries: 5 closed, 2 failed, 1 running**. Matching that
is the real proof: it means our capture, replay, cycle handling and storage behave exactly
as the engine does on its own.

> A difference of one in the closed/failed split is worth investigating rather than
> shrugging at — the engine is deterministic, so the same answers must give the same arc.

---

## Step 5. Check what the engine stored

```sql
SELECT (SELECT count(*) FROM "EngineAnswers"      WHERE "BusinessUnitId" = 216) AS answers,
       (SELECT count(*) FROM "EngineAnswerChips" c JOIN "EngineAnswers" a ON a."Id" = c."AnswerId"
         WHERE a."BusinessUnitId" = 216)                                        AS chips,
       (SELECT count(*) FROM "EngineStateDaily"   WHERE "BusinessUnitId" = 216) AS state_days,
       (SELECT count(*) FROM "EngineCycles"       WHERE "BusinessUnitId" = 216) AS cycles,
       (SELECT count(*) FROM "EngineFeedItems"    WHERE "BusinessUnitId" = 216) AS feed,
       (SELECT count(*) FROM "TeamWeeklyFlags"    WHERE "UnitId" = 216 AND NOT "IsDel") AS flags;
```

Expect roughly: answers 1,260 · chips 2,265 · state_days 90 · cycles 8 · feed 90+ · flags 6–8.

The cycles themselves:

```sql
SELECT c."CycleKey", c."IssueFileKey", c."Route", c."ShareText", c."Outcome",
       c."DeliveredDay", c."SignalReadDay",
       (SELECT count(*) FROM "EngineCycleDeliveries" d WHERE d."CycleId" = c."Id") AS rungs
FROM "EngineCycles" c WHERE c."BusinessUnitId" = 216 ORDER BY c."CycleIndex";
```

What to look for:

- `ShareText` reads in **words** ("most of the team"), never "5 of 9";
- a cycle that laddered has **2 rungs**, and rung one's row still holds rung one's text;
- routes vary (`ask`, `team`, `content`) — that is the share deciding delivery.

### Idempotency, which matters more than it looks

```
POST /api/rrida/engine/run?businessUnitId=216
```

Run it twice. **The counts above must not change.** Same 90 state rows, same 8 cycles, no
duplicates. That is what makes the daily job safe to re-run after a deploy.

### Anonymity

```sql
-- what a manager can see must never reach a person
SELECT "ConcentrationReading", "ConcentrationTopShare" FROM "EngineStateDaily"
WHERE "BusinessUnitId" = 216 ORDER BY "Day" DESC LIMIT 3;
```

A **word and a share, never a member id**. Members live only in `EngineMembers` /
`EngineAnswers`, and no manager endpoint returns them.

---

## Step 6. Weekly Flags — the integration

```
GET /api/teams/216/weekly-flags
```

**Expected:** several flags, newest first, each with `weekStartDate` (a **Monday**),
`weekEndDate` (the Sunday), a `label` like "Week of 6 Oct", plus `whatCameUp` and
`suggestedAction`.

Check the content reads correctly:

- **whatCameUp** names the *theme* and the share in words — never a chip, a number or a person;
- **suggestedAction** is the action **as delivered**, often with "You will know it worked
  if…" and sometimes a worksheet code;
- a week where the causes read as outside the team says so, and explicitly gives **no team
  action** — that is Headwinds behaving correctly, not a gap.

```sql
SELECT "WeekStartDate", "WeekEndDate", "Source", left("WhatCameUp",60) AS what,
       left("SuggestedAction",60) AS action
FROM "TeamWeeklyFlags" WHERE "UnitId" = 216 AND NOT "IsDel" ORDER BY "WeekStartDate";
```

Every row must say **`Source = 'Engine'`**.

### The control check — do not skip this

```sql
SELECT "UnitId", "WeekStartDate", "Source" FROM "TeamWeeklyFlags"
WHERE "UnitId" IN (188, 211) AND NOT "IsDel" ORDER BY "UnitId", "WeekStartDate";
```

**Still 8 rows, still `DemoSeed`, unchanged.** The engine only ever writes rows it owns.

Also confirm the existing page still works for the old teams:

```
GET /api/teams/188/weekly-flags
GET /api/teams/188/weekly-flags?weekStartDate=2026-08-24
GET /api/team-health/timeline?teamId=188&...   (as the page calls it)
GET /api/team-health/milestones?teamId=188
```

All must behave exactly as before.

---

## Step 7. The manager's side

```
GET /api/rrida/manager/state?businessUnitId=216
GET /api/rrida/manager/active-action?businessUnitId=216
GET /api/rrida/manager/feed?businessUnitId=216&take=20
```

**state** — `vertical` (cruise/drift/stall), `horizontal` (unlocated/internal/external),
`headwindsFlag`, `limb`, and a `configId`. `isCalibrating` should be false by now.

**active-action** — the running cycle with its delivered text, its worked-if, route, rung,
and `confirmationDue`. If the seeded run ended with everything resolved, this returns
"No action is active for this team", which is a valid answer.

**feed** — daily, outcome, note, weekly and monthly items with their payloads.

Try the confirmation path yourself on the running cycle:

```
POST /api/rrida/manager/confirm
{ "businessUnitId": 216, "cycleKey": "C8", "workedIfMet": true }
```

Expect either the updated action, or *"This rung has already been confirmed"* — because
seeding already confirmed it. Both prove the rule is enforced.

---

## Step 8. Eran (optional — this one costs money)

```
POST /api/rrida/engine/narrate?businessUnitId=216&take=5
```

**Expected:** `phrased: 5`, and those feed items now carry `prose`. Re-run it: it phrases
only items that have none, so nothing is re-billed and no wording a manager has read
changes.

```sql
SELECT "PromptType", "Model", "DurationMs", "Error" IS NULL AS ok
FROM "EngineLlmCalls" WHERE "BusinessUnitId" = 216 ORDER BY "Id" DESC LIMIT 5;
```

Prose must never name a person and never quote a chip.

---

## Step 9. Front end

The frontend needs **no change**: Weekly Flags is the same endpoint and the same shape.

1. Open Team Health for **QA TM1 Team (216)** → the week dropdown lists the engine's weeks,
   and each shows "What came up" and "Suggested action".
2. Open it for **188** → the old DemoSeed flags, exactly as before.
3. Switch weeks in the dropdown → `?weekStartDate=` returns that week's flag.

---

## Cleaning up

To re-run the whole test from scratch on the same team:

```
POST /api/rrida/engine/seed-scenario
{ "businessUnitId": 216, "scenarioId": "team-platform", "autoConfirm": true, "reset": true }
```

`reset: true` deletes that team's engine answers, confirmations and derived results. It does
**not** delete member records, and it does not touch any other team.

To remove the engine's flags only:

```sql
UPDATE "TeamWeeklyFlags" SET "IsDel" = true
WHERE "UnitId" = 216 AND "Source" = 'Engine';
```

---

## If something looks wrong

| Symptom | Where to look |
|---|---|
| Load refuses with a count mismatch | The kit in the deployed build is not the vendored one |
| "The RRIDA kit was not found" | `kit/data` did not reach the build output; check `Rrida:KitDataPath` |
| Seed says the team already holds engine data | Send `reset: true` |
| 0 deliveries after seeding | Reference data was never loaded, or the team's start date is in the future |
| Deliveries but no weekly flags | The weeks fall outside the last 26, or each week already holds a non-Engine flag |
| Flags exist but the page shows none | The page is asking for a week with no flag; quiet weeks get no row on purpose |
| A count changes when you re-run | An upsert key is wrong — this is the one result worth stopping for |

## What this does not cover

- The **daily job** stays disabled (`JobSchedules:EngineDailyRun.Enabled = false`). Seeding
  and the run endpoint do the same work, on demand.
- **Team-track answers cast no vote** — asked and recorded, but no action, by design.
- **Thresholds are provisional** pending Neil, which is why every row stores its `configId`.
