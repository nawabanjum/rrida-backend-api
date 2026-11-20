# RRIDA Engine integration plan

Bringing Clive's deterministic engine (`D:\Dev\RRIDA_Engine\RRIDA-Engine`, github.com/clivehays/RRIDA-Engine) into this API.

**Status:** plan only. Nothing in Module 1 has started. The reference tables and migration `20260922090000_AddDeterministicReferenceData` already exist in this repo from the earlier plan and are reused in step 1.2 with their source changed to the kit.

---

## Ground rules

1. **No migration is ever run against a database by us.** `dotnet ef database update` is not run, and neither is anything that writes to Dev, Pre-Prod or Production.
2. **Every database change ships as two things:** an EF migration under `CloverBackoffice.Repository/Migrations/`, and a matching idempotent script under `CloverBackoffice.Repository/Scripts/DeterministicLayer/` that Nawab runs by hand in pgAdmin. The script mirrors the migration exactly, runs top to bottom on F5, is safe to run twice, records itself in `__EFMigrationsHistory`, and ends with a summary `SELECT`.
3. **The engine is never edited.** `Rrida.Engine` and `Rrida.State` are vendored as they are. Their 94 conformance tests run in our CI. A change we need goes back to Clive; a threshold change is Neil's, applied upstream by editing `kit/data/thresholds.json` and regenerating the kit.
4. **The kit is the source of truth for content.** `kit/data` (2.7 MB) ships with the API. `kit/fixtures` (41 MB) is test-only and is not deployed.
5. **Each step ends with a review.** Nothing starts until the previous step is approved.

### Verified before planning (23 September 2026)

- `dotnet test` in the engine repo: **94 passed, about 2 seconds**.
- `dotnet run --project samples/Rrida.Sample -- team-platform`: 124 dates, 90 asked days, 8 deliveries, 90 log records.
- A scratch **net9.0** project referencing `Rrida.Engine` and `Rrida.State`: **0 errors, 0 warnings**. Both libraries have **zero NuGet dependencies**.

### The three facts the design rests on

1. `Team.RecordDay(day)` takes no answers. It reads them from `TeamConfig.Script`, keyed by **asked-day index**, so we build the script from our database.
2. `Resolver` keeps per-team accumulators in memory and has **no snapshot or restore**. Both accumulators are recurrences: a day fed twice double-counts, a day skipped is lost.
3. So the integration **replays recorded facts** (answers, confirmations, declines) from day one on every run. The engine is deterministic, so the same facts always give the same state.

### Blocking prerequisites (outside the code)

| # | Item | Owner |
|---|---|---|
| P1 | **Licence and ownership.** The engine repo has no LICENCE file and no copyright header. Confirm before vendoring. | Clive |
| P2 | **Member identity from the mobile app.** Answers today carry no member id, so distinct contributors cannot be counted. | Mobile team |
| P3 | **Chip bank swap.** The engine only understands kit chip ids (`191-I6`); our `DailyReasons` are a different bank. | Product + mobile |
| P4 | **Per-team working week and start date.** Default `mon-fri` is acceptable to start. | Product |
| P5 | Thresholds D1–D26 are **provisional pending Neil**; team-track delivery is off; 72 hours currently means 3 asked days. | Neil |

---

# Module 1 — Foundation

Get the engine building inside our solution, the kit's content into our database, and real answers captured in the shape the engine needs.

### Step 1.1 — Vendor the engine

**Work:** add the engine repo under `external/RRIDA-Engine` as a git subtree or submodule, pinned to a tag. Add `Rrida.Engine` and `Rrida.State` to `CloverBackoffice.sln` as project references. Copy `kit/data` to `CloverBackoffice.API/CloverEra/Data/rrida-kit/` as content copied to output, keeping `.gitattributes` (`* -text`) so the JSON stays byte-identical. Add their test project to CI. Register an `EngineContext` singleton built with `EngineContext.FromKit`.

