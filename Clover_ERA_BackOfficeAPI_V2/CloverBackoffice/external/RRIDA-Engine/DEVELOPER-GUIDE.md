# Developer guide

For a .NET developer who has not read any of the handover. You do not need to, to get this
running and wired into Azure. Read it when you want to know why a number is what it is.

**Everything in here is provisional, and Clive is the authority on it.** That is not a
disclaimer, it is the operating state: every threshold is a decision about what the product
calls trouble, and it can change. Section 4 says what that means for you.

---

## 1. Day one

```
git clone https://github.com/clivehays/RRIDA-Engine.git
cd RRIDA-Engine/dotnet
dotnet build
dotnet test
```

Expected:

```
Build succeeded.
    0 Warning(s)
    0 Error(s)

Passed!  -  Failed: 0, Passed: 94, Skipped: 0, Total: 94, Duration: 5 s
```

**94 tests, about five seconds.** There is nothing to configure and no connection string.
The suite walks up from the test binary to find `/kit`, which is in the repository.

Those 94 tests are not 94 assertions. They replay:

- **3,600 days** of resolved state across 32 runs, every value to 1e-9
- **720 asked days** of full engine runs across the 8 scenarios
- **3,240 chips** through the route calculation, at both sides of the share threshold
- **1,620 chips** through a complete cycle on the engine's own day loop

If `dotnet build` fails on the target framework, you have an SDK newer than 8.0 and no 8.0
runtime. That is fine and expected: the libraries target `net8.0` and the SDK restores the
targeting pack. The test project rolls forward at runtime so the host can launch.

### Then run one scenario end to end

```
dotnet run --project samples/Rrida.Sample -- team-platform
```

```
RRIDA deterministic layer, sample host
==============================================================================
  scenario      team-platform
  working week  mon-fri  (Mon, Tue, Wed, Thu, Fri)
  enrolled      7
  tracks asked  individual, team
  delivering    individual   (team answers are recorded and cast no vote)
  floor         floor(0.33 x 7) = 2; max(2, 2) = 2

day   1 (asked   1)  STATE  Cruise     unlocated
day   9 (asked   7)  STATE  Drift      unlocated            opened by absolute
day   9 (asked   7)  DELIVER  C1 content/MA-E-004-24  4 of 7
                        Build the handoff definition with the team using the centralised …
                        confirm by day 12, read on day 19
day  19 (asked  15)  READ     C1 moved  -> closed
...
------------------------------------------------------------------------------
90 asked days over 124 dates. 8 deliveries: 5 closed, 2 failed, 0 unattributed, 0 unread, 1 running.
90 log records written, one per team per day.
```

Then the last log record, in full, as a production store would be handed it.

Three things to notice in that output, because they are the whole product in miniature.

**`day 9 (asked 7)`.** Two clocks. Day 9 is a date; asked day 7 is the seventh day this
team was actually asked anything. Every window in the layer counts the second.

**`opened by absolute`.** Which of the two limbs opened the gate. The absolute limb says
this team is running low; the relative limb says it has departed from its own recent
baseline. A team that was already negative on day one never departs from anything, which is
why there are two.

**`4 of 7`, then a route.** The contributor share picks which of three moves goes out. Four
of seven is above half, so the team has already said it and asking one person would read as
not having listened.

Other scenarios: `team-care`, `team-claims`, `team-field-sales`, `team-design`,
`team-data`, `team-support`, `team-engineering-leads`.

---

## 2. The contract, in one page

### In

Per team, per asked day:

| | |
|---|---|
| **Answers** | One per member per track. Each is a `member`, a `track` (`individual` or `team`), a `level` (`red`, `orange`, `yellow`, `green`), and up to three `chips`. A member may answer each track once a day. **A reason is compulsory**: zero chips is valid only with free text. |
| **The team's working week** | Weekdays the manager chose. `mon-fri`, `sun-thu` for the Gulf, `wed-sun` for a restaurant, or any list. Sunday is 0. |
| **Config** | `kit/data/thresholds.json`, injected. Nothing in the state layer carries a default and the resolver throws without it. |

