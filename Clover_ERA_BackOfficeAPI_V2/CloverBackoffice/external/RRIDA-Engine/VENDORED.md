# RRIDA Engine — vendored copy

Upstream: **https://github.com/clivehays/RRIDA-Engine**
Synced to: **0729864** ("Nothing says when the check-back lands"), 16 November 2026
Previously: 06361d9 (20 October 2026), 99a804f (17 October 2026), bf3bc9b (6 October 2026). First vendored: 23 September 2026, at 5b3bd74

## The 0729864 sync (16 November 2026)

Five upstream commits since 06361d9: c36935d (D36), 90fe06d, 59de560, 3197c91 (tools only, not
vendored) and 0729864. Taken as the net change 06361d9..0729864, as Clive's note asked: two
fields (`reading_lands_in`, `check_back_lands_in`) were created and removed inside the range.

| Here | State |
|---|---|
| `dotnet/Rrida.Engine` | upstream 0729864 plus our two local changes only (tap replay; team-size headcount, below). Upstream's `Team.WorkingDaysBack` / `WorkingDaysAhead` and the `Simulation.cs` payload changes applied as they are |
| `kit/data`, `scenarios/`, `rotations/`, `demo-data/`, `kit/README.md` | unchanged upstream, still byte-identical |
| `DEVELOPER-GUIDE.md` | upstream 0729864 |
| `dotnet/Rrida.State` | unchanged upstream, still byte-identical |

Net payload contract, and where the backend follows it:

| Gone | Replaced by |
|---|---|
| `signal_read_day`, `days_until_read` | nothing: no field says when the check-back lands |
| `confirmation_due_day` | `confirmation_due_in` ("in 3 working days") |
| `awaiting_a_reading` | `awaiting_a_check_back`, no interval in it (EranGuard field list, EranGuardFallback) |
| `actions_taken[].date` | `actions_taken[].when` |

- **D36.** Eran's daily answer has six fields, `outward` after `body`: EranNarratorService asks
  for and stores it, EranGuard checks it like the body (a message stored with five reads as an
  empty one), EranGuardFallback fills it from the payload's own outward wording as the MVP's stub
  does, and the timeline returns `daily.outward`. The page draws it as its own block under the
  body with no button, and the quiet note drops "nothing is being asked of you" when it is there.
- **No run-day index on any manager-facing surface.** EngineChatContextService ports chat.js
  ("Delivered 3 working days ago", "the next check-back on it comes when a question on it comes
  round again"); the move card's "Given on day N", "confirm it was done on day N" and "Confirmed
  on day X … read on day Y" now read from `wordedWhen`, `confirmationDueIn` and `confirmedWhen`
  on the active action, phrased by the engine's own helpers through `EngineWorkingDays`.
- **Prompts.** `CloverEra/Prompts/rrida-eran.md` and `rrida-eran-tasks.json` are upstream
  0729864's PREAMBLE and tasks word for word, with "Clover ERA" read as "RRIDA" as before.
- **Not taken:** upstream's `PROMPT_FINGERPRINT` (the MVP's response-cache key) and
  `tools/read-horizon/`. The read path is untouched, as the note asks.
- Upstream's checks on its own tree: `npm test` 328 pass, `dotnet test` 96 pass, `build-kit`
  leaves no diff apart from CRLF line endings on a Windows checkout.

## Local change: team size as the headcount (16 November 2026)

Not upstream. The host enrols a device, not a person (a reinstall enrols the same person
again), so the floor `max(2, 33% of enrolled)` and the share rose with every duplicate. The host
now hands in the team size the manager set, dated (`TeamSizeHistory`), and the engine counts the
floor and the share against it. With no headcount given the engine is exactly upstream, so every
authored scenario and the MVP conformance suite run unchanged.

