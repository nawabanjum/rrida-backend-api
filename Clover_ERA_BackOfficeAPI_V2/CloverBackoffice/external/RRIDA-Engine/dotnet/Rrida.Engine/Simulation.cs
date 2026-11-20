namespace Rrida.Engine;

/// <summary>
/// The simulation: a global clock, the day loop, and the events a presenter steps between.
/// Build spec section 8.
/// </summary>
/// <remarks>
/// The clock is global across all teams so the roll-up stays coherent. Every team ticks on
/// every day whether or not anyone is watching it.
/// </remarks>
public sealed partial class Simulation
{
    /// <summary>Rung two actions are structural and carry no authored note type.</summary>
    private const string RungTwoDefaultNoteType = "Change";

    /// <summary>
    /// The case for the move, withheld from the daily payload on a reminder day. See the
    /// comment at the point of removal in <see cref="Daily"/> for why this is a missing
    /// field rather than an instruction.
    /// </summary>
    private static readonly string[] ReminderWithheld =
    {
        "triggering_condition", "how_it_came_through", "share_of_team", "issue_title",
        "worksheet_substitution",
    };

    /// <summary>
    /// The counters this run numbers its cycles and messages with. Per run, not per process.
    /// </summary>
    public RunScope Scope { get; } = new();

    public EngineContext Ctx { get; }
    public int Day { get; private set; }
    public string StartDate { get; }
    public int Horizon { get; }

    /// <summary>
    /// D26. Where a portfolio's week ends, for the roll-up only. Configured rather than
    /// fixed: a senior scenario may set its own, the run may set a default for all of them,
    /// and the calendar's value applies when neither says anything.
    /// </summary>
    public int RollUpWeekEnds { get; }

    public List<SimEvent> Events { get; } = [];
    private readonly Dictionary<string, Team> _teams = new(StringComparer.Ordinal);
    private readonly List<string> _order = [];

    /// <summary>
    /// Where the resolved state comes from (spec 7.1). Injected rather than imported.
    /// </summary>
    /// <remarks>
    /// The boundary is the single most important one in the spec, and a comment saying so is
    /// not a boundary. <b>There is no default:</b> the engine does not reach for a state
    /// computation even to fall back on one.
    /// </remarks>
    public IStateSource StateSource { get; }

    public Simulation(
        EngineContext ctx,
        IEnumerable<TeamConfig> scenarios,
        IStateSource stateSource,
        int? horizon = null,
        string? startDate = null,
        int? rollUpWeekEnds = null)
    {
        Ctx = ctx;
        StateSource = stateSource ?? throw new ArgumentNullException(
            nameof(stateSource),
            "Simulation needs a stateSource. The team state is a RRIDA-level computation and this "
            + "engine does not perform it (spec 7.1): it emits the atom upward and takes the "
            + "resolved state as an input.");
        StartDate = startDate ?? ctx.Calendar.DefaultStartDate;
        Horizon = horizon ?? 90;
        RollUpWeekEnds = rollUpWeekEnds ?? ctx.Calendar.DefaultRollUpWeekEnds;

        foreach (var config in scenarios)
        {
            var team = new Team(ctx, config, Scope);
            _teams[team.Id] = team;
            _order.Add(team.Id);
        }
        ValidateChildLinks();
    }

    private void ValidateChildLinks()
    {
        foreach (var team in AllTeams())
        {
            foreach (var childId in team.ChildTeamIds)
            {
                if (!_teams.ContainsKey(childId))
                {
                    throw new InvalidOperationException(
                        $"{team.Id} lists child team {childId}, which does not exist");
                }
            }
        }
    }

    public Team? TeamOf(string id) => _teams.GetValueOrDefault(id);
    public List<Team> AllTeams() => _order.Select(id => _teams[id]).ToList();

    public bool IsMonthEnd(int day) => Calendar.IsMonthEnd(StartDate, day);
    public string MonthLabel(int day) => Calendar.MonthLabel(StartDate, day);

    public sealed record StepResult(int Day, List<SimEvent> Events, bool AtHorizon);

    // -------------------------------------------------------------- the day loop

    public StepResult Step()
    {
        if (Day >= Horizon) return new StepResult(Day, [], true);
        Day += 1;
        var day = Day;
        var events = new List<SimEvent>();

        foreach (var team in AllTeams()) team.RecordDay(day);
        foreach (var team in AllTeams()) TickTeam(team, day, events);

        if (IsMonthEnd(day))
        {
            foreach (var team in AllTeams()) events.Add(MonthlySummary(team, day));
        }

        foreach (var team in AllTeams())
        {
            if (team.ChildTeamIds.Count == 0) continue;
            // An open condition runs its own clock first, so a condition that resolves today
            // frees the slot before a new pattern is looked for. One at a time, like a team.
            TickCrossTeam(team, day, events);
            var crossTeam = CheckCrossTeam(team, day);
            if (crossTeam is not null) events.Add(crossTeam);
        }

        Events.AddRange(events);
        return new StepResult(day, events, Day >= Horizon);
    }