### Out

| | |
|---|---|
| **Resolved state** | Two axes and a flag, never one label. Vertical: `cruise`, `drift`, `stall`. Horizontal: `unlocated`, `internal`, `external`. The Headwinds flag is raised whenever the horizontal is external, **regardless of the vertical** — a team can be Stall and flagged at once, and on a large team with external causes that is the usual condition. Plus the limb and track that opened the gate, and the concentration reading. |
| **The delivery payload** | What Eran is handed: the action text, the condition the manager confirms against, the move, the route, the share **in words rather than counts**, how the signal came through, and the days the confirmation and the read fall on. One a day, every day, including the quiet days. |
| **The daily log record** | One per team per day from day one. Flat keys, plain numbers, a schema version, a config id, and a natural key of `(teamId, day)`. |

### The one rule that must never be broken

> **The engine never sees the resolver, and the resolver never lets an identity out.**

Spec 7.1 draws the first half: the team state is a RRIDA-level computation, and the engine
emits the atom upward and consumes a resolved state as an input. `Rrida.Engine` holds no
reference to `Rrida.State`. Three tests assert it, because a comment claiming a boundary is
not a boundary. Swap the resolver for the real RRIDA service and nothing in the engine
changes.

D16 draws the second half: the concentration reading leaves as a word and a share, never a
member. Identities are used to compute the share and go no further. **On a team of three,
naming who carried the evidence names the person.** A test asserts that too.

### The daily message, and the five display rules

The engine emits one daily payload per team per day, on the quiet days too, for the whole
life of a cycle. Eran turns each one into six fields, always the same six, under a fixed
schema (`DAILY_SCHEMA` in `src/eran/prompts.js`):

| Field | What it is |
|---|---|
| `headline` | One short sentence naming where the team is. Under twelve words. |
| `body` | The team's own reading, in one or two short paragraphs, with one phrase wrapped in `**` for emphasis. |
| `outward` | The condition whose cause the team places outside the team, and the move to raise it. Its own block. |
| `one_thing` | The single thing to do this week. |
| `say_this` | The words the manager can use, as a quotation. |
| `what_tells_you` | The observable that tells the manager it landed. |

All six sit in `required` and `additionalProperties` is `false`, so the last four come back
as **empty strings** when there is no live action and no outward condition, rather than being
left out. That is deliberate and is not to be relaxed: a model allowed to drop a key hands
every client a shape it has to branch on. Empty string is the signal to collapse the section,
never to render a gap.

`outward` was added on 4 November and **was previously carried inside `body`**, joined on with
"separately". If you built against the five, this is the field to add.

A cycle runs for a fortnight or more, so the same move is in the payload on ten or more
consecutive days. Four rules stop that reading as ten moves. The first is in this pack. The
other three are display rules, and they are written down here because the only place they
existed before was as comments in the demo board, which section 6 tells you not to port.

1. **The payload withholds the case on a reminder day.** `Situation` goes to
   `action_reminder` from the second day of a cycle, and `triggering_condition`,
   `how_it_came_through`, `share_of_team`, `issue_title` and `worksheet_substitution` are
   removed from the payload. What is left is what a nudge is made of: how long it has waited,
   what the check-back will have to go on, and the move itself so it can be named rather than
   re-argued. The
   prompt asks for this as well, and asking was not enough. Measured over a run of reminders
   with the framing still in the payload, four days in five re-argued why the condition was
   chosen, in half the words and the same substance. **An instruction the model can break is
   weaker than a field it does not have.** `FeedItem.Reminder` carries the flag so a client
   never has to infer it.

2. **The message thread shows `headline`, `body` and `outward` only.** The other three are never printed
   in the thread. They used to be, and a move running a fortnight was printed ten times down
   a column the manager scrolls past to reach the box they type in. Eran's own words already
   say what the week is about, which is the thread's job. The instruction belongs to the card.