**Database:** none.

**Done when:** our solution builds on net9.0 with the net8.0 libraries; their 94 tests run green in our CI; a smoke test resolves `EngineContext` from DI and reads the kit's `meta.json` version.

**Blocked by:** P1.

### Step 1.2 — Kit content into the reference tables

**Work:** point the reference loader at `kit/data` instead of the older handover pack, and load `RefQuestions`, `RefChips`, `RefActions`, `RefIssueFiles`, `RefWorksheets`, `RefWorksheetPhases`, `RefCapabilityFlags` as one new `RefDataVersion`. Assert the kit's declared counts and halt on mismatch. Both tracks load; `Track` distinguishes them. Add a read API that serves a question and its three chip sets for the mobile app.

Note: the engine reads the kit files directly. These tables exist to serve the mobile app and to resolve ids for display, not to feed the engine.

**Database:** small additions to `RefDataVersions` — `KitVersion`, `DataVersion`, `BaselineTag`.
- Migration: `2026XXXX_AddKitVersionToRefDataVersions`
- Script: `Scripts/DeterministicLayer/02_AddKitVersionColumns.idempotent.sql`
- Plus `01_AddReferenceTables.idempotent.sql`, already written and **not yet run**.

**Done when:** a load produces 90 + 90 questions, 1,620 + 1,620 chips, 2,919 actions, 118 issue files, 56 worksheets, 210 phases and 85 capability flags; a deliberately broken file refuses the load and writes nothing.

**Blocked by:** 1.1.

### Step 1.3 — Identity, membership and capture

**Work:** member identity and effective-dated membership, kept unreachable from every manager-facing read path. Per-team settings: working week, start date, delivery tracks. The mobile submit endpoint writes engine facts: one answer per member per track per day, 0–3 kit chip ids, free text required when no chip is picked. A helper converts a date to the team's asked-day index, and the two are stored side by side.

**Database:** `EngineMembers`, `EngineTeamMemberships`, `EngineTeamSettings`, `EngineAnswers`, `EngineAnswerChips`.
- Migration: `2026XXXX_AddEngineIdentityAndAnswers`
- Script: `Scripts/DeterministicLayer/03_AddEngineIdentityAndAnswers.idempotent.sql`

**Done when:** a member can answer both tracks once a day and not twice; the enrolled count for a past date is right; a manager-role query cannot join an answer to a person.

**Blocked by:** P2, P3, P4.

---

# Module 2 — Engine runtime

Replay the facts through the engine, store what comes out, and run it daily.

### Step 2.1 — Results storage and the sink

**Work:** implement `IStateSink.Write(teamId, ResolvedRecord)` against our database, flattening the resolved record. **Upsert on (TeamId, Day)**, never insert. Every row carries `Schema` and `ConfigId`. Cycles and deliveries store snapshots of the delivered action text, condition, route and phase run, so a past delivery always reports what it actually delivered. Feed items store `Kind`, `Situation` and the payload.

**Database:** `EngineStateDaily`, `EngineCycles`, `EngineCycleDeliveries`, `EngineFeedItems`, `EngineTeamNotes`.
- Migration: `2026XXXX_AddEngineRunResults`
- Script: `Scripts/DeterministicLayer/04_AddEngineRunResults.idempotent.sql`

**Done when:** writing the same day twice leaves one row with the later values; no state row exists without a config id.

**Blocked by:** 1.3.

### Step 2.2 — The replay runner

**Work:** for one team, read settings, roster and every recorded answer, build `TeamConfig.Script` keyed by asked-day index, set `AutoConfirm = false`, and replay day 1 to today: `RecordDay`, `resolver.Resolve`, `ApplyState`, `AttemptSelection`, and the recorded confirmations and declines on their own days. Write results through the step 2.1 sink. Confirmations and declines are recorded as facts by their own endpoints.

