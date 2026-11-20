# The conformance kit

Everything a port needs to prove it computes the same thing as the JavaScript reference,
and nothing it does not.

The reference is the JavaScript in `src/`. Its own test suite is its contract. This kit
is that contract written down as data, so a second implementation can be held to it
without reading the first one.

**Every threshold in here is provisional, and Clive is the authority on it.** Changing one
changes what the product calls trouble. See `definitions/D1-D30-provisional-definitions.md`.

---

## What a passing port is

> **Every fixture reproduced, per day, to a tolerance of 1e-9 on every floating-point
> value, and exactly on every integer, string, boolean and null.**

That is the whole bar. Not "close enough to be useful", not "the same states came out":
the same numbers on the same days, in the same order, including the days where nothing
happened.

Three things that are part of the bar and easy to miss:

1. **A null is not a zero.** A baseline that does not exist yet, a `z` with no variance
   behind it, a concentration reading with nothing to attribute: all of these are null in
   the fixtures, and a port that writes 0 there has changed what the record means.
2. **The log record is rounded and the rest is not.** `logRecord` in every state fixture
   carries values rounded to six decimal places, because that is what the reference's own
   record does before a store sees it. The `tracks` and `classifier` blocks beside it are
   full double precision. Both must match: the rounding is behaviour, not presentation.
3. **Order matters where the fixture is a list.** Tally entries, ranked candidates and
   eligible actions are written in a total order the reference defines, and ties in the
   selection break on a documented chain ending in action id, so runs are reproducible.

If a value cannot be matched at 1e-9, say which one and why. Do not loosen the tolerance.

---

## What is in here

| Folder | What it holds |
|---|---|
| `data/` | 180 questions and 3240 chips across both banks, the merged library, both rotations, the schedule, the calendar, every threshold, and a schema for each file. |
| `definitions/` | D1 to D30, handover sections A to N, and the two spec findings. |
| `fixtures/state/` | 8 scenarios and 24 section K cases, 3,600 days of expected state in all. |
| `fixtures/engine/` | 8 scenarios over 720 asked days, the chip bench, and one golden run per daily situation the scenarios do not reach. |

### `data/`

Both banks and the library are copied out of the reference's own `data/` byte for byte.
Nothing is transformed, because the point of a conformance kit is that both
implementations read the same bytes.

Two files are **new here**, and they carry what the JavaScript holds in code rather than in
data:

**The individual rotation in here is the demo's six-day one, and production runs the
ninety.** `rotation/individual.json` has a cycleLength of 6: six of the ninety
individual questions, looping. Six reach 26 of the 85 issue buckets and the other 59 are
unreachable, because the questions that would raise them are never asked. Production runs
`data/rotation-90/rotation.json` in the reference, which asks all ninety one a day.

It is the demo's because the fixtures are: the eight authored scenarios are pinned to those
six questions and the engine refuses them on the ninety, since a scripted answer names a
chip belonging to a question the rotation is not asking that day. So every engine fixture
here exercises the six-day arithmetic. **A port that reproduces every fixture has proved it
computes the same thing, and has not proved anything about the rotation it will run.** A
fixture generated against the ninety is the work that closes that, and it is not here yet.

The team rotation is the ninety already: `rotation/team.json` orders all ninety team
questions and the engine asks them one a day whichever individual rotation is loaded.

- `calendar.json` — the named working weeks, the weekday numbering, the default start
  date and where the roll-up week ends. **Sunday is 0.** A port that numbers weekdays from
  Monday reads every schedule one day out and nothing will crash.
- `thresholds.json` — every number the layer computes against, in three blocks: `state`
  (what the resolver is injected with), `engine` (constants the engine carries), and
  `vocabulary` (the closed value sets). Element is deliberately **not** in the vocabulary:
  it is an open value (spec 7.1a) and a port that enumerates it will reject the team bank.

`schema/` carries one JSON Schema per data file. They are documentation with teeth: the
descriptions say which fields are load bearing and why, which is the part that does not
survive being inferred from the data.

### `fixtures/state/`

Each file has three parts: metadata, `inputs`, and `expected`.

`inputs` is every answer the team recorded, as `{ day, askedDayIndex, member, track,
questionId, level, chips, freeText }`. Replay them in day order. **Feed each day exactly
once**: both accumulators are recurrences, so a day fed twice is double-counted and a day
skipped is gone. That is why the reference catches up day by day rather than jumping.

`expected` is one entry per day the team was asked on, carrying:

- `askedDayIndex` and `classifierQuestionId` — the one crossing between the two day
  spaces, pinned. Get this wrong and you get a plausible horizontal answer rather than a
  crash, which is exactly how it went wrong in the reference.
- `atoms` — the day's atom vector per track: level, locus and cause, aggregated.
- `tracks` — both accumulators per track, with the relative limb's baseline (`m0`, `s0`,
  `age`, whether it is floored, whether it is frozen).