3. **The action card latches the wording from the first day of the cycle and never re-reads
   it.** Hold `one_thing`, `say_this` and `what_tells_you` from the day the move arrived, keyed
   to the cycle id, and show that text for as long as the cycle is live. Do not rebuild the
   card from today's response: Eran rephrases the move slightly every day, which is harmless
   if you ignore it and reads as the system changing its mind if you display it. Clear the
   latch when the cycle resolves, and empty the card rather than only hiding it, so last
   week's move cannot be read back out of the page.

4. **`say_this` and `what_tells_you` are collapsed by default.** Behind one disclosure,
   shut on load. On the glance a manager needs the one thing and the confirm button. The
   words matter in the room and the test matters afterwards, and neither matters at nine in
   the morning on day fifteen. Folded, not dropped.

5. **`outward` is its own block, under the body, and never inside it.** Different border,
   its own heading saying the cause sits outside the team, and no button. It cannot go in the
   action card, because nothing in it is selectable: the engine builds the outward tally and
   never picks from it, so there is nothing to confirm and nothing to close. Running it into
   the body is what it used to do, and a manager reading one paragraph that holds both their
   own signal and a cause they do not own will try to fix the second one.

   Two things on the same screen must then stop claiming there is nothing to do. The quiet
   note under the message and the weekly flag panel both said "nothing is being asked of you
   yet", and both were printed inches from a block asking the manager to raise something.
   They now speak for the selection and the check-back, which is all they ever knew about.

The reference implementation of rules 2 to 5 is `public/app.js`: lines 276 to 293 for the
thread, `outwardBlock` for rule 5, the latch for rule 3 and the card and fold for rule 4.

### No payload says "day 98"

The run-day index counts days since a team started. It is an internal coordinate, and it
reached the live environment: *"The next check-back lands on day 98."* There is no day 98 in a
manager's world.

**Every manager-facing surface says time as a count of the team's own working days.** It was
in thirteen places, and only two of them were fields; the rest were deterministic prose, so
no prompt rule would have caught it. `Team.WorkingDaysBack` and `Team.WorkingDaysAhead` are
the only two things that may phrase an interval, and `test/scenarios.test.mjs` asserts that
no `day \d+` survives in any payload.

**Changed on 16 November. These three payload fields are gone:**

| Was | Now | Why |
|---|---|---|
| `signal_read_day` (int) | gone entirely (see below) | an index, printed verbatim |
| `confirmation_due_day` (int) | `confirmation_due_in` | the same |
| `days_until_read` (int) | gone entirely (see below) | counted calendar days, not the team's |

The monthly's `actions_taken[].date` is now `.when`, for the same reason. `Cycle.SignalReadDay`
and the rest are untouched, so a UI that wants the number still has it; what changed is what
the model is handed.

### "Reading" meant four things, so the measurement is now the check-back

The word was doing four jobs at once, three of them manager-facing:

| Sense | Meaning | Status |
|---|---|---|
| The team's current state | "the reading on this team is Cruise" | keeps the word |
| The weekly message (D25) | the message on the team's last working day | keeps the word |
| **Whether the move worked** | `signalReadDay`, the before-and-after | **now "the check-back"** |
| The weekly prompt's three inputs | "you are given three readings off them" | now "three inputs" |

Senses 1 and 2 coexist without trouble, because the weekly message *is* a reading of the
team. Sense 3 was the collision, and it gets worse rather than better if the check-back ever
moves onto the weekly day: two different things, said the same way, in the same message, on
the same day.

**Changed on 16 November. Two more payload fields renamed:**

| Was | Now |
|---|---|
| `reading_lands_in` | gone entirely (see the next section) |
| `awaiting_a_reading` | `awaiting_a_check_back`, with no interval in it |

Four deterministic strings changed with them, in `simulation.js`, `chat.js` and their C#
ports: "the next check-back on it lands ...", "... check-back lands in 3 working days",
"ended without a check-back", and the weekly's awaiting clause.