    private void TickTeam(Team team, int day, List<SimEvent> events)
    {
        // 0. The manager's own taps, replayed. Port of the reference's step 0
        //    (simulation.js applyManualConfirmations): a host that keeps the taps as facts
        //    rebuilds the run from them, and a real tap always beats the scripted manager.
        ApplyManagerTaps(team, day, events);

        // 1. Auto-confirmation from the script.
        ApplyScriptedConfirmation(team, day, events);

        // 2. A due signal read resolves the cycle before anything else can be selected.
        //    D29. Any cycle whose reading falls due, not only the one in the slot. A cycle
        //    the manager confirmed weeks ago is still owed its measurement and its outcome
        //    message.
        foreach (var due in team.DueReads(day))
        {
            ReadSignal(team, day, events, due);
        }

        // 3. The state, resolved outside the engine and handed in (spec 7.1). The simulation
        //    owns the wiring because it owns the clock; the engine owns neither.
        //
        //    D20. The state is resolved and applied every day; the recalculation predicate
        //    governs the weekly manager-facing message and nothing else. They were one
        //    predicate, which is why a gate opening could sit unapplied for up to six days.
        var record = team.ApplyState(StateSource.Resolve(Ctx, team, day));
        if (record is not null && team.IsReadingDay(day)) events.Add(WeeklyEvent(team, day, record));

        // 3a. D28. Record what cleared the floor today, acted on or not. After the state,
        //     because the state decides which tally is live; before selection, because what
        //     is recorded has to be what was there when the choice was made rather than what
        //     was left over after it.
        team.UpdateWatch(day);

        // 4. Selection, if the team has no active cycle and the state delivers at all.
        //
        //    Calibration is a condition of delivery, not a side effect of one. Before D20 the
        //    state was only applied on a recalculation day, so the state was null for the
        //    first six days and this gate could not open. D20 made the state resolve daily,
        //    which is right, and in doing so opened delivery from day one: three scenarios
        //    put an action in front of the manager on day 1 while the board still read
        //    "calibrating". Spec 7.3 is the rule and it is stated here rather than inherited
        //    from the shape of something else.
        //    D29. A rung two that came due while the manager was busy goes first: it is
        //    owed to a condition that did not shift, and a fresh selection taking the slot
        //    ahead of it would turn the ladder into a single rung without saying so.
        var canDeliver = team.SlotIsFree() && team.State is not null && !team.IsCalibrating(day);
        Cycle? queued = null;
        if (canDeliver && team.PendingRungTwo.Count > 0)
        {
            queued = team.PendingRungTwo[0];
            team.PendingRungTwo.RemoveAt(0);
            team.AdvanceToRungTwo(day, queued);
            events.Add(ActionActiveEvent(team, day, queued, rungTwo: true));
        }

        if (queued is null && canDeliver)
        {
            var attempt = team.AttemptSelection(day);
            if (attempt.Cleared)
            {
                var escalation = team.CheckEscalation(day, team.ActiveCycle!.IssueFile);
                events.Add(ActionActiveEvent(team, day, team.ActiveCycle));
                if (escalation is not null) events.Add(EscalationEvent(team, day, escalation));
            }
            else if (team.IsReadingDay(day))
            {
                // Nothing cleared the floor on a reading day is itself something the manager
                // is told (spec 7.4, last row). Recorded on the weekly message.
                var weekly = events.FirstOrDefault(e => e.Type == "weekly" && e.TeamId == team.Id);
                if (weekly is not null)
                {
                    weekly.FloorCleared = false;
                    weekly.Posture = attempt.Posture ?? EngineState.NoFloorPosture.GetValueOrDefault(team.State ?? "");
                }
            }
        }

        // 5. Confirmation falling due.
        var cycle = team.ActiveCycle;
        if (cycle is not null && !cycle.Confirmed && cycle.ConfirmationDueDay == day)
        {
            events.Add(new SimEvent
            {
                Type = "confirmation_due", Day = day, TeamId = team.Id, CycleId = cycle.Id,
                Label = $"{team.Name}: 72-hour confirmation due",
            });
        }

        // 6. The daily message. One per day the team was asked something, whatever happened
        //    above. A manager who opens the board on a working day and finds nothing has
        //    learned the board is not worth opening, so a quiet day gets a message that says
        //    so rather than silence.
        //
        //    D35. Not on a day the team was not asked. The rule read "every day, whatever
        //    happened above" and that was wrong on a non-working day, because nothing
        //    happened: no answers, no state, nothing read. What came out was a nudge about a
        //    move on a Saturday. Reported by the production developer, who had already hidden
        //    them. Same class as the day-space crossings and as D25.
        if (team.IsAskedOn(day)) DailyMessage(team, day);
    }

    private void ApplyScriptedConfirmation(Team team, int day, List<SimEvent> events)
    {
        var cycle = team.ActiveCycle;
        if (cycle is null || cycle.Confirmed || !team.AutoConfirm) return;
        // A per-cycle entry wins; otherwise the team's standing behaviour applies. Without a
        // standing behaviour a 90-day run fills up with cycles the manager never confirmed,
        // which is a scripting artefact rather than anything the engine is doing.
        var scripted = team.ScriptedConfirmationFor(cycle.Index, cycle.Rung);
        if (scripted is not { } behaviour) return;
        // D24 amended: an offset is counted in the team's own days, like every other interval
        // a cycle is timed in. Counted in dates it would drift against the confirmation it is
        // meant to answer, and a manager who confirms on schedule would look like one who
        // never confirmed at all.
        var dueOn = team.AskedDaysAfter(
            cycle.DeliveredDay, behaviour.DayOffset ?? Ctx.Config.ConfirmationLag + 2);
        if (day < dueOn) return;
        team.Confirm(day, behaviour.WorkedIfMet, "scripted manager");
        events.Add(new SimEvent
        {
            Type = "confirmed", Day = day, TeamId = team.Id, CycleId = cycle.Id,
            Source = "scripted manager", WorkedIfMet = cycle.WorkedIfMet,
            Label = $"{team.Name}: manager confirmed rung {cycle.Rung}",
        });
    }

    // ------------------------------------------------------------ signal and note

    private void ReadSignal(Team team, int day, List<SimEvent> events, Cycle? target = null)
    {
        var resolution = team.ResolveCycle(day, target);
        if (resolution is null) return;
        var cycle = resolution.Cycle;

        events.Add(new SimEvent
        {
            Type = "signal_read", Day = day, TeamId = team.Id, CycleId = cycle.Id,
            Moved = resolution.Read.Moved, Outcome = resolution.Outcome,
            Label = $"{team.Name}: signal read on \"{cycle.TriggeringQuestionText}\"",
        });

        OutcomeMessage(team, day, cycle, resolution);
        TeamNote(team, day, cycle, resolution);

        if (resolution.Outcome == "rung_two")
        {
            // D29. Rung two is a move, so it needs the manager. If they are mid-move it
            // waits rather than interrupting, and goes in front of a fresh selection when
            // they are free: a condition that did not shift is owed its second move, and
            // dropping it here would quietly turn a ladder into a single rung.
            if (team.SlotIsFree())
            {
                team.AdvanceToRungTwo(day, cycle);
                events.Add(ActionActiveEvent(team, day, cycle, rungTwo: true));
            }
            else
            {
                team.PendingRungTwo.Add(cycle);
            }
        }
        else
        {
            team.CloseOut(day, resolution.Outcome, cycle);
        }
    }