| File | Change |
|---|---|
| `Rrida.Engine/Team.Headcount.cs` | **new**: `HeadcountPeriod`, `Team.EnrolledCountOn(day)` (size in force, else `EnrolledOn(day).Count`) |
| `Rrida.Engine/Team.cs` | `TeamConfig.Headcount` (default empty); `Team` made `partial`; `InitHeadcount(config)` after `Roster = config.Roster;` |
| `Rrida.Engine/Tally.cs` | `ComputeFloor(team.EnrolledOn(day).Count, …)` → `ComputeFloor(team.EnrolledCountOn(day), …)` |
| `Rrida.Engine/Simulation.cs` | `DailyMessage`: `team.EnrolledOn(Math.Max(1, day)).Count` → `team.EnrolledCountOn(Math.Max(1, day))` |

Left as upstream on purpose: the senior roll-up's child visibility floor (`Simulation.cs`,
`child.EnrolledOn(...)`), an anonymity rule that the host never reaches (teams are replayed alone).
A re-sync copies upstream's files and re-applies the four edits above.

## The 06361d9 sync (20 October 2026)

Three upstream commits since 99a804f: bb00493 (D34), 5692b9e (guide) and 06361d9 (D35).

| Here | State |
|---|---|
| `dotnet/Rrida.Engine` | upstream 06361d9 **plus the manager-tap replay**, now in its own file `Simulation.ManagerTaps.cs` (a partial of `Simulation`). `Simulation.cs` differs from upstream by two things only: `partial` on the class and the five-line step-0 call `ApplyManagerTaps(team, day, events)` at the top of `TickTeam`. A re-sync copies upstream's `Simulation.cs` and re-applies those two edits (Clive's Q5; a hook upstream would remove even those) |
| `kit/data` | byte-identical to 06361d9 (only `rotation/individual.json`'s note changed: it is the demo rotation) |
| `rotations/rotation-90.json` | **new**, upstream `data/rotation-90/rotation.json`, the production rotation |
| `rotations/rotation-demo-6day.json` | upstream `data/rotation-demo-6day.json`, byte-identical to `kit/data/rotation/individual.json` |
| `DEVELOPER-GUIDE.md`, `kit/README.md` | upstream 06361d9 |
| `scenarios/`, `demo-data/`, `Rrida.State` | unchanged upstream, still byte-identical |

- **D35.** `Simulation.TickTeam` step 6 writes the daily message only on a day the team is asked
  (`if (team.IsAskedOn(day))`). The backend timeline's weekend rule is now exactly that rule
  (`EranCoachTimelineService.IsShown`), kept only so rows stored by an older run stay hidden until
  the team's next run deletes them.
- **D34, as the client confirmed.** Every team is asked on the production rotation: all ninety
  individual and all ninety team questions, one of each per asked day, looping (day d asks 120+d
  and 300+d). `RridaEngineContextFactory.Context` is built from the kit's data, calendar, config and
  tracks with `rotations/rotation-90.json` (FromKit always loads the kit's six-day file, so the
  context is assembled rather than the vendored engine edited). The demo's six exist only for the
  engine team's authored scenarios, which the reference itself pins to them: the scenario seeder
  therefore refuses (POST seed-scenario), and the workbook import runs on the ninety, taking one of
  the day's own chips where the workbook's labels belong to another question. Answers stored before
  the switch were given to the six's questions; the replay leaves them out (and logs how many)
  rather than letting the engine refuse the whole run. Migration 20261020133345_DropEngineTeamRotation
  removes the per-team rotation column added in 20261020102702.

## The 99a804f sync (17 October 2026)

Upstream's `dotnet/` port is caught up with the JavaScript reference again (its commit
3721e7e ported D27–D30, and D31–D33 landed in both in the same commits), so this copy now
takes the upstream port as it is rather than our own port of D27–D30.

| Here | State |
|---|---|
| `kit/data` | **byte-identical** to upstream 99a804f, including the new required `library/outward.json` (D31) and `meta.json` counts.actions 3080 |
| `scenarios/` | byte-identical to upstream `data/scenarios` |
| `demo-data/coaching-directions.json` | byte-identical to upstream (unchanged since bf3bc9b) |
| `DEVELOPER-GUIDE.md`, `kit/README.md` | upstream 99a804f |
| `dotnet/Rrida.State` | byte-identical to upstream |
| `dotnet/Rrida.Engine` | **byte-identical to upstream 99a804f, plus the manager-tap replay** (60 added lines in `Simulation.cs`, nothing removed; section below) |