**Internal identifiers were deliberately left alone.** `signalReadDay`, `signalRead`,
`awaitingRead`, `readTrack`, `readFileOnDay`, `nextReadDay`, `isReadingDay`,
`readingWeekday`, `MAX_READ_DEFERRALS` and `EARLIEST_READ_OFFSET` all keep their names, in
both languages. None of them reaches a manager, and renaming them would have moved the
conformance fixtures for nothing. If you are reading engine code, "read" still means the
check-back; it is only the manager-facing surface that distinguishes them.

`PREAMBLE` in `src/eran/prompts.js` now defines the two words against each other, because
the field rename alone does not stop a model writing "reading" out of its own vocabulary.

### Nothing says when the check-back lands

The check-back is keyed to the next return of a question that can report the condition
(D27, `rotation.nextReadDay`). The period that governs it is the rotation's length. On the
demo's six-question file that is days. On `data/rotation-90/rotation.json` it is **a median
of 17 working days, 48 at the ninetieth percentile and 93 at worst**, measured across all
85 individual-track issue files and every delivery day. The "day 98" and "82 days" reports
from the live environment are both in that distribution. They were not bugs in the
arithmetic. The arithmetic was right.

**So the interval is gone rather than reworded.** There is no `check_back_lands_in`, and
`awaiting_a_check_back` carries no number. What Eran is given instead is a phrase that is
true at any rotation length:

> The next check-back on it comes when a question on it comes round again.

Removed rather than instructed, for the same reason as rule 1 above: a number in the
payload is a number that reaches the message. `PREAMBLE` also forbids inventing one, and
forbids the substitutes a model reaches for when a number is missing ("soon", "shortly",
"in the next week or so"), because those are worse than silence. `test/scenarios.test.mjs`
asserts no payload carries the retired field or the phrase "check-back lands".

`Cycle.SignalReadDay` is untouched in both languages, so the board and any UI that wants
the day still has it. What changed is what the model is handed.

**This is a holding position, not the end state.** The honest reading of the measurement is
that a check-back a manager waits a working quarter for is not a check-back. Two ways out
were measured and both are written up in the engine log:

| Rule | Readable | Wait (working days) |
|---|---|---|
| Today | 100% | median 17, p90 48, max 93 |
| Read on the team's weekly day (D25) | **4.8%** | median 6 |
| Weekly day, that week, against the dimension | 99.9% | median 6, p90 10, max 18 |

The middle row is the trap, and it is the reason today's rule is still here. Reading on the
weekly day sounds like a free improvement, but `readFileOnDay` counts members who filed a
chip into the issue file *on that day*, and chips belong to questions. On a weekly day the
question asked is usually not one that can fill that file, so the file is unreadable 95.2%
of the time. D23's guard does not catch it: that guard tests `responses === 0`, and a weekly
day has responses. So `contributors` comes back 0 against a baseline of 3, `fellByOne` is
true, `moved` is true, and the cycle **closes telling the manager their conversation
worked**. Reproduced end to end in `tools/read-horizon/prove-hazard.mjs`.

The third row is the version that works, and it needs the dimension widening to have
anything to read. If you pick it up: the two changes are one change.

**Not a date, which was the first attempt and the suite rejected it twice.** Month
abbreviations collide with real names, and a team with someone called Jun or May either trips
the no-individual-names guard or forces it to be loosened, which is the worse trade. And a
cycle can resolve on a day the team was never asked, so a date can name a Saturday to a team
that does not work Saturdays, which is what D35 exists to prevent.

---

## 3. Azure wiring checklist

Six items. Each names the interface it implements.