**Database:** `EngineConfirmations`, `EngineDeclines`.
- Migration: `2026XXXX_AddEngineConfirmationsAndDeclines`
- Script: `Scripts/DeterministicLayer/05_AddEngineConfirmationsAndDeclines.idempotent.sql`

**Done when:** one scripted scenario replayed through our persistence produces the same log records, cycles and outcomes as the sample host does in memory. This parity test is the acceptance test for the whole module.

**Blocked by:** 2.1.

### Step 2.3 — The daily job

**Work:** a `BackgroundService` on the existing `JobSchedules` pattern, per team, under the advisory lock pattern we already use, idempotent, with replay time measured and logged. Handles teams joining mid-life and days with no answers.

**Database:** none, unless the timing measurement says we need a resume point, which is a decision for the review at the end of this step.

**Done when:** running the job twice in a row changes nothing; replay time at our real team count is acceptable and written down.

**Blocked by:** 2.2.

---

# Module 3 — Surfaces

Give the manager, the team member and the senior manager what the engine produces.

### Step 3.1 — Manager and member APIs

**Work:** manager endpoints for the current state (both axes plus the Headwinds flag), the active action with its condition and route, one-tap confirm, decline with reason, and the feed. Mobile endpoint for team notes. Proportions only, never counts: the concentration reading leaves as a word and a share, never a member.

**Database:** none.

**Done when:** a manager can be handed an action, confirm it, and see the outcome after the read; no endpoint exposes a member id or a chip-level count.

**Blocked by:** 2.3.

### Step 3.2 — Eran

**Work:** drain `team.Feed` per day and turn each payload into prose with our existing Anthropic client. Eran phrases and never selects: the action, route, rung and condition are already decided. Store prompt, response, model and tokens. Cache per team, day and prompt type.

**Database:** `EngineLlmCalls`, or reuse `AIRequestLogs` if the review prefers it.
- Migration: `2026XXXX_AddEngineLlmCalls`
- Script: `Scripts/DeterministicLayer/06_AddEngineLlmCalls.idempotent.sql`

**Done when:** every delivered action carries prose generated from its own payload, and re-running a day does not re-bill or change the wording.

**Blocked by:** 3.1.

### Step 3.3 — Roll-up, cross-team and the front end

**Work:** expose `Simulation.RollUp` and `CheckCrossTeam` for a Portfolio Manager: per child team the state, cycle status and closed-versus-failed counts, never the action or the chips, with the visibility floor of 4 enforced where the data is served. The cross-team trigger is built and labelled provisional. Front end: manager page, roll-up page, and the mobile team-note screen.

**Database:** none.

**Done when:** a Portfolio Manager sees the roll-up with no child-team actions in it, and a child team under 4 members reads as calibrating whatever its real state.

**Blocked by:** 3.2, and the design calls in build spec §7 being confirmed.

---

## Not in this plan

Team-track delivery (asked and recorded today, but casts no vote until Neil ratifies the external-locus route and spec 7.1b), threshold changes of any kind, the neutral-minority route, the empty Yellow tally in Stall, and cross-team threshold values.

## Database artefacts, all together

| Script | Migration | Module |
|---|---|---|
| `01_AddReferenceTables.idempotent.sql` | `20260922090000_AddDeterministicReferenceData` | written, not run |
| `02_AddKitVersionColumns.idempotent.sql` | `AddKitVersionToRefDataVersions` | 1.2 |
| `03_AddEngineIdentityAndAnswers.idempotent.sql` | `AddEngineIdentityAndAnswers` | 1.3 |
| `04_AddEngineRunResults.idempotent.sql` | `AddEngineRunResults` | 2.1 |
| `05_AddEngineConfirmationsAndDeclines.idempotent.sql` | `AddEngineConfirmationsAndDeclines` | 2.2 |
| `06_AddEngineLlmCalls.idempotent.sql` | `AddEngineLlmCalls` | 3.2 |

Every one is run by hand, by Nawab, in pgAdmin.