    /// <summary>
    /// Rung one's note type is authored in the library. Rung two is structural and carries
    /// none, so a mechanism change defaults to Change: a mechanism the team can see is by
    /// definition something different afterwards.
    /// </summary>
    public (string NoteType, bool Derived, string? Reason) NoteTypeFor(Cycle cycle)
    {
        var action = Ctx.Data.Action(cycle.ActionId)!;
        if (!string.IsNullOrEmpty(action.NoteType)) return (action.NoteType!, false, null);
        return (RungTwoDefaultNoteType, true,
            "rung two is structural and carries no authored note type; a mechanism the team can "
            + "see defaults to Change");
    }

    // ------------------------------------------------------------- Eran messages

    private FeedItem PushFeed(Team team, FeedItem item)
    {
        team.Feed.Add(item);
        return item;
    }

    /// <summary>The daily message. One per asked day, for every team. See D35 at the call site.</summary>
    public FeedItem DailyMessage(Team team, int day)
    {
        var cycle = team.ActiveCycle;
        var enrolled = team.EnrolledCountOn(Math.Max(1, day));
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["team_state"] = team.State ?? "calibrating",
        };
        string situation;

        if (cycle is not null)
        {
            var action = Ctx.Data.Action(cycle.ActionId)!;
            var chip = Ctx.Data.Chip(cycle.TriggeringChipId);
            var daysSince = day - cycle.DeliveredDay + 1;
            situation = cycle.Confirmed ? "action_confirmed"
                : daysSince <= 1 ? "action_first_day" : "action_reminder";
            payload["action_text"] = action.Text;
            payload["action_mode"] = action.Mode ?? (cycle.Rung == 2 ? "Team" : "One-to-one");
            // Named for what it is to the manager. A field called worked_if invites Eran to
            // say "the worked-if condition", which means nothing to the person reading it.
            // Read through the accessor: after a prerequisite substitution the action's own
            // condition is about an exercise the team did not run (spec 5.1.4).
            payload["how_you_will_know"] = Route.DeliveredWorkedIf(Ctx, cycle);
            payload["issue_title"] = action.Issue;
            payload["move"] = cycle.Rung == 2
                ? "the second move on this theme, a change to how something works"
                : "the first move on this theme, a conversation";
            // A Positive chip does not route. The engine records that as "none", which is
            // accurate but says nothing to a model, so it is named for the move it is.
            payload["route"] = cycle.Rung == 2 ? "structural"
                : cycle.Routing.Route == "none" ? "build" : cycle.Routing.Route;
            // Deliberately words, not a fraction. Handing Eran "3 of 9" and asking it not to
            // repeat the number is a rule it can break; not giving it the number is not.
            payload["share_of_team"] = cycle.Rung == 2
                ? null : Route.ShareInWords(cycle.Routing.Share, cycle.Routing.Threshold);
            payload["triggering_condition"] = ThemeLevelCondition(cycle, chip);
            payload["how_it_came_through"] = Tally.SignalStrength(
                cycle.TallyContributors.Count, team.Windowed(Math.Max(1, day)).Floor.Required);
            payload["days_since_delivered"] = daysSince;
            payload["confirmation_due_in"] =
                Team.WorkingDaysAhead(team, day, cycle.ConfirmationDueDay);
            // No interval for the check-back, decided 16 November. It is keyed to the next
            // return of a question that can report the condition, which on the
            // ninety-question rotation is a median of 17 working days and 93 at worst.
            // "The check-back lands in 48 working days" is accurate and useless, and it is
            // what reached the live environment as "day 98".
            //
            // The field is removed rather than the model instructed not to use it, which is
            // guide rule 1: a number in the payload is a number that reaches the message.
            // Eran is given the phrase instead, which is true at any rotation length.

            // Only the content route hands over a worksheet, and only its name. Eran has not
            // been given what is inside it and must not describe it.
            if (cycle.Rung == 1 && cycle.Routing.Route == "content")
            {
                var names = cycle.Routing.WorksheetsDelivered
                    .Select(w => string.IsNullOrEmpty(w.Title) ? w.Code : $"{w.Code} {w.Title}")
                    .ToList();
                payload["worksheet"] = names.Count > 0 ? string.Join(", ", names) : null;

                // Where a prerequisite substitution happened (spec 5.1.3), the action text
                // names the worksheet the crosswalk wanted rather than the one being handed
                // over, and Eran would otherwise have to resolve a contradiction nobody told
                // it about. The conflict is stated, and which side wins is stated with it.
                if (cycle.Routing.WorksheetChoice?.Substitution is { } sub)
                {
                    payload["worksheet_substitution"] =
                        $"The action text names {sub.WantedTitle}. That is not what is being handed "
                        + $"over. {sub.WantedTitle} assumes {string.Join(" and ", sub.Unmet)} has been "
                        + "run and closed first and this team has not done that, so the worksheet "
                        + "above is delivered instead. The condition has not changed; the starting "
                        + "point has.";
                }
            }
            if (cycle.Confirmed) payload["confirmed_on_day"] = cycle.ConfirmedDay;
            if (cycle.RungOneRecord is { SignalRead: { } firstRead })
            {
                payload["first_move_outcome"] = firstRead.Moved
                    ? "the conversation shifted the reports"
                    : "the conversation did not shift the reports";
            }

            // A reminder is not given the case for the move.
            //
            // The task tells Eran not to re-describe the condition on a reminder day, and
            // with the framing still in the payload that held on one day in five: measured
            // over a run of reminders, four of five re-argued why the condition was chosen,
            // in half the words but the same substance. The same lesson as the headcount,
            // which is not handed over either: an instruction the model can break is weaker
            // than a field it does not have.
            //
            // What is left is what a nudge is made of, how long it has waited and when the
            // reading lands, plus the move itself so it can be named rather than
            // re-explained. The route stays, because a reminder that calls a team response a
            // one-to-one has mis-framed it, and the worksheet stays, because naming the
            // session is how the nudge points at anything. What goes is the argument for why
            // it was chosen, which has been read already on every day since.
            if (situation == "action_reminder")
            {
                foreach (var field in ReminderWithheld) payload.Remove(field);
            }
        }
        else if (team.IsCalibrating(day))
        {
            situation = "calibrating";
            payload["days_answered"] = day;
            payload["days_until_first_state"] = Math.Max(0, Ctx.Config.CalibrationDays + 1 - day);
            payload["enrolled"] = enrolled;
        }
        else
        {
            var last = team.CycleHistory.Count > 0 ? team.CycleHistory[^1] : null;
            var justResolved = last is not null && last.ResolvedDay == day;
            situation = justResolved ? "between_cycles" : "nothing_cleared";
            payload["posture"] = EngineState.NoFloorPosture.GetValueOrDefault(team.State ?? "");
            payload["nearest_condition"] = NearestCondition(team, day);
            // D29. Nothing is being asked of them and something is still being measured.
            // Both are true at once now, and a quiet day that does not say so reads as the
            // team having nothing open at all.
            var measuring = team.AwaitingRead.Where(c => c.SignalRead is null).ToList();
            if (measuring.Count > 0)
            {
                payload["awaiting_a_check_back"] = string.Join("; ", measuring.Select(
                    c => $"{c.Issue} is still being measured"));
            }
            if (last is not null)
            {
                var how = last.Outcome switch
                {
                    "closed" => "closed, and the reports fell away",
                    "failed" => "ran out of moves without shifting",
                    "unread" => "ended without a check-back, because the question went unanswered when it came round",
                    _ => "ended without being confirmed, so nothing could be credited to it",
                };
                payload["what_happened_last"] =
                    $"{last.Issue} {how}, {Team.WorkingDaysBack(team, day, last.ResolvedDay)}";
            }
        }