| # | What | Interface | Blocks a first run? |
|---|---|---|---|
| 1 | **Answer ingestion** | Produce `Team.RecordDay(day)` input: `ScriptEntry(member, track, smiley, chips, text)` per member per track. Today the sample reads a fixture; in production this is a query. | **Yes.** Nothing runs without answers. |
| 2 | **Per-team working week** | `TeamConfig.WorkingWeek` — a name from `kit/data/calendar.json` or a list of weekdays. | **Yes**, but default `mon-fri` and move on. |
| 3 | **Injected config** | `EngineConfig.Load(path)` and `StateConfig.Load(path)`, both from `kit/data/thresholds.json`. Ship the file; do not retype the numbers. | **Yes.** Both throw without it. |
| 4 | **Log record storage** | `IStateSink.Write(teamId, record)`. **Upsert on `(TeamId, Day)`** — the record exposes `Key` for exactly this. Carry `Schema` and `ConfigId` on the row. | No. `MemorySink` works until you need history. |
| 5 | **The daily job** | The loop in `samples/Rrida.Sample/Program.cs`. Per team per day: `RecordDay`, `resolver.Resolve`, `ApplyState`, then `AttemptSelection` if nothing is running. | **Yes.** |
| 6 | **Where the Eran payload goes** | `Team.Feed` is appended to as the day runs. Each `FeedItem` carries `Kind`, `Situation` and a `Payload` dictionary. Drain it per day. | No. The layer is correct without Eran; the manager just gets no prose. |

Four notes that will save you a day each.

**Feed each day exactly once, in order.** Both accumulators are recurrences. A day fed
twice is double-counted; a day skipped is gone. `Resolver.Resolve` catches up from the last
day it saw rather than jumping, which is why it takes the day rather than a range.

**Upsert, do not insert.** Replaying a run has to be idempotent. That is what makes the
record safe to hand a store, and what lets you re-run a day after a deploy.

**The resolver holds per-team running state in memory.** One instance per run. If your job
is stateless between days you will need to rehydrate it by replaying, which works and is
slow, or persist the accumulator state, which is not in this pack.

**Store the config id with the record.** A state figure without its thresholds is not
interpretable, and the thresholds are provisional. `Resolver` takes the id from you rather
than computing it, because hashing a config object means agreeing about key order and float
printing across two languages, which is not a thing worth agreeing about.

---

## 4. What not to change

**Every threshold. Every definition. Every data file.**

Concretely: anything in `kit/data/thresholds.json`, anything in `kit/data/` at all, and
every rule the definitions in `kit/definitions/` describe. They are not tuning knobs. `h` is
31.0 and `g` is 17.5 because sections J and K measured them against a false-opening budget
on two null teams at five team sizes. The D16 concentration cut is a lookup rather than a
constant because one cut for every size reaches 8.0 per cent worst-case error against 4.1
for the lookup, and fails in different directions at each end.

Changing a number here changes what the product calls trouble.

### When a definition is settled

```
1. Edit kit/data/thresholds.json, or the data file concerned.
2. From the repository root:  node tools/kit/build-kit.mjs
3. cd dotnet && dotnet test
4. Commit the config change and the regenerated fixtures together.
```

Step 2 regenerates every fixture from the JavaScript reference, which stays the source of
truth. Step 3 will fail loudly and everywhere, because a threshold reaches every day of
every run — that is the point. Read the diff on the fixtures: it is the change, stated in
numbers.

**Never edit a fixture by hand.** If a fixture and the reference disagree, the reference is
right and the kit is stale.

---

## 5. Regression

**The fixtures are the acceptance test for any change on either side.** Change the C# and
`dotnet test` tells you. Change the JavaScript and regenerating the kit tells you. Change a
threshold and both tell you.

### Adding a scenario

1. Author it as a scenario in the reference (`data/scenarios/`) and add it to that
   folder's `index.json`.
2. `node tools/kit/build-kit.mjs`. It appears in both fixture folders and in both index
   files.
3. `dotnet test`. The state suite picks it up from the index with no C# change at all; the
   engine suite picks it up the same way.

If you want a case rather than a scenario — a synthetic team at a given negative share —
add it to `CASES` in `tools/kit/model.mjs` and it is generated at all three sizes.

**A fixture is one draw, not a mean.** The section K cases are drawn from a seeded model,
and section J and K's published tables are means over ten thousand runs. One run wanders:
`born40-6` drew a quiet first fifty-six days, froze its baseline there, and its relative
limb then climbs past its bound on a case section K describes as invisible to D13. That is
the reference's own output, and a harder fixture for a port, so it stands as drawn. Each
case file carries a `realised` block saying what it actually drew.

---

## 6. What is not in this pack