What the 99a804f sync brought, and where it shows in the backend:

- **D31, outward conditions.** A negative chip naming a cause outside the team votes in a
  fourth, outward bucket that nothing selects from. Daily payloads carry `outward_condition`,
  `outward_move`, `outward_goes_to`, `outward_strength`, `outward_days_above`,
  `outward_how_you_will_know` (all or none); weekly payloads carry `outward_condition`. It is
  never a cycle, so it never reaches a move card, an action dot or a weekly flag's action.
- **D32, both tracks vote.** `EngineConfig.Implemented` and the `FromKit` default are both
  `individual, team` upstream; `RridaEngineContextFactory` uses the default.
- **D33.** The weekly payload adds `how_the_questions_compare`, `awaiting_a_reading` and
  `answer_balance` (renamed from `line_1_trend`; nothing in the backend read the old key).
- **Library.** 3080 actions (118 build actions, 58 new worked-if conditions). Every chip id and
  question id is unchanged, so stored answers stay valid; what a chip routes to changed.
- **Eran.** `rrida-eran-tasks.json` is the reference's `TASKS` at 99a804f, extracted and
  checked byte for byte against the bf3bc9b extraction first: `daily`, `outcome` and `weekly`
  changed; `rrida-eran.md` (PREAMBLE), the daily schema and the chat prompt did not.

**How it was verified.** Each of the eight scenarios run on the JavaScript reference at 99a804f
and on this copy: cycles, states, feed items and every payload value compared — 105 cycles,
720 states, 1,459 feed items, 17,435 payload values, **zero differences**. The backend suite
(including the manager-tap replay and the seeder-reproduces-the-MVP tests) passes on it.

## The D27–D30 port (7 October 2026) — superseded on 17 October by upstream's own port

Ported from the reference diff `5b3bd74..bf3bc9b` (`src/rotation.js`, `src/engine/team.js`,
`route.js`, `simulation.js`, `tally.js`). Full definitions: `kit/definitions` upstream,
`handover/provisional-definitions-D1-D30.md`.

- **D27 — the signal read is keyed on the issue file.** `Rotation.NextReadDay`,
  `QuestionsCarrying`, `PeriodFor` (on `IRotation`); `Team.ReadFileOnDay` replaces the
  chip read for the baseline, the read, the D23 deferral and rung two. `Cycle.ReadTrack`.
  Loader guards retired: `cycleLength` need not be 6, an element may repeat.
- **D28 — the condition watch.** `Team.ConditionWatch`, `UpdateWatch(day)` (every asked day,
  after the state, before selection), `WatchList(day, limit, staleAfter)`, `CycleOpenOn`.
  Selects nothing. `Tally.SignalStrength` (moved; `Simulation.SignalStrength` delegates).
- **D29 — the slot frees at confirmation.** `Team.AwaitingRead`, `PendingRungTwo`,
  `SlotIsFree`, `DueReads`, `ParkConfirmed`; `ResolveCycle`, `AdvanceToRungTwo` and
  `CloseOut` take a target cycle. Candidates whose condition is under measurement are
  filtered out of selection (`SelectionAttempt.AwaitingRead`).
- **D30 — one worksheet session per ten asked days.** `Route.SessionCadenceDays`,
  `Routing.CadenceHeld`, `Team.LastSessionAskedDay`; majority-neutral candidates are held
  out of selection while the cap is in force (`SelectionAttempt.CadenceHeld`).
- Also from the same commits: the reminder payload no longer carries the case for the move
  (`triggering_condition`, `how_it_came_through`, `share_of_team`, `issue_title`,
  `worksheet_substitution`), and the monthly summary reads the last *resolved* state.