        payload["situation"] = situation;

        // D31. What the team is naming that is not theirs to fix, if anything is above the
        // floor. Additive, on any kind of day, and never a situation of its own: it does not
        // replace what the day is about, it is another thing that is true. On a day where
        // nothing cleared the floor it will usually be the most useful thing on the message,
        // and on a day with a move running it sits behind the move, which is the right order.
        var outwardToday = team.OutwardList(day, limit: 1);
        if (outwardToday.Count > 0)
        {
            var o = outwardToday[0];
            payload["outward_condition"] = o.Issue;
            payload["outward_strength"] = o.StrengthInWords;
            payload["outward_days_above"] = o.DaysAbove;
            payload["outward_move"] = o.Move;
            payload["outward_goes_to"] = o.WhoItGoesTo;
            payload["outward_how_you_will_know"] = o.HowYouWillKnow;
        }

        return PushFeed(team, new FeedItem
        {
            Id = Scope.NextFeedId(), TeamId = team.Id, Day = day, Kind = "daily",
            CycleId = cycle?.Id, Rung = cycle?.Rung, Situation = situation,
            Reminder = situation == "action_reminder", Payload = payload,
        });
    }

    /// <summary>
    /// What is closest to clearing the floor, at theme level. Given to Eran on a quiet day so
    /// the message can say what would have to change, without naming an action that has not
    /// been selected and without quoting a chip.
    /// </summary>
    public string? NearestCondition(Team team, int day)
    {
        // Titles and counts only. Nothing here may carry a file or action code: whatever Eran
        // is handed in system vocabulary tends to come back out in system vocabulary.
        var tallyName = team.LiveTally();
        if (tallyName is null) return null;
        var windowed = team.Windowed(Math.Max(1, day));
        TallyEntry? best = null;
        foreach (var entry in windowed.Of(tallyName))
        {
            if (best is null || entry.Contributors.Count > best.Contributors.Count) best = entry;
        }
        if (best is null) return "nobody has reported anything that points the same way this week";
        var action = Ctx.Data.Action(best.ActionId)!;
        var need = windowed.Floor.Required;
        var got = best.Contributors.Count;
        var cleared = got >= need;

        // An action whose own cycle resolved inside this window is held back from selection,
        // so saying it has enough contributors without saying that would read as the layer
        // ignoring a signal it can see.
        var spent = team.CycleHistory.Any(c =>
            c.ResolvedDay is { } r && r >= windowed.From
            && (c.Rung1ActionId == best.ActionId || c.Rung2ActionId == best.ActionId));
        var issue = (action.Issue ?? "").ToLowerInvariant();
        if (cleared && spent)
        {
            return $"{issue} is still in the reading, but it is the same signal the last piece of "
                + "work was built on, so it cannot start the same thing again";
        }
        if (cleared) return $"{issue} is the one being picked up";
        return $"the closest is {issue}, and it {Tally.SignalStrength(got, need)}";
    }

    /// <summary>
    /// What the team reported, at theme level, never quoting a chip.
    /// </summary>
    /// <remarks>
    /// The chip wording is identifying on a small team, so it does not leave the engine. The
    /// issue file is named for the deficit it represents, which reads correctly for the fix
    /// and Yellow tallies and backwards for the build tally: a Green chip means the team said
    /// this is working, and describing them as having converged on a deficit tells Eran the
    /// opposite of what happened.
    /// </remarks>
    public string ThemeLevelCondition(Cycle cycle, Chip? chip)
    {
        var question = Ctx.Data.QuestionOf(cycle.TriggeringQuestionId)!;
        var theme = (cycle.Issue ?? "").ToLowerInvariant();
        var asked = $"Asked \"{question.Text}\", ";

        if (chip is { Polarity: "positive" })
        {
            return $"{asked}the team said this is one of the things that is working, and they said "
                + "so across the week rather than on a single day. It is filed under "
                + $"\"{cycle.Issue}\", which is the name of the problem this theme covers rather "
                + "than a description of what the team reported. Do not write it up as a fault.";
        }
        if (chip is { Polarity: "indifferent" })
        {
            return $"{asked}the team's answers were neither good nor bad on {theme}. That is not "
                + "the same as nothing being wrong: it is untested, unremarkable or unasked, and "
                + "it held across the week rather than on a single day.";
        }
        return $"{asked}the team's answers converged on {theme}. They pointed at the same "
            + "underlying condition rather than at one incident, and they did so across the week "
            + "rather than on a single day.";
    }

    private FeedItem OutcomeMessage(Team team, int day, Cycle cycle, Resolution resolution)
    {
        var nextStep = resolution.NextStep switch
        {
            "close" => "this one closes here",
            "deliver rung two" => "the next move goes out today, a change to how something works",
            "fail" => "both moves have been tried and neither shifted it",
            "cannot attribute" => "cannot attribute",
            "cannot be read" => "the question went unanswered, so there was nothing to read",
            _ => resolution.NextStep,
        };
        return PushFeed(team, new FeedItem
        {
            Id = Scope.NextFeedId(), TeamId = team.Id, Day = day, Kind = "outcome",
            CycleId = cycle.Id, Rung = cycle.Rung,
            Payload = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["signal_moved"] = resolution.Read.Moved,
                ["how_read"] = resolution.Read.HowRead,
                ["manager_confirmed"] = cycle.Confirmed,
                ["manager_saw_the_change"] = cycle.WorkedIfMet == true,
                ["next_step"] = nextStep,
            },
        });
    }

    private FeedItem TeamNote(Team team, int day, Cycle cycle, Resolution resolution)
    {
        var action = Ctx.Data.Action(cycle.ActionId)!;
        var (noteType, derived, reason) = NoteTypeFor(cycle);
        var outcome = resolution.Read.Unread
            ? "the question was not answered when it came round, so there is nothing to compare yet"
            : resolution.Read.Moved
                ? $"the signal on this question moved after the manager acted ({resolution.Read.HowRead})"
                : "the signal on this question has not moved yet";

        return PushFeed(team, new FeedItem
        {
            Id = Scope.NextFeedId(), TeamId = team.Id, Day = day, Kind = "note",
            CycleId = cycle.Id, Rung = cycle.Rung,
            NoteType = noteType, NoteTypeDerived = derived, NoteTypeReason = reason,
            Payload = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["note_type"] = noteType,
                // What the manager actually did, not what the action named (spec 5.1.4). The
                // team was in the room: a note describing an exercise they did not do is the
                // loop reporting something that did not happen, to the only people who can tell.
                ["action_text"] = Route.DeliveredActionText(Ctx, cycle),
                ["outcome"] = outcome,
                ["issue_title"] = action.Issue,
            },
        });
    }

    /// <summary>D31. The week's outward condition, written out, or null where there is none.</summary>
    private static string? OutwardLine(Team team, int day)
    {
        var o = team.OutwardList(day, limit: 1).FirstOrDefault();
        return o is null ? null
            : $"{o.Issue}. It {o.StrengthInWords}, on {o.DaysAbove} of this team's days. "
              + "The cause the team names sits outside the team, so the move is to raise it "
              + $"with {o.WhoItGoesTo} rather than to run anything here.";
    }

    private SimEvent WeeklyEvent(Team team, int day, StateApplication record)
    {
        var cycle = team.ActiveCycle;
        // D29. A confirmed cycle leaves the slot and waits for its reading, so "nothing is
        // running" was being said to a manager on a week when something was being measured
        // on their team. The slot being empty is a statement about their attention, not
        // about the system, and the weekly card is where the difference shows.
        var measuring = team.AwaitingRead.Where(c => c.SignalRead is null).ToList();
        var item = PushFeed(team, new FeedItem
        {
            Id = Scope.NextFeedId(), TeamId = team.Id, Day = day, Kind = "weekly",
            Payload = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["state_before"] = record.StateBefore ?? "calibrating",
                ["state_after"] = record.State,
                ["changed"] = record.Changed,
                // Line 1 only. Line 2 is fed by Question 2 and there is no Question 2 bank
                // (spec 7.1), so there is no second trend to report and none is invented.
                // Renamed from line_1_trend, which it never was. The value is the
                // vertical axis's leaning, which is the balance of answers, and the field sat
                // next to nothing about the lines at all. Calling it line 1 was readable as
                // the first of the two the board draws, and that is the one thing it does not
                // mean.
                ["answer_balance"] = record.VerticalLeaning ?? "not yet read",
                // D33. How the two questions compare, which is what the two lines are for and
                // what the weekly has never carried. In words: the figures are the machinery
                // panel's.
                ["how_the_questions_compare"] =
                    EngineState.CompareTracks(team, day, Ctx.Config.WindowDays).Reading,
                // What actually separates the states, which is locus and never a trend line.
                ["where_the_causes_sit"] = record.HorizontalLeaning ?? "not yet read",
                // Past tense and an elapsed count, because a bare "delivered day 9" read on
                // day 14 as a future date and Eran phrased it as one. The count is the team's
                // own working days: it used to be a calendar subtraction printed beside a
                // run-day index.
                ["active_cycle"] = cycle is null ? "nothing is running"
                    : $"{cycle.Issue} was delivered {Team.WorkingDaysBack(team, day, cycle.DeliveredDay)}. "
                      + "What was asked of the manager is "
                      + (cycle.Rung == 2
                          ? "a change to how something works, after the conversation did not shift it. "
                          : "a conversation, the first move on this. ")
                      + (cycle.Confirmed
                          ? $"They confirmed it {Team.WorkingDaysBack(team, day, cycle.ConfirmedDay)}. "
                          : "The system asked for confirmation "
                            + $"{Team.WorkingDaysBack(team, day, cycle.ConfirmationDueDay)} "
                            + "and has not had it. ")
                      + "The next check-back on it comes when a question on it comes round again.",
                // Named separately from active_cycle because it asks nothing of the
                // manager. It is there so the week can say a check-back is still coming rather
                // than implying the team has nothing open, and it carries no action, no
                // condition and no date to confirm.
                ["awaiting_a_check_back"] = measuring.Count > 0
                    ? string.Join("; ", measuring.Select(c =>
                        $"{c.Issue}, confirmed {Team.WorkingDaysBack(team, day, c.ConfirmedDay)}, "
                        + "still being measured"))
                    : null,
                // D31. Reported on the week as well as on the day, because the weekly card
                // is where a manager looks for what is building, and a condition that is
                // building and is not theirs belongs in that answer rather than being absent
                // from it.
                ["outward_condition"] = OutwardLine(team, day),
                ["floor_cleared"] = cycle is not null,
            },
        });
        return new SimEvent
        {
            Type = "weekly", Day = day, TeamId = team.Id, FeedId = item.Id,
            StateBefore = record.StateBefore, StateAfter = record.State, Changed = record.Changed,
            FloorCleared = cycle is not null,
            Label = $"{team.Name}: weekly recalculation, {record.StateBefore ?? "calibrating"} to {record.State}",
        };
    }

    public SimEvent MonthlySummary(Team team, int day, string? monthLabel = null)
    {
        var label = monthLabel ?? MonthLabel(day);
        // The first calendar day of this month. It used to be derived by subtracting the day
        // of the month from the day number, which held only while the two counted the same
        // thing. Under D24 they do not: day 30 of the run is not the 30th of the month.
        var monthStart = Calendar.FirstDayOfMonth(StartDate, day);
        var cycles = team.CycleHistory.Where(c => c.ResolvedDay >= monthStart).ToList();
        var states = team.StateHistory.Where(s => s.Day >= monthStart).ToList();

        var actions = new List<Dictionary<string, object?>>();
        foreach (var c in cycles)
        {
            var worksheet = Route.DeliveredWorksheet(c, 1);
            var row = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["when"] = Team.WorkingDaysBack(team, day, c.SelectedDay),
                ["area"] = c.Issue,
                ["move"] = c.Rung == 2
                    ? "second move, a change to how something works"
                    : "first move, a conversation",
                ["outcome"] = (c.Outcome ?? "").Replace("_", " "),
                ["action"] = Route.DeliveredActionText(Ctx, c),
            };
            if (worksheet is not null)
            {
                var name = $"{worksheet.Code} {worksheet.Title}";
                // Where a cycle laddered the row reports rung two, but a worksheet handed
                // over by its first move is still something that happened this month. Named
                // under its own key rather than beside a second-move action, where it would
                // read as the thing that was run.
                if (c.Rung == 2) row["worksheet_run_before_this"] = name;
                else row["worksheet"] = name;
            }
            actions.Add(row);
        }

        var counts = team.CycleCountsOf();
        var item = PushFeed(team, new FeedItem
        {
            Id = Scope.NextFeedId(), TeamId = team.Id, Day = day, Kind = "monthly", Month = label,
            Payload = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["actions_taken"] = actions,
                ["state_start"] = states.Count > 0
                    ? states[0].StateBefore ?? team.State ?? "calibrating"
                    : team.State ?? "calibrating",
                ["state_end"] = team.State ?? "calibrating",
                ["two_line_trend"] = states.Count > 0
                    ? $"the balance of answers is {states[^1].VerticalLeaning}, and the causes named "
                      + $"sit {states[^1].HorizontalLeaning}"
                    : "not yet calculated",
                ["confirmations"] = cycles.Where(c => c.ConfirmedDay is not null)
                    .Select(c => $"{c.Issue}, confirmed "
                        + $"{Team.WorkingDaysBack(team, day, c.ConfirmedDay)}").ToList(),
                ["cycles"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["closed"] = counts.Closed, ["failed"] = counts.Failed,
                    ["unattributed"] = counts.Unattributed, ["unread"] = counts.Unread,
                    ["running"] = counts.Running,
                },
                ["repeated_draws"] = team.FailedFiles.Values
                    .Where(f => f.Count > 1 || f.Escalated)
                    .Select(f => $"{f.Issue}, tried and not shifted {f.Count} time(s)").ToList(),
            },
        });
        return new SimEvent
        {
            Type = "monthly", Day = day, TeamId = team.Id, FeedId = item.Id, Month = label,
            Label = $"{team.Name}: monthly coaching summary for {label}",
        };
    }

    private SimEvent EscalationEvent(Team team, int day, Escalation escalation)
    {
        var item = PushFeed(team, new FeedItem
        {
            Id = Scope.NextFeedId(), TeamId = team.Id, Day = day, Kind = "escalation",
            Payload = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["issue_title"] = escalation.Issue,
                ["failed_cycles_count"] = escalation.FailedCycles,
                // As delivered, not as named. A manager told the team was given an exercise
                // they never ran stops trusting the record, and the escalation is the one
                // message that asks them to take something seriously (spec 5.1.4).
                ["actions_tried"] = escalation.ActionsTriedText,
                ["weeks_span"] = escalation.WeeksSpan,
            },
        });
        return new SimEvent
        {
            Type = "escalation", Day = day, TeamId = team.Id, FeedId = item.Id,
            IssueFile = escalation.IssueFile,
            Label = $"{team.Name}: {escalation.Issue} has drawn again after failing",
        };
    }

    private static SimEvent ActionActiveEvent(Team team, int day, Cycle cycle, bool rungTwo = false) =>
        new()
        {
            // The playbook itself rides on the day's daily message, which is written after
            // this in the tick and picks the action up from the team. One message a day.
            Type = "action_active", Day = day, TeamId = team.Id, CycleId = cycle.Id,
            Rung = cycle.Rung, RungTwo = rungTwo,
            Label = $"{team.Name}: {(rungTwo ? "rung two" : "an action")} became active ({cycle.Issue})",
        };

    // --------------------------------------------------------------- senior view

    /// <summary>D26. The week every child row is read against.</summary>
    public Calendar.Week RollUpWeek(int? day = null, Team? seniorTeam = null)
    {
        var ends = seniorTeam?.RollUpWeekEnds ?? RollUpWeekEnds;
        return Calendar.RollUpWeek(StartDate, day ?? Day, ends);
    }

    public sealed class RollUpRow
    {
        public required string TeamId { get; init; }
        public required string Name { get; init; }
        public required int Enrolled { get; init; }
        public required string State { get; init; }
        public required bool StateWithheld { get; init; }
        public required string CycleStatus { get; init; }
        public required int Closed { get; init; }
        public required int Failed { get; init; }
        public required int WeekFrom { get; init; }
        public required int WeekTo { get; init; }
        public required int ClosedThisWeek { get; init; }
        public required int? ReadOn { get; init; }
    }

    /// <summary>
    /// The roll-up (build spec 7.3). Read-only, and deliberately thin.
    /// </summary>
    /// <remarks>
    /// Team name, state, cycle status, and closed against failed. Not the issue file, not
    /// the action, not the chips. <b>A child team below four enrolled members shows as
    /// calibrating whatever its actual state</b>, because on a team of three, naming the
    /// state names who is unhappy (build spec 7.5).
    /// </remarks>
    public List<RollUpRow> RollUp(Team seniorTeam)
    {
        var week = RollUpWeek(Day, seniorTeam);
        var rows = new List<RollUpRow>();
        foreach (var childId in seniorTeam.ChildTeamIds)
        {
            var child = _teams[childId];
            var enrolled = child.EnrolledOn(Math.Max(1, Day)).Count;
            var visibilityFloor = enrolled < Ctx.Config.RollUpVisibilityFloor;
            var counts = child.CycleCountsOf();
            var cycle = child.ActiveCycle;
            var cycleStatus = "none";
            if (cycle is not null)
            {
                cycleStatus = cycle.SignalReadDay is not null && cycle.Confirmed ? "closing" : "active";
            }
            else if (child.CycleHistory.Count > 0)
            {
                cycleStatus = child.CycleHistory[^1].Outcome == "failed" ? "failed" : "none";
            }

            int? readOn = null;
            for (var d = week.To; d >= week.From; d -= 1)
            {
                if (child.IsReadingDay(d)) { readOn = d; break; }
            }

            rows.Add(new RollUpRow
            {
                TeamId = child.Id,
                Name = child.Name,
                Enrolled = enrolled,
                State = visibilityFloor ? "calibrating" : child.State ?? "calibrating",
                StateWithheld = visibilityFloor,
                CycleStatus = cycleStatus,
                Closed = counts.Closed,
                Failed = counts.Failed,
                WeekFrom = week.From,
                WeekTo = week.To,
                ClosedThisWeek = child.CycleHistory.Count(c =>
                    c.Outcome == "closed" && c.ResolvedDay >= week.From && c.ResolvedDay <= week.To),
                ReadOn = readOn,
            });
        }
        return rows;
    }

    /// <summary>
    /// The senior-level trigger (build spec 7.4): the same issue file active in three or more
    /// child teams at once, or failing in two or more.
    /// </summary>
    /// <remarks>
    /// <b>Thresholds are guessed rather than derived, and are flagged as provisional
    /// everywhere they appear.</b>
    /// </remarks>
    public SimEvent? CheckCrossTeam(Team seniorTeam, int day)
    {
        var active = new List<(string File, List<string> Teams)>();
        var failing = new List<(string File, List<string> Teams)>();
        // D31. A third basis, and the only one that does not need a cycle.
        //
        // The two above count what was delivered: a file active in three child teams, or
        // failing in two. An outward condition is never delivered, so neither could ever see
        // one, and the conditions most worth a senior reading are precisely the ones no
        // individual manager can act on. Three teams naming the same external cause is a fact
        // about the organisation and not about any of the three managers, and until now it was
        // the one pattern the roll-up was structurally blind to.
        var outward = new List<(string File, List<string> Teams)>();
        void Add(List<(string File, List<string> Teams)> into, string file, string teamId)
        {
            var found = into.FirstOrDefault(x => x.File == file);
            if (found.Teams is null) { into.Add((file, [teamId])); return; }
            found.Teams.Add(teamId);
        }

        foreach (var childId in seniorTeam.ChildTeamIds)
        {
            var child = _teams[childId];
            if (child.ActiveCycle is { } cycle) Add(active, cycle.IssueFile, child.Id);
            foreach (var record in child.FailedFiles.Values) Add(failing, record.IssueFile, child.Id);
            // Asked of the child's own list rather than of its tally, so the threshold counts
            // teams for which the condition is live today and not teams that reported it once
            // in a window that has since moved on.
            foreach (var row in child.OutwardList(day, limit: 10)) Add(outward, row.IssueFile, child.Id);
        }

        (string File, List<string> Teams, string Basis)? hit = null;
        foreach (var (file, teams) in active)
        {
            if (teams.Count >= Ctx.Config.CrossTeamActiveTeams)
            {
                hit = (file, teams, "active in three or more child teams");
                break;
            }
        }
        if (hit is null)
        {
            foreach (var (file, teams) in failing)
            {
                if (teams.Count >= Ctx.Config.CrossTeamFailingTeams)
                {
                    hit = (file, teams, "failing in two or more child teams");
                    break;
                }
            }
        }
        if (hit is null)
        {
            // Three, the same as the active threshold, and for the same reason: two teams is
            // a coincidence a senior manager can reach for an explanation of, and three is a
            // pattern. Provisional like every other threshold here.
            foreach (var (file, teams) in outward)
            {
                if (teams.Count >= Ctx.Config.CrossTeamActiveTeams)
                {
                    hit = (file, teams,
                        "named as an external cause by three or more child teams, and not "
                        + "deliverable to any of them");
                    break;
                }
            }
        }
        if (hit is not { } found2) return null;
        if (seniorTeam.CrossTeamCondition is { ResolvedDay: null } open && open.IssueFile == found2.File)
        {
            return null;
        }
        if (seniorTeam.CrossTeamHistory.Any(c => c.IssueFile == found2.File)) return null;

        var issue = Ctx.Data.IssueFile(found2.File);
        seniorTeam.CrossTeamCondition = new CrossTeamCondition
        {
            Day = day,
            IssueFile = found2.File,
            Issue = issue?.Issue ?? found2.File,
            ChildTeams = found2.Teams.Select(id => _teams[id].Name).ToList(),
            Basis = found2.Basis,
        };

        var item = PushFeed(seniorTeam, new FeedItem
        {
            Id = Scope.NextFeedId(), TeamId = seniorTeam.Id, Day = day, Kind = "cross_team",
            Payload = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["issue_title"] = issue?.Issue ?? found2.File,
                ["basis"] = found2.Basis,
                ["child_teams"] = seniorTeam.CrossTeamCondition.ChildTeams,
                ["provisional"] = true,
            },
        });
        return new SimEvent
        {
            Type = "cross_team", Day = day, TeamId = seniorTeam.Id, FeedId = item.Id,
            Provisional = true,
            Label = $"{seniorTeam.Name}: cross-team concentration on {issue?.Issue ?? found2.File}",
        };
    }

    /// <summary>The senior manager confirms the cross-team condition (build spec 7.4).</summary>
    public CrossTeamCondition? ConfirmCrossTeam(Team seniorTeam, int day, string source = "manager")
    {
        var condition = seniorTeam.CrossTeamCondition;
        if (condition is null || condition.Confirmed || condition.ResolvedDay is not null) return null;
        condition.Confirmed = true;
        condition.ConfirmedDay = day;
        condition.ConfirmationSource = source;
        return condition;
    }

    /// <summary>
    /// Close the cross-team condition and write the note.
    /// </summary>
    /// <remarks>
    /// The note goes to the managers, never to their teams. A note about a pattern across
    /// three teams, delivered into those teams, tells each of them that the others reported
    /// the same thing, which is a comparison the layer never makes and the teams never asked
    /// for. It is addressed to the people who can act on it.
    /// </remarks>
    public SimEvent? ResolveCrossTeam(Team seniorTeam, int day)
    {
        var condition = seniorTeam.CrossTeamCondition;
        if (condition is null || condition.ResolvedDay is not null) return null;

        condition.ResolvedDay = day;
        condition.Outcome = condition.Confirmed ? "closed" : "cannot_attribute";

        PushFeed(seniorTeam, new FeedItem
        {
            Id = Scope.NextFeedId(), TeamId = seniorTeam.Id, Day = day, Kind = "cross_team_note",
            Payload = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["issue_title"] = condition.Issue,
                ["child_teams"] = condition.ChildTeams,
                ["basis"] = condition.Basis,
                ["manager_confirmed"] = condition.Confirmed,
                ["days_open"] = day - condition.Day,
                ["outcome"] = condition.Outcome,
                ["audience"] = "the managers who report to this senior manager, never their teams",
                ["provisional"] = true,
            },
        });

        seniorTeam.CrossTeamHistory.Add(condition);
        seniorTeam.CrossTeamCondition = null;

        return new SimEvent
        {
            Type = "cross_team_resolved", Day = day, TeamId = seniorTeam.Id,
            IssueFile = condition.IssueFile, Outcome = condition.Outcome, Provisional = true,
            Label = $"{seniorTeam.Name}: cross-team condition {condition.Outcome}",
        };
    }

    /// <summary>
    /// The cross-team condition's own clock, run once a day for a senior manager.
    /// </summary>
    /// <remarks>
    /// It borrows the team cycle's shape: the confirmation falls due 72 hours after the
    /// condition is raised, and the condition resolves a full cycle later. There is no signal
    /// to read at senior level, because the pattern is measured in the child teams and
    /// reading it back would mean reaching into them, so the outcome turns on the
    /// confirmation alone.
    /// </remarks>
    private void TickCrossTeam(Team seniorTeam, int day, List<SimEvent> events)
    {
        var condition = seniorTeam.CrossTeamCondition;
        if (condition is null) return;

        // D24 amended. The senior team's own days, like every other interval a cycle is timed
        // in. Counted in dates, a condition raised on a Thursday fell due and resolved on days
        // the manager of managers was not at work.
        var dueOn = seniorTeam.AskedDaysAfter(condition.Day, Ctx.Config.ConfirmationLag);
        if (!condition.Confirmed && day == dueOn)
        {
            events.Add(new SimEvent
            {
                Type = "cross_team_confirmation_due", Day = day, TeamId = seniorTeam.Id,
                Provisional = true,
                Label = $"{seniorTeam.Name}: 72-hour confirmation due on the cross-team condition",
            });
        }
        // The scripted senior manager acts.
        // The sixth day space crossing, and the same shape as the other five. dueOn is
        // computed in the senior team's own asked days and then converted, which is right;
        // dueOn + 1 adds a calendar day to the converted answer, which is not. On a seven-day
        // week the two are the same number and nothing shows. On a five-day week, whenever the
        // day after the due date is a Saturday, the condition confirms on a day the manager of
        // managers was not at work, and the run diverges from the same run on a seven-day week
        // by one asked day. See src/engine/simulation.js for why it was latent until the team
        // track began delivering.
        var actsOn = seniorTeam.AskedDaysAfter(condition.Day, Ctx.Config.ConfirmationLag + 1);
        if (!condition.Confirmed && day >= actsOn && seniorTeam.AutoConfirm)
        {
            ConfirmCrossTeam(seniorTeam, day, "scripted manager");
            events.Add(new SimEvent
            {
                Type = "cross_team_confirmed", Day = day, TeamId = seniorTeam.Id,
                Label = $"{seniorTeam.Name}: confirmed the cross-team condition",
            });
        }
        if (day >= seniorTeam.AskedDaysAfter(condition.Day, Ctx.Config.CrossTeamCycleDays))
        {
            var resolved = ResolveCrossTeam(seniorTeam, day);
            if (resolved is not null) events.Add(resolved);
        }
    }

    // ------------------------------------------------------------------ controls

    /// <summary>
    /// The beats worth stopping on (build spec 8).
    /// </summary>
    /// <remarks>
    /// Escalation is on the list because a repeated unresolved draw (spec 9.5) is the one
    /// moment the system admits it cannot fix something. "Confirmed" is deliberately not: it
    /// is the scripted manager acting on the presenter's behalf, and the beat it belongs to
    /// has already been stopped on when the confirmation fell due.
    /// </remarks>
    public static readonly HashSet<string> StopEvents = new(StringComparer.Ordinal)
    {
        "action_active", "confirmation_due", "signal_read", "weekly", "monthly", "escalation",
    };

    /// <summary>Jump the clock forward. The clock only runs forwards.</summary>
    public List<SimEvent> JumpToDay(int target)
    {
        var collected = new List<SimEvent>();
        var clamped = Math.Min(target, Horizon);
        while (Day < clamped) collected.AddRange(Step().Events);
        return collected;
    }
}