| | |
|---|---|
| **Eran** | The language model that turns a payload into prose for the manager. The payload is the boundary: `FeedItem.Payload`. The schema it answers under, and the five display rules, are in section 2. |
| **The manager surface** | The board, the machinery panel, the cards. The payload is the boundary to that too. Three of section 2's four daily-message rules are display rules and belong to whatever is built here. |
| **Answer capture** | The check-in itself: the four faces, the chip sets, the free-text box. `ScriptEntry` is the boundary. |
| **The production database** | `IStateSink` is the boundary. |

Four boundaries, and three of them are the same shape: a plain object with flat keys and no
class instances, so nothing downstream needs this codebase to deserialise it.

---

## 7. Open items that affect you

Seven, with links. None is a defect in this pack; each is a decision that has not been made.

### Decisions not yet made

**All of D1 to D30.** → [`kit/definitions/D1-D30-provisional-definitions.md`](kit/definitions/D1-D30-provisional-definitions.md)

Every one is provisional. D1 to D22 were adopted by Clive as build lead so that measurement
could proceed; D23 and D24 were written while building and are marked as such. Two of them
are readings of a specification that does not settle the question, and those are written up
separately → [`kit/definitions/spec-findings.md`](kit/definitions/spec-findings.md):

- §4.2 contradicts itself on the severity weights. Orange is 0.5 in the table and "above
  0.5" in the prose immediately below it. The layer follows the table, because §6.4 does
  too. It bites in exactly one place, the tally tie-break.
- §4.4 says Yellow is not neutral and gives it a polarity lean that can point negative. D1
  assigns Yellow to the positive side unconditionally. The accumulators score it 2/3 on a
  linear scale, so it already pulls a team down relative to Green.

### The 72-hour confirmation

→ [`kit/definitions/sections/post-launch.md`](kit/definitions/sections/post-launch.md), item 2

`ConfirmationLag` is 3, counted in the team's own days. A confirmation delivered on a
Thursday therefore falls due on a Tuesday: five calendar days, not 72 hours. It has one
clear virtue, which is that a confirmation never falls due on a day the manager is not at
work. Three calendar days would keep the spec's words and put confirmations due on Sundays.

**For Clive:** does 72 hours mean three days of elapsed time, or three days of the manager's
attention? The layer can do either; it is one value in `thresholds.json`.

### The empty yellow tally

→ [`kit/definitions/sections/I-vertical-gate.md`](kit/definitions/sections/I-vertical-gate.md)

Stall gates the Yellow tally. Yellow answers are the indifferent set, and a team in Stall
that has been answering Yellow without picking chips produces a yellow tally with nothing in
it that clears the floor. The layer says so — that is what `NoFloorPosture["Stall"]` is —
but a manager in Stall being told there is nothing to act on is the least satisfying
message the product sends. Not a bug; a gap in the library.

### The neutral-minority route

→ [`kit/definitions/sections/E-neutral-negatives.md`](kit/definitions/sections/E-neutral-negatives.md)

Spec 4.6 routes to content when the contributing chips are **majority** neutral, and the
majority is strict: an even split is not a concentration. What a substantial minority of
neutral should do is not settled. Today it does nothing, and the share decides the route as
usual. Nine questions have four or more of their six negative chips tagged neutral, so this
is reachable rather than theoretical.

### Team delivery is on, and was switched off until 17 October

→ `EngineContext.DeliveryTracks`, and the comment above it

Both tracks are asked every day, both produce atoms, and **both now cast a vote**. This
section said the opposite until D32 and the production developer reported it as out of date,
which it was.

The two things it waited on are settled. D31 gave the team bank's 163 external-locus negative
chips somewhere to go that nothing selects from, which was the dangerous one: every one of
them routed to a manager-behaviour action, so switching the track on without it would have
handed a manager an exercise about their own behaviour because their team said a vacancy was
never filled. And spec 7.1b is decided: the tallies are shared, and a person who says a thing
about themselves and about the team has said two things, so the signal amplifies rather than
being deduplicated.