- `classifier` — the attribution and level sums, their variances, and `z`.
- `vertical`, `horizontal`, `headwindsFlag`, `locusFilter`, `limb`, `track`, `openedDay`.
- `concentration` — a word and a share. **Never a member.**
- `watch`, `watchExposed`.
- `logRecord` — exactly what a production store would be handed.

The 24 section K cases are synthetic: answers drawn from the model in
`tools/kit/model.mjs`, which ports `tools/vertical_gate.py`. **The drawn answers are in
the fixture.** A port replays them and never runs the model, because a fixture that shipped
a random seed would be asking two languages to agree about a generator.

A case fixture carries a `realised` block: the negative share it actually drew, over the
whole run and over the baseline window, against the share the model asked for. **A fixture
is one draw, not the study's mean.** Section J and K's tables are means over ten thousand
runs. One run wanders: `born40-6` drew a quiet first fifty-six days, froze its baseline
there, and its relative limb then climbs past its bound on a case section K describes as
invisible to D13. That is the reference's own output and a harder fixture for a port, so
it stands as drawn.

### `fixtures/engine/`

One file per scenario, with one entry per asked day carrying the three tallies and the
floor, the selection attempt, the route and the contributor share, the cycle as it stood,
the day's events, the succession record, any cross-team condition, and **every payload the
engine handed to Eran that day**. Then the resolved cycle history, the escalations, the
failed files, the installed mechanisms and the worksheet history.

The quiet days are in there on purpose. One message a day, every day, is a decision the
layer makes rather than a silence, and a port that emits nothing on a quiet day has changed
the product.

### `fixtures/engine/situations/`

One hand-built run per daily situation the eight scenarios do not reach, with the day
it is expected on named in the file.

There is one, **`between_cycles`**: the day a cycle resolved and nothing new was selected.
The scenarios reached it until the team question began voting. With both tracks feeding the
tally there is enough evidence that selection succeeds on the same day a cycle resolves, so
none of the eight ever leaves a day in between. The situation is still reachable and the
engine is unchanged: a team whose condition closes on a quiet day still gets it.

**It is in the kit because every other engine fixture is generated from those scenarios.**
A situation that leaves the demo leaves the conformance kit with it, and a port would
quietly stop replaying a case the production build has to get right. The reference covers
it in `test/between-cycles.test.mjs`, and that test cannot reach a port, so the fixture
exists to carry it across.

A situation fixture holds its own scenario block, script included, rather than taking its
answers from a state fixture the way the eight do. It is hand-built and small enough to
carry them, and a port replays it the same way: rebuild the team from `scenario`, step to
the horizon, and assert the situation on `expectedOn` along with `cycleHistory` and the
payload. `dotnet/Rrida.Deterministic.Tests/SituationFixtureTests.cs` is the worked example.

The bench is two files:

- `bench/routes.json` — all 3,240 chips in both banks
  through the route calculation, at **both** sides of the share threshold. Two shares
  rather than one because a chip that routes correctly at one says nothing about the other,
  and the share is the whole of spec 5.1.1.
- `bench/drives.json` — all 1,620 chips in the
  individual bank driven through a complete cycle on the engine's own day loop, with the
  signal held flat so the ladder climbs to rung two and then fails.
  1,526 of 1,620 pass in the reference.

**The team bank is absent from `drives.json`, and that is not an omission.** The bench
answers a chip's question on the day the rotation asks it, and its bench rotation
hard-codes the individual track, so a team question has no day to be asked on. Sweeping it
would report 1,620 chips that never produced an action
and call that a routing failure, which is not what it would mean. The limitation is the
reference's own and is stated in `src/chipsweep.js`. Every team chip is still in
`routes.json`.

---

## The order to port in

Each step has fixtures that pass before the next one needs to work.

| Step | What | Held by |
|---|---|---|
| 1 | The loader, with every validation the reference halts on | `data/` and its schemas |
| 2 | The calendar: working weeks, asked-day windows, the rotation crossing | `askedDayIndex` in every state fixture |
| 3 | The state layer: accumulators, classifier, concentration, resolver, log | `fixtures/state/` |
| 4 | Tally and routing | `fixtures/engine/bench/routes.json` |
| 5 | Cycles and succession | `fixtures/engine/bench/drives.json` |
| 6 | Cross-team and the readings | `fixtures/engine/scenarios/` |

Step 3 is the one to do carefully and step 2 is the one that will bite. Everything in the
layer counts the team's own asked days, never calendar days, and both are plain integers,
so handing one where the other is wanted is silent. It was silent five times in the
reference. `definitions/sections/day-space-sweep.md` lists every function that takes a day
and which space it is in.

---

## Regenerating this kit

```
node tools/kit/build-kit.mjs
```

Everything here is generated from the reference. Nothing in it is hand-maintained, and
nothing in `src/` or `data/` is written to. If a fixture and the reference disagree, the
reference is right and the kit is stale.

**When a definition or a threshold is ratified and changed:** edit the config or the data,
regenerate this kit, and re-run both test suites. The fixtures are the acceptance test for
a change on either side, which is the point of having them.