**How it was verified.** Upstream's own suite (`dotnet/Rrida.Deterministic.Tests`) run
against an upstream clone at bf3bc9b with these sources copied in: **94 of 94 pass**,
including `The_eight_scenarios_reproduce_their_runs_day_by_day`, which checks every day,
cycle, routing and Eran payload of the eight scenarios. The test bench's `BenchRotation`
needs the three new `IRotation` members and the D27 filler isolation from
`src/chipsweep.js`; that is a change to upstream's test code, not shipped here. D28 is not
asserted by the fixtures, so its watch lists were compared against the JavaScript reference
at seven checkpoints for all eight teams: 316 entries, zero differences.

## Changing anything here

`kit/data` decides what the product calls trouble, and `Rrida.Engine` has to reproduce the
reference. Clive is the authority on the definitions (D1–D30, all provisional):

- **a threshold or a definition** changes upstream: edit `kit/data/thresholds.json`,
  `node tools/kit/build-kit.mjs`, commit with the regenerated fixtures;
- **engine behaviour** changes in the reference first, then here, verified against the
  regenerated fixtures as above.

## Updating

1. Pull upstream. Read any new `handover/for-the-developer-*.md` and the reply notes.
2. Copy over, byte for byte: `kit/data`, `DEVELOPER-GUIDE.md`, `kit/README.md`, `data/scenarios` to
   `scenarios/`, `data/demo-data/coaching-directions.json` to `demo-data/`, and
   `data/rotation-90/rotation.json` and `data/rotation-demo-6day.json` to `rotations/`.
3. Copy upstream `dotnet/Rrida.Engine` and `dotnet/Rrida.State` over ours, keeping our own
   `Simulation.ManagerTaps.cs`, then re-apply the two edits to `Simulation.cs`: `partial` on the class,
   and the step-0 call `ApplyManagerTaps(team, day, events);` at the top of `TickTeam`.
4. Diff the reference `src/` since the last sync and port anything that touches the engine or Eran's
   prompts (`CloverBackoffice.API/CloverEra/Prompts`).
5. Copy these sources into an upstream clone and run `dotnet test` there; it must be green.
6. Run our own suite (`EngineFeedCountTests` pins the reference's message counts).

## Open

- **Licence and ownership are unstated.** The upstream repository has no LICENCE file.
- Both tracks vote (D32); the guide said otherwise until 5692b9e.
- The 72-hour confirmation is **3 asked days**. One value in `thresholds.json`.

## Manager taps replayed (14 October 2026)

The reference's day loop starts with a step 0, `applyManualConfirmations` (`src/engine/simulation.js`):
the presenter's real confirmations are kept as a journal and replayed at the start of the day they
were given, so a reload reproduces the run. The .NET `Simulation` had no step 0, so a host could only
confirm through the script. Ported as `Simulation.ManagerConfirmations` and `ManagerDeclines`, applied
in `TickTeam` before the scripted confirmation, exactly as the reference orders them.

One deliberate difference: a tap given on the day its action was delivered cannot match at the start
of that day, because the cycle does not exist yet. The reference loses it on reload; here it applies
at the start of the next day the cycle is in the slot, under the day it was given.

Held to the reference by `CloverBackoffice.Tests/Engine/EngineManagerTapReplayTests`: for each of the
eight authored scenarios, a run confirmed by the scripted manager and a run fed the same confirmations
as taps produce identical feeds, cycles and states.

## Coaching directions (14 October 2026)

`demo-data/coaching-directions.json` is byte-identical to the reference's
`data/demo-data/coaching-directions.json` at bf3bc9b. It is not part of the kit: the 19 directions are a
manager-facing label (spec 5.3), never read by the engine. The API copies it to
`CloverEra/Data/rrida-demo-data/` and Eran is given the label as `what_this_is_about`, exactly as the
reference's `src/eran/context.js` does.

Eran's prompts (`CloverBackoffice.API/CloverEra/Prompts/rrida-eran.md` and `rrida-eran-tasks.json`) are
the reference's `PREAMBLE` and `TASKS` from `src/eran/prompts.js` at bf3bc9b, copied word for word.