One condition still gets one move. Sharing the buckets is what guarantees it rather than a
precedence rule, and the difference between how people rate themselves and how they rate the
team is reported in the weekly message as `how_the_questions_compare`, never as a second move.

**If you are reading this to know what to implement:** `Implemented` and the `?? [...]`
default in `EngineConfig` are separate, and both are now both tracks. The allow-list says what
may be asked for; the default says what is asked for when the caller says nothing. Moving only
the first runs one track against two-track fixtures, which is how it was found.

### Which rotation to run, which is not what this guide used to imply

→ `ROTATIONS` in `src/context.js`

**Production asks all ninety individual questions and all ninety team questions, one of each a
day, looping.** The individual bank is ids 121 to 210 and the team bank 301 to 390.

There is no six-question bank. `data/rotation-demo-6day.json` is a schedule naming six of the
ninety individual questions and looping them every six days, and **it reaches 26 of the 85
issue buckets**. The other 59 are unreachable on it, because the questions that would raise
them are never asked, so no amount of running time surfaces them. It exists because on the
real rotation a cycle cannot be delivered, confirmed, read and closed inside one sitting, and
a demo has to show that.

`loadRotation` has no default and halts without a rotation path. It used to fall back to a
file called `data/rotation.json` which held the six, so a build that said nothing inherited
a third of the instrument, and one did. A document of ours describing the ninety as "used for
measurement", under a heading reading "What is not your problem", is what confirmed it.

**The conformance fixtures are built on the six-day rotation**, because the eight authored
scenarios are pinned to its questions and the engine refuses them on the ninety. So
reproducing every fixture proves a port computes the same thing as the reference and proves
nothing about the rotation it will run. A fixture generated against the ninety is outstanding
work and is not in the kit yet.
### The loader half-state, and what is still not named about it

→ [`kit/definitions/sections/post-launch.md`](kit/definitions/sections/post-launch.md), item 3

**This is fixed, and it is on the list because one part of it is not explained.**

What looked for months like a flaky test was a defect in the rotation loader that could
drop a track: a rotation present but unable to fill it walked past the guard, and the
accessor re-derived on every call a question the loader had already settled at load. Nothing
tied `tracksAsked` to what `questionIdForDay` actually answered, so a null arriving later
went out as an answer, and the horizontal axis came back `unlocated` where it should have
said `external`. All of that is closed in both implementations, with tests that fail against
the loader as it stood.

**What is not named is why the re-derivation misbehaved.** Instrumenting it reproduced the
reading the original hunt could not believe — the guard branch taken while the two values it
guards on both read correctly. The fix removes the dependency rather than explaining the
perturbation. Nothing in either implementation relies on it now, and a track that reports
itself as asked can no longer answer null at all: it throws, naming the track, the day and
the state. If you ever see that message, it is telling you something real and it is telling
you where.

### One documentation slip, recorded so it is not read as a rule

`src/engine/calendar.js` says ninety asked days need "a hundred and twenty-six" calendar
days on a five-day week. The code gives 124, every scenario horizon is 124, and
`post-launch.md` says 124. The port is pinned to the behaviour and a test says so.

---

## Where to look when something disagrees

| Symptom | Look at |
|---|---|
| A state value is out from a certain day onwards | The baseline froze, or a day was fed twice. `RelativeReading.BaselineFrozen`, and whether your loop skips days. |
| A horizontal axis reads `unlocated` where it should say `external` | The classifier was given the wrong question. It is keyed on the question, and the question comes from the team's **asked-day index**, not the date. This is the defect that reached a manager as a missing flag. |
| A window holds the wrong number of answers | A date was counted where an asked day was wanted. `Calendar.RotationDay` is the only crossing. |
| A different action was delivered on a tie | Severity weight, then question spread, then first to cross the floor, then action id. See `Tally.CompareCandidates`. |
| A signal read closed a cycle on a day nobody answered | It should not: D23 defers twice and then resolves as `unread`. Check `MaxReadDeferrals`. |
| Cycle ids do not match the fixture | The run numbers them. One `RunScope` per run; a team built alone gets its own. |
