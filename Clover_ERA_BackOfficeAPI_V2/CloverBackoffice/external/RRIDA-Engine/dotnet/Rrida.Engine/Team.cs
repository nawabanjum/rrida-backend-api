namespace Rrida.Engine;

public sealed class Member
{
    public required string Id { get; init; }
    public string? Name { get; init; }
    public int JoinedDay { get; init; } = 1;
    public int? LeftDay { get; init; }
}

/// <summary>One scripted answer: who, on which track, with what face and which chips.</summary>
public sealed record ScriptEntry(string Member, string? Track, string Smiley, IReadOnlyList<string> Chips, string? Text);

public sealed class TeamConfig
{
    public required string Id { get; init; }
    public string Name { get; init; } = "";
    public string? Industry { get; init; }
    public string? ManagerName { get; init; }
    public string ManagerLevel { get; init; } = "Team manager";
    public string? Arc { get; init; }
    public string? IntendedState { get; init; }
    public List<string> IntendedRoutes { get; init; } = [];
    public List<string> ChildTeamIds { get; init; } = [];
    public required List<Member> Roster { get; init; }

    /// <summary>OURS, not upstream (Team.Headcount.cs). Empty: the members enrolled count, as upstream.</summary>
    public List<HeadcountPeriod> Headcount { get; init; } = [];

    /// <summary>Keyed by the team's own asked-day index, not by date (D24 amended).</summary>
    public Dictionary<int, List<ScriptEntry>> Script { get; init; } = [];

    public string? WorkingWeek { get; init; }
    public string? StartDate { get; init; }

    /// <summary>D26. Only ever read off a senior team. Null means the run's default applies.</summary>
    public int? RollUpWeekEnds { get; init; }

    public List<(int CycleIndex, int Rung, int? DayOffset, bool WorkedIfMet)> Confirmations { get; init; } = [];
    public (int? DayOffset, bool WorkedIfMet)? DefaultConfirmation { get; init; }
    public bool AutoConfirm { get; init; } = true;
}

/// <summary>
/// A team: its roster, its recorded responses, its state, and its one active cycle.
/// Deterministic spec sections 4, 7, 8 and 9.
/// </summary>
public sealed partial class Team
{
    /// <summary>
    /// The run this team belongs to, which is what numbers its cycles.
    /// </summary>
    /// <remarks>
    /// A team built on its own gets a scope of its own, because it is its own run. A team
    /// built by a Simulation shares that simulation's, so cycle ids run in one sequence
    /// across the teams in a run, which is what the reference does and what the fixtures
    /// record.
    /// </remarks>
    public RunScope Scope { get; }

    public EngineContext Ctx { get; }
    public string Id { get; }
    public string Name { get; }
    public string? Industry { get; }
    public string? ManagerName { get; }
    public string ManagerLevel { get; }
    public string? Arc { get; }
    public string? IntendedState { get; }
    public IReadOnlyList<string> IntendedRoutes { get; }
    public IReadOnlyList<string> ChildTeamIds { get; }
    public List<Member> Roster { get; }

    public string StartDate { get; }
    public string WorkingWeek { get; }
    public int[] Schedule { get; }

    /// <summary>D25. The weekly reading lands on the last day of this team's own week.</summary>
    public int ReadingWeekday { get; }

    public int? RollUpWeekEnds { get; }

    private readonly Dictionary<int, List<ScriptEntry>> _script;
    private readonly List<(int CycleIndex, int Rung, int? DayOffset, bool WorkedIfMet)> _scriptedConfirmations;
    private readonly (int? DayOffset, bool WorkedIfMet)? _defaultConfirmation;
    public bool AutoConfirm { get; }

    // askedCount[d] is how many of this team's days fall in 1..d, grown as asked.
    private readonly List<int> _askedCount = [0];

    // askedDays[n] is the calendar day of this team's nth asked day. Index 0 unused, so the
    // two read the same way round: AskedDaysTo gives an index, this takes one.
    private readonly List<int> _askedDays = [0];

    private readonly Dictionary<int, List<Response>> _responsesByDay = [];

    public List<Response> Responses { get; } = [];
    public string? State { get; private set; }
    public LocusFilter? LocusFilter { get; private set; }
    public object? Resolved { get; private set; }
    public List<StateApplication> StateHistory { get; } = [];

    public Cycle? ActiveCycle { get; set; }

    /// <summary>
    /// D29. Cycles the manager has finished with and the system has not.
    /// </summary>
    /// <remarks>
    /// A cycle occupies the manager from delivery to confirmation, which is about four of
    /// this team's days; it then waits for its condition to be asked again, which on the
    /// real question set is twenty or more. Measured on an uncurated team of eight: of the
    /// eighty-four asked days where something had cleared the floor and could not be acted
    /// on, sixty-eight were days the manager had already confirmed and was waiting on a
    /// measurement they take no part in. So the slot is freed at confirmation and the
    /// reading happens behind it.
    /// </remarks>
    public List<Cycle> AwaitingRead { get; } = [];

    /// <summary>
    /// A background cycle whose read asked for rung two, waiting for the slot. Rung two is
    /// owed to a condition that did not shift, so it is not dropped; it is queued and goes
    /// in front of a fresh selection when the manager is free.
    /// </summary>
    public List<Cycle> PendingRungTwo { get; } = [];

    public List<Cycle> CycleHistory { get; } = [];

    /// <summary>Rung two actions that have closed for this team on this file, during this run.</summary>
    public HashSet<string> InstalledMechanisms { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, WorksheetRecord> WorksheetHistory { get; } = new(StringComparer.Ordinal);

    /// <summary>Content actions the manager has declined. One per capability-gap file.</summary>
    public HashSet<string> DeclinedActions { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// D30. The team's own day on which the last worksheet session was delivered, so the
    /// cadence cap has something to measure from.
    /// </summary>
    public int? LastSessionAskedDay { get; set; }

    /// <summary>
    /// D28. Every condition that has cleared the floor, whether or not anything was done
    /// about it. Keyed by issue file, which is the bucket. See <see cref="UpdateWatch"/>.
    /// </summary>
    public Dictionary<string, WatchRecord> ConditionWatch { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// D31. The same, for conditions whose cause the team has placed outside itself.
    /// </summary>
    /// <remarks>
    /// A separate store on purpose. The two lists answer different questions: the watch says
    /// what is building that the manager could be given next, and this says what is building
    /// that is not theirs. Folded together, the outward ones would sit at the top of a list
    /// of three forever, since nothing ever resolves them, and push out the thing the manager
    /// can actually act on.
    /// </remarks>
    public Dictionary<string, WatchRecord> OutwardWatch { get; } = new(StringComparer.Ordinal);

    public string? PendingSuccessionFile { get; set; }
    public string? HeldIssueFile { get; set; }
    public Dictionary<string, FailedFile> FailedFiles { get; } = new(StringComparer.Ordinal);
    public List<Escalation> Escalations { get; } = [];

    /// <summary>Senior level only (build spec 7.4). Initialised rather than left undefined.</summary>
    public CrossTeamCondition? CrossTeamCondition { get; set; }

    public List<CrossTeamCondition> CrossTeamHistory { get; } = [];
    public List<FeedItem> Feed { get; } = [];
    public SelectionAttempt? LastSelectionAttempt { get; private set; }

    public Team(EngineContext ctx, TeamConfig config, RunScope? scope = null)
    {
        Ctx = ctx;
        Scope = scope ?? new RunScope();
        Id = config.Id;
        Name = config.Name;
        Industry = config.Industry;
        ManagerName = config.ManagerName;
        ManagerLevel = config.ManagerLevel;
        Arc = config.Arc;
        IntendedState = config.IntendedState;
        IntendedRoutes = config.IntendedRoutes;
        ChildTeamIds = config.ChildTeamIds;
        Roster = config.Roster;
        InitHeadcount(config);

        // D24. The working week the manager chose, and the calendar it is read against.
        StartDate = config.StartDate ?? ctx.Calendar.DefaultStartDate;
        WorkingWeek = config.WorkingWeek ?? ctx.Calendar.DefaultSchedule;
        Schedule = ctx.Calendar.Resolve(WorkingWeek);
        ReadingWeekday = Calendar.LastDayOfWeek(Schedule);
        RollUpWeekEnds = config.RollUpWeekEnds;

        _script = config.Script;
        _scriptedConfirmations = config.Confirmations;
        _defaultConfirmation = config.DefaultConfirmation;
        AutoConfirm = config.AutoConfirm;
    }

    // ------------------------------------------------------------- the team's week

    /// <summary>
    /// Is this calendar day one the team is asked on?
    /// </summary>
    /// <remarks>
    /// The rotation runs seven days a week and belongs to everyone; this decides whether
    /// this particular team hears it. A team not asked today produces no answers, which is a
    /// different fact from a team asked and silent, and the layer keeps them apart: see D23,
    /// where a signal read landing on a day like this defers rather than reading zero.
    /// </remarks>
    public bool IsAskedOn(int day) => day >= 1 && Schedule.Contains(Calendar.WeekdayOf(StartDate, day));

    /// <summary>
    /// How many of the team's own days have happened by <paramref name="day"/>. This is the
    /// team's clock, and every window in the layer counts these rather than calendar days.
    /// </summary>
    public int AskedDaysTo(int day)
    {
        while (_askedCount.Count <= day)
        {
            var d = _askedCount.Count;
            _askedCount.Add(_askedCount[d - 1] + (IsAskedOn(d) ? 1 : 0));
        }
        return _askedCount[Math.Max(0, day)];
    }

    /// <summary>
    /// The calendar day on which a window of <paramref name="n"/> of the team's days,
    /// ending at <paramref name="day"/>, began.
    /// </summary>
    /// <remarks>
    /// A seven-day tally window is seven days the team answered on, which for a five-day
    /// week spans nine or ten calendar days. Clamped to day 1, because a window cannot reach
    /// back past the start of the run and a team in its first week is answering from a
    /// shorter window rather than from nothing.
    /// </remarks>
    public int AskedWindowFrom(int day, int n)
    {
        var seen = 0;
        for (var d = day; d >= 1; d -= 1)
        {
            if (!IsAskedOn(d)) continue;
            seen += 1;
            if (seen >= n) return d;
        }
        return 1;
    }

    /// <summary>
    /// The calendar day of this team's nth asked day. The inverse of <see cref="AskedDaysTo"/>.
    /// </summary>
    /// <remarks>
    /// D24 amended: the team's nth asked day is offered the nth question of each bank, so a
    /// team walks all ninety in order whatever days it works and a chip authored for
    /// question n still meets question n. Everything the engine times off a cycle is derived
    /// through here, so a run is the same run on any schedule, stretched in calendar time
    /// and not otherwise changed.
    /// </remarks>
    public int CalendarDayForAsked(int n)
    {
        if (n < 1) return 1;
        while (_askedDays.Count <= n)
        {
            var next = (_askedDays.Count > 0 ? _askedDays[^1] : 0) + 1;
            var day = next;
            while (!IsAskedOn(day)) day += 1;
            _askedDays.Add(day);
        }
        return _askedDays[n];
    }

    /// <summary>
    /// <paramref name="n"/> of the team's own days after <paramref name="day"/>, as a calendar day.
    /// </summary>
    /// <remarks>
    /// The unit every cycle is timed in. Three days to a confirmation means three days the
    /// team was asked, so a confirmation never falls due on a day nobody is there to give it.
    /// </remarks>
    public int AskedDaysAfter(int day, int n) => CalendarDayForAsked(AskedDaysTo(day) + n);

    /// <summary>How long ago, in the team's own working days.</summary>
    /// <remarks>
    /// Every manager-facing surface says time this way. The run-day index is an internal
    /// coordinate: "day 98" counts days since this team started and means nothing to the
    /// person reading it, and it reached the live environment.
    ///
    /// Not a date, for two reasons. Month abbreviations collide with real names, so a team
    /// with someone called Jun or May would trip the no-individual-names guard or force it
    /// to be loosened. And a cycle can resolve on a day the team was never asked, so a date
    /// can name a Saturday to a team that does not work Saturdays.
    /// </remarks>
    public static string? WorkingDaysBack(Team team, int day, int? then)
    {
        if (then is not { } t) return null;
        var n = team.AskedDaysTo(day) - team.AskedDaysTo(t);
        if (n <= 0) return "today";
        return n == 1 ? "1 working day ago" : $"{n} working days ago";
    }

    /// <summary>How far ahead, in the team's own working days.</summary>
    public static string? WorkingDaysAhead(Team team, int day, int? then)
    {
        if (then is not { } t) return null;
        var n = team.AskedDaysTo(t) - team.AskedDaysTo(day);
        if (n <= 0) return "today";
        return n == 1 ? "in 1 working day" : $"in {n} working days";
    }

    /// <summary>D25. The weekly reading, on the last day of the team's own week.</summary>
    public bool IsReadingDay(int day) =>
        IsAskedOn(day)
        && Calendar.WeekdayOf(StartDate, day) == ReadingWeekday
        && AskedDaysTo(day) > Ctx.Config.CalibrationDays;

    // ---------------------------------------------------------------- enrolment

    public List<Member> EnrolledOn(int day) =>
        Roster.Where(m => m.JoinedDay <= day && (m.LeftDay is null || m.LeftDay > day)).ToList();

    public int Size => Roster.Count;

    // ---------------------------------------------------------------- responses

    public List<Response> ResponsesBetween(int from, int to, string? track = null)
    {
        var output = new List<Response>();
        for (var day = from; day <= to; day += 1)
        {
            if (!_responsesByDay.TryGetValue(day, out var dayResponses)) continue;
            foreach (var response in dayResponses)
            {
                if (track is null || response.Track == track) output.Add(response);
            }
        }
        return output;
    }

    public sealed record RecordedDay(int Day, List<Rotation.Slot> Slots, int? QuestionId, List<Response> Responses);

    /// <summary>
    /// Record the day's answers from the scenario script.
    /// </summary>
    /// <remarks>
    /// A day is two questions: the individual one, then the team one. A member contributes
    /// at most once per question per day (spec 4.3), so a script entry names its track and a
    /// second entry for the same member on the same track is a scripting error, rejected
    /// rather than silently merged.
    /// </remarks>
    public RecordedDay RecordDay(int day)
    {
        // D24. Not a day this team works, so nothing is asked and nothing is recorded. The
        // script should carry no entry for such a day; if one is there it is a scripting
        // error and saying so beats silently dropping answers.
        if (!IsAskedOn(day))
        {
            _responsesByDay[day] = [];
            return new RecordedDay(day, [], null, []);
        }

        // D24 amended. The script is the team's own sequence of answering days, and so is
        // the rotation: its nth entry meets the nth question. A script written against a
        // contiguous rotation therefore needs no re-authoring to run on a five-day week.
        var askedIndex = AskedDaysTo(day);
        var scripted = _script.TryGetValue(askedIndex, out var rows) ? rows : [];
        var enrolled = EnrolledOn(day).Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
        var asked = Ctx.Rotation.TracksAsked;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var recorded = new List<Response>();

        foreach (var entry in scripted)
        {
            var track = entry.Track ?? "individual";
            if (!asked.Contains(track))
            {
                throw new InvalidOperationException(
                    $"{Id} day {day}: the script answers on the {track} track, but that track has no "
                    + "bank behind it");
            }
            var questionId = Ctx.Rotation.QuestionIdForDay(Calendar.RotationDay(this, day), track)
                ?? throw new InvalidOperationException($"{Id} day {day}: the {track} rotation has no question");

            if (!enrolled.Contains(entry.Member))
            {
                throw new InvalidOperationException(
                    $"{Id} day {day}: {entry.Member} is not enrolled but the script has them answering");
            }
            var key = $"{track}:{entry.Member}";
            if (!seen.Add(key))
            {
                throw new InvalidOperationException($"{Id} day {day}: {entry.Member} answers the {track} question twice");
            }

            if (!Vocabulary.SmileyPolarity.TryGetValue(entry.Smiley, out var polarity))
            {
                throw new InvalidOperationException($"{Id} day {day}: unknown smiley {entry.Smiley}");
            }
            var chips = entry.Chips;
            if (chips.Count > 3)
            {
                throw new InvalidOperationException($"{Id} day {day}: {entry.Member} selected {chips.Count} chips, max 3");
            }
            // Spec 4.3: a reason is compulsory. Zero chips is valid only with free text, and
            // Continue stays inactive until one or the other is given. An answer never leaves
            // the screen carrying a level and no reason at all, so a script that produces one
            // is producing an answer the app cannot.
            var text = string.IsNullOrWhiteSpace(entry.Text) ? null : entry.Text.Trim();
            if (chips.Count == 0 && text is null)
            {
                throw new InvalidOperationException(
                    $"{Id} day {day}: {entry.Member} answered {entry.Smiley} with no chip and no free "
                    + "text. A reason is compulsory (spec 4.3)");
            }
            foreach (var chipId in chips)
            {
                var chip = Ctx.Data.Chip(chipId)
                    ?? throw new InvalidOperationException($"{Id} day {day}: chip {chipId} is not in the bank");
                if (chip.QuestionId != questionId)
                {
                    throw new InvalidOperationException(
                        $"{Id} day {day}: chip {chipId} belongs to question {chip.QuestionId}, "
                        + $"but the {track} rotation asks {questionId}");
                }
                if (chip.Polarity != polarity)
                {
                    throw new InvalidOperationException(
                        $"{Id} day {day}: {entry.Member} chose {entry.Smiley} but chip {chipId} "
                        + $"is in the {chip.Polarity} set");
                }
            }
            recorded.Add(new Response
            {
                Day = day,
                MemberId = entry.Member,
                QuestionId = questionId,
                Track = track,
                Smiley = entry.Smiley,
                Chips = chips.ToList(),
                Text = text,
            });
        }

        _responsesByDay[day] = recorded;
        Responses.AddRange(recorded);

        var slots = Ctx.Rotation.SlotsForDay(Calendar.RotationDay(this, day));
        var individual = slots.FirstOrDefault(s => s.Track == "individual");
        // The individual question stays the day's headline question: it is the one the
        // signal read and the succession rule are written against.
        return new RecordedDay(day, slots, individual?.QuestionId, recorded);
    }

    // ------------------------------------------------------------------- window

    public WindowedTallies Windowed(int day) =>
        Tally.Build(Ctx, this, day, Ctx.DeliveryTracks, LocusFilter, locusFilterWasGiven: true);

    /// <summary>
    /// D28. Record what cleared the floor today, acted on or not.
    /// </summary>
    /// <remarks>
    /// The engine works one condition at a time, which is the right discipline for a manager
    /// and is also why a team can report something serious for weeks and never hear of it:
    /// selection is gated on there being no open cycle, and while one is open nothing else
    /// is looked at, let alone remembered. Measured on an uncurated team of eight over
    /// ninety asked days, fifty-three distinct conditions cleared the floor and two were
    /// acted on, and three of those passed over carried a higher severity and more people
    /// than the one that was. Six of eight reported a career-development problem for
    /// seventeen days and nothing was ever delivered about it.
    /// <para>
    /// This is the memory that was missing. Without it a condition above the floor for
    /// seventeen days and one that crossed it once are the same object, so "building" cannot
    /// be said at all, and early warning is the product's claim.
    /// </para>
    /// <para>It selects nothing, routes nothing and delivers nothing.</para>
    /// </remarks>
    public void UpdateWatch(int day)
    {
        if (!IsAskedOn(day)) return;
        if (State is null) return;
        var live = EngineState.LiveTally.GetValueOrDefault(State);
        if (live is null) return;

        var windowed = Windowed(day);
        // D31. The outward bucket is watched too, and into its own store. See OutwardWatch.
        UpdateOutwardWatch(day, windowed);
        if (!windowed.Tallies.TryGetValue(live, out var bucket)) return;
        var required = windowed.Floor.Required;
        var askedDay = AskedDaysTo(day);

        foreach (var entry in bucket)
        {
            var contributors = entry.Contributors.Count;
            if (contributors < required) continue;
            var action = Ctx.Data.Action(entry.ActionId);
            var issueFile = action?.IssueFile ?? entry.ActionId;

            if (!ConditionWatch.TryGetValue(issueFile, out var record))
            {
                record = new WatchRecord { IssueFile = issueFile, Issue = action?.Issue ?? issueFile };
                ConditionWatch[issueFile] = record;
            }
            // One sample per asked day. A day can be re-ticked in a reload, and two samples
            // for one day would count that day's evidence twice.
            var last = record.Samples.Count > 0 ? record.Samples[^1] : null;
            if (last is not null && last.AskedDay == askedDay)
            {
                last.Contributors = Math.Max(last.Contributors, contributors);
                last.Severity = Math.Max(last.Severity, entry.SeverityWeight);
                continue;
            }
            record.Samples.Add(new WatchSample
            {
                Day = day, AskedDay = askedDay,
                Contributors = contributors, Severity = entry.SeverityWeight,
            });
        }
    }

    /// <summary>
    /// D31. Record what the team is naming that sits outside the team.
    /// </summary>
    /// <remarks>
    /// Same shape as the watch, same one-sample-per-asked-day rule, and deliberately a
    /// different store. Keyed by issue file, because the outward bucket is and because the
    /// registry hands over one move per condition.
    /// </remarks>
    public void UpdateOutwardWatch(int day, WindowedTallies windowed)
    {
        if (!windowed.Tallies.TryGetValue("outward", out var bucket)) return;
        var required = windowed.Floor.Required;
        var askedDay = AskedDaysTo(day);

        foreach (var entry in bucket)
        {
            var contributors = entry.Contributors.Count;
            if (contributors < required) continue;
            var issueFile = entry.IssueFile;
            if (!OutwardWatch.TryGetValue(issueFile, out var record))
            {
                var file = Ctx.Data.IssueFile(issueFile);
                record = new WatchRecord { IssueFile = issueFile, Issue = file?.Issue ?? issueFile };
                OutwardWatch[issueFile] = record;
            }
            var last = record.Samples.Count > 0 ? record.Samples[^1] : null;
            if (last is not null && last.AskedDay == askedDay)
            {
                last.Contributors = Math.Max(last.Contributors, contributors);
                last.Severity = Math.Max(last.Severity, entry.SeverityWeight);
                continue;
            }
            record.Samples.Add(new WatchSample
            {
                Day = day, AskedDay = askedDay,
                Contributors = contributors, Severity = entry.SeverityWeight,
            });
        }
    }

    /// <summary>
    /// D31. What the team is naming that is not theirs to fix, as at <paramref name="day"/>,
    /// worst first.
    /// </summary>
    /// <remarks>
    /// Ordered as the watch is, and stale after the same ten of the team's own days. Each row
    /// carries the outward move and who it goes to, from the registry, because a condition
    /// named without a next step is the product shrugging.
    /// <para>
    /// Strength is in words only, the same rule as the watch and for the same reason: a
    /// headcount on a small team invites the manager to work out who.
    /// </para>
    /// </remarks>
    public List<OutwardEntry> OutwardList(int day, int limit = 2, int staleAfter = 10)
    {
        var required = Windowed(day).Floor.Required;
        var today = AskedDaysTo(day);
        var rows = new List<OutwardEntry>();

        foreach (var record in OutwardWatch.Values)
        {
            var seen = record.Samples.Where(x => x.Day <= day).ToList();
            if (seen.Count == 0) continue;
            var last = seen[^1];
            var since = today - last.AskedDay;
            if (since > staleAfter) continue;

            var consecutive = 1;
            for (var i = seen.Count - 1; i > 0; i -= 1)
            {
                if (seen[i].AskedDay - seen[i - 1].AskedDay != 1) break;
                consecutive += 1;
            }
            var peak = seen.Max(x => x.Contributors);
            var move = Ctx.Data.OutwardFor(record.IssueFile);

            rows.Add(new OutwardEntry(
                record.IssueFile,
                record.Issue,
                seen[0].Day,
                last.Day,
                seen.Count,
                consecutive,
                peak,
                Tally.SignalStrength(peak, required),
                seen.Max(x => x.Severity),
                since,
                move?.Action.Text,
                move?.Action.WorkedIf,
                move?.WhoItGoesTo));
        }

        // Votes lead, which is Clive's ruling and the rule everywhere else that orders
        // conditions. See the JavaScript for why this did not when it was first written.
        rows.Sort((a, b) =>
        {
            var c = b.PeakContributors.CompareTo(a.PeakContributors);
            if (c != 0) return c;
            c = b.Severity.CompareTo(a.Severity);
            if (c != 0) return c;
            c = b.Consecutive.CompareTo(a.Consecutive);
            if (c != 0) return c;
            c = b.DaysAbove.CompareTo(a.DaysAbove);
            if (c != 0) return c;
            return string.CompareOrdinal(a.IssueFile, b.IssueFile);
        });
        return rows.Take(limit).ToList();
    }

    /// <summary>The cycle that was open on <paramref name="day"/>, which is not always the one open now.</summary>
    public Cycle? CycleOpenOn(int day)
    {
        if (ActiveCycle is { } active && active.SelectedDay <= day) return active;
        foreach (var cycle in CycleHistory)
        {
            if (cycle.SelectedDay <= day && (cycle.ResolvedDay is null || cycle.ResolvedDay >= day))
            {
                return cycle;
            }
        }
        return null;
    }

    /// <summary>
    /// D28. What was building and not being worked on, as at <paramref name="day"/>, worst
    /// first.
    /// </summary>
    /// <remarks>
    /// Ordered the way selection orders: how many people, then severity, then persistence.
    /// <para>
    /// It led on severity to begin with, on the reasoning that the question a manager asks
    /// when something worse arrives mid-move is whether anything here is bigger. That
    /// reasoning still holds; the arithmetic does not support it. SeverityWeight is
    /// 0.5 x (votes + reds), so it is the contributor count with a thumb on Red rather than
    /// an independent measure of how bad something is, and leading on it ranked by very
    /// nearly the same quantity while reading as though it ranked by a different one. Worse,
    /// it could diverge from what the manager is actually handed next, which is the one thing
    /// this list must not do.
    /// </para>
    /// <para>
    /// Persistence stays in the order, below both, because it is the only thing here the
    /// engine does not already weigh and a condition reported for three weeks is a different
    /// fact from one reported on Tuesday.
    /// </para>
    /// <para>
    /// <paramref name="staleAfter"/> is in the team's own asked days. A condition last above
    /// the floor a month ago is history rather than a warning, and listing it would turn the
    /// watch list into the thing it exists to replace: a list long enough to ignore.
    /// </para>
    /// </remarks>
    public List<WatchEntry> WatchList(int day, int limit = 3, int staleAfter = 10)
    {
        var required = Windowed(day).Floor.Required;
        // The condition being worked on as at `day`, not the one being worked on now. Asking
        // the watch about an older week and excluding today's cycle would list that week's
        // own action as something nobody was acting on.
        var openFile = CycleOpenOn(day)?.IssueFile;
        var today = AskedDaysTo(day);
        var rows = new List<WatchEntry>();

        foreach (var record in ConditionWatch.Values)
        {
            if (record.IssueFile == openFile) continue;
            var seen = record.Samples.Where(x => x.Day <= day).ToList();
            if (seen.Count == 0) continue;

            var last = seen[^1];
            var since = today - last.AskedDay;
            if (since > staleAfter) continue;

            // The run of asked days ending at the last sighting. Weekends are not days this
            // team was asked anything, so they do not break a run.
            var consecutive = 1;
            for (var i = seen.Count - 1; i > 0; i -= 1)
            {
                if (seen[i].AskedDay - seen[i - 1].AskedDay != 1) break;
                consecutive += 1;
            }

            var peak = seen.Max(x => x.Contributors);
            rows.Add(new WatchEntry(
                record.IssueFile,
                record.Issue,
                seen[0].Day,
                last.Day,
                seen.Count,
                consecutive,
                peak,
                // In words as well as in figures, so a caller handing this to Eran cannot
                // leak a headcount by forgetting to convert it. The figure is for the
                // machinery panel.
                Tally.SignalStrength(peak, required),
                seen.Max(x => x.Severity),
                since));
        }

        rows.Sort((a, b) =>
        {
            var c = b.PeakContributors.CompareTo(a.PeakContributors);
            if (c != 0) return c;
            c = b.Severity.CompareTo(a.Severity);
            if (c != 0) return c;
            c = b.Consecutive.CompareTo(a.Consecutive);
            if (c != 0) return c;
            c = b.DaysAbove.CompareTo(a.DaysAbove);
            if (c != 0) return c;
            return string.CompareOrdinal(a.IssueFile, b.IssueFile);
        });
        return rows.Take(limit).ToList();
    }

    /// <summary>
    /// D29. Whether the manager can be given something new.
    /// </summary>
    /// <remarks>
    /// Free means they are not mid-move: either nothing is running, or what is running has
    /// been confirmed and is only waiting to be measured. One move at a time still holds,
    /// and it is now one move at a time of the manager's attention rather than of the
    /// engine's bookkeeping.
    /// </remarks>
    public bool SlotIsFree() => ActiveCycle is null || ActiveCycle.Confirmed;

    /// <summary>Every cycle, foreground or background, whose signal falls due today.</summary>
    public List<Cycle> DueReads(int day)
    {
        var due = new List<Cycle>();
        if (ActiveCycle is { } active && active.SignalReadDay == day) due.Add(active);
        foreach (var cycle in AwaitingRead)
        {
            if (cycle.SignalReadDay == day) due.Add(cycle);
        }
        return due;
    }

    /// <summary>Move a confirmed cycle out of the slot so something else can have it.</summary>
    public void ParkConfirmed()
    {
        if (ActiveCycle is { Confirmed: true } confirmed)
        {
            AwaitingRead.Add(confirmed);
            ActiveCycle = null;
        }
    }

    public string? LiveTally() => State is null ? null : EngineState.LiveTally.GetValueOrDefault(State);

    // -------------------------------------------------------------------- state

    /// <summary>
    /// Take a resolved state. Spec 7.1: the state is a RRIDA-level computation and this
    /// engine does not perform it. It is an input, not an output.
    /// </summary>
    /// <remarks>
    /// The engine's job is to emit the atom upward and consume whatever comes back. What
    /// produced the record is not the engine's business, which is why nothing here reaches
    /// for a resolver: the caller supplies it.
    /// </remarks>
    public StateApplication? ApplyState(StateApplication? record)
    {
        if (record is null) return null;
        State = record.State;
        LocusFilter = record.LocusFilter;
        Resolved = record.Resolved;
        StateHistory.Add(record);
        return record;
    }

    /// <summary>
    /// Spec 7.3, counted in the team's own days: seven calendar days hold five days of
    /// answers on a five-day week, and the rule is about having enough answers.
    /// </summary>
    public bool IsCalibrating(int day) => AskedDaysTo(day) <= Ctx.Config.CalibrationDays || State is null;

    // -------------------------------------------------------------------- cycle

    /// <summary>
    /// Try to make an action active. Succession (spec 8.2) is applied before selection
    /// (spec 6.4), as section 6.4 requires.
    /// </summary>
    /// <remarks>
    /// Returns a record of the attempt either way: "nothing cleared the floor" is a result
    /// the manager is told about, not a silence.
    /// </remarks>
    public SelectionAttempt AttemptSelection(int day)
    {
        var windowed = Windowed(day);
        var tallyName = LiveTally();
        var attempt = new SelectionAttempt
        {
            Day = day, State = State, LiveTally = tallyName, Windowed = windowed,
        };
        LastSelectionAttempt = attempt;

        if (tallyName is null)
        {
            attempt.Posture = "Calibrating. No state, so no tally is live.";
            return attempt;
        }

        var candidates = Tally.EligibleActions(windowed, tallyName);
        attempt.Eligible = candidates.ToList();

        // An action whose own cycle resolved inside this window is not handed back. The spec
        // does not cover this: 9.5 excludes nothing from future tallies, on the reasoning
        // that a chip can only win in a window where its question was asked, so the same
        // chip cannot win week after week. That holds once the votes age out, but the signal
        // read happens while they are still inside the seven days, so without this the
        // manager is handed the action they just closed, selected on the very votes it
        // closed on.
        //
        // Matched on the tally identity, not the delivered action: votes accumulate on the
        // ask action and the route is swapped at selection, so on the team and content
        // routes the action a cycle delivered is not the action its votes are filed under,
        // and comparing the delivered id would silently match nothing.
        var justResolved = CycleHistory
            .Where(c => c.ResolvedDay is { } r && r > windowed.From - 1)
            .SelectMany(c => new[] { c.TallyActionId, c.Rung1ActionId, c.Rung2ActionId })
            .Where(id => !string.IsNullOrEmpty(id))
            .ToHashSet(StringComparer.Ordinal);
        if (justResolved.Count > 0)
        {
            var suppressed = candidates.Where(c => justResolved.Contains(c.ActionId)).ToList();
            if (suppressed.Count > 0)
            {
                candidates = candidates.Where(c => !justResolved.Contains(c.ActionId)).ToList();
                attempt.Suppressed = suppressed;
            }
        }

        // D30. While the fortnightly cap is in force, a condition that could only be
        // delivered as a discovery session is not selected at all.
        //
        // Everything else the cap catches takes the team conversation instead, which is the
        // same move made smaller. An unlocated signal cannot: spec 4.6 sends it to a session
        // because there is nothing to tell the team yet and nobody to usefully ask, so a
        // conversation there is not a smaller move, it is the wrong one. The condition waits
        // instead, stays on the watch list, and the slot goes to something that can be acted
        // on today.
        if (LastSessionAskedDay is { } lastSession
            && AskedDaysTo(day) - lastSession < Route.SessionCadenceDays)
        {
            var discovery = candidates
                .Where(c => c.ChipContributors.Count > 0
                    && Route.MixOf(Ctx.Data, c.ChipContributors.Keys.ToList()).MajorityNeutral)
                .ToList();
            if (discovery.Count > 0)
            {
                var ids = discovery.Select(c => c.ActionId).ToHashSet(StringComparer.Ordinal);
                candidates = candidates.Where(c => !ids.Contains(c.ActionId)).ToList();
                attempt.CadenceHeld = discovery
                    .Select(c => new SelectionHold(
                        c.ActionId, c.IssueFile, c.Contributors.Count,
                        "this condition has not located a cause, so the only move for it is a "
                        + "session, and a session ran inside the last fortnight"))
                    .ToList();
            }
        }

        // D29. A condition still being measured cannot be selected again.
        //
        // Freeing the slot at confirmation opened this: the first cycle on a condition
        // confirms, the slot comes free, the condition is still above the floor because its
        // votes have not aged out, and it wins again while its own reading is still to come.
        // The manager gets the same move twice and the reading is then taken against a
        // baseline from a cycle that is no longer the only thing acting on it. One condition,
        // one cycle, until it resolves. The cycle still in the slot counts too when it is
        // confirmed and has a reading to come: that is the state selection now runs in, so
        // leaving it out suppressed nothing on the one day it mattered and the same condition
        // was handed over twice.
        var underMeasurement = new HashSet<string>(StringComparer.Ordinal);
        if (ActiveCycle is { Confirmed: true, SignalRead: null } stillInSlot)
        {
            underMeasurement.Add(stillInSlot.IssueFile);
        }
        foreach (var cycle in AwaitingRead) underMeasurement.Add(cycle.IssueFile);
        foreach (var cycle in PendingRungTwo) underMeasurement.Add(cycle.IssueFile);
        if (underMeasurement.Count > 0)
        {
            var held = candidates.Where(c => underMeasurement.Contains(c.IssueFile)).ToList();
            if (held.Count > 0)
            {
                candidates = candidates
                    .Where(c => !underMeasurement.Contains(c.IssueFile)).ToList();
                attempt.AwaitingRead = held
                    .Select(c => new SelectionHold(
                        c.ActionId, c.IssueFile, c.Contributors.Count,
                        "a cycle on this condition is confirmed and still waiting to be read"))
                    .ToList();
            }
        }

        if (PendingSuccessionFile is { } file)
        {
            var spread = Tally.FileQuestionSpread(windowed, file);
            var sameFile = candidates.Where(c => c.IssueFile == file).ToList();
            var mechanisms = new List<string>();
            foreach (var candidate in sameFile)
            {
                var trigger = Tally.Triggering(Ctx, candidate);
                if (trigger is not null && !mechanisms.Contains(trigger.Chip.Rung2)) mechanisms.Add(trigger.Chip.Rung2);
            }
            var remaining = mechanisms.Where(m => !InstalledMechanisms.Contains(m)).ToList();
            var exhausted = mechanisms.Count > 0 && remaining.Count == 0;
            var inForce = sameFile.Count > 0 && spread.DrawsFromMoreThanOne && !exhausted;

            attempt.Succession = new SuccessionRecord
            {
                HeldFile = file,
                Issue = Ctx.Data.IssueFile(file)?.Issue,
                EligibleInFile = sameFile.Count,
                QuestionSpread = spread,
                MechanismsReachable = mechanisms,
                MechanismsInstalled = InstalledMechanisms.OrderBy(m => m, StringComparer.Ordinal).ToList(),
                MechanismsRemaining = remaining,
                Exhausted = exhausted,
                InForce = inForce,
                Reason = inForce
                    ? $"held: {file} still has an eligible action and draws from {spread.Count} of the week's questions"
                    : sameFile.Count == 0
                        ? $"released: {file} has no eligible action in this window"
                        : !spread.DrawsFromMoreThanOne
                            ? $"released: {file} draws from one question only, so it is that question's topic rather than a standing condition"
                            : $"released: {file} is exhausted, every mechanism behind its eligible actions is already installed",
            };
            if (inForce) { candidates = sameFile; HeldIssueFile = file; }
            else { PendingSuccessionFile = null; HeldIssueFile = null; }
        }

        var selection = Tally.SelectAction(candidates);
        attempt.Selection = selection;

        if (selection is null)
        {
            attempt.Posture = EngineState.NoFloorPosture.GetValueOrDefault(State ?? "");
            return attempt;
        }

        attempt.Cleared = true;
        // D29. Whatever was in the slot is confirmed by now or selection would not have run,
        // and it still has a reading to come. It goes behind, not away.
        ParkConfirmed();
        ActiveCycle = OpenCycle(day, selection.Winner, attempt);

        // D30. A session delivered today is what the next fortnight's cap measures from. Set
        // here rather than inside OpenCycle because this is the only path that delivers one,
        // and a rung two is a mechanism rather than a session.
        if (ActiveCycle.Routing.Route == "content") LastSessionAskedDay = AskedDaysTo(day);
        return attempt;
    }

    private Cycle OpenCycle(int day, TallyEntry entry, SelectionAttempt attempt)
    {
        // The triggering chip is the one the signal is read against, so it has to be a chip
        // that is still being selected on the most recent asking of its question. Picking
        // purely on window-wide contributors can land on a chip whose votes all came from an
        // earlier occurrence, which gives a baseline of zero that can never fall by one.
        var trigger = ChooseTriggeringChip(entry, day, attempt.Windowed.From);
        var chip = trigger.Chip;

        // The route swap. Votes accumulated on the ask action; now the winner is known, the
        // contributor share picks which of the three rung one actions is actually delivered
        // (spec 5.1.1). It happens exactly once, here, and is fixed for the cycle: if the
        // share moves mid-cycle the route does not, because the manager has already been
        // handed a move.
        var routing = Route.Select(
            Ctx, chip,
            contributors: entry.Contributors.Count,
            enrolled: attempt.Windowed.Floor.Enrolled,
            history: WorksheetHistory,
            declined: DeclinedActions,
            // Spec 4.6 turns on the chips that converged on the winning action, not on the
            // triggering chip alone: a concentration of neutral across the contributors is
            // the finding, and one neutral trigger among located chips is not.
            contributingChipIds: entry.ChipOrder,
            // D30. The cadence cap, in the team's own days.
            lastSessionAskedDay: LastSessionAskedDay,
            askedDayNow: AskedDaysTo(day));

        var rung1 = Ctx.Data.Action(routing.ActionId)!;
        var rung2 = Ctx.Data.Action(chip.Rung2)!;
        var baselineDay = trigger.BaselineDay;
        // D27. The baseline is how full the bucket was, not how many picked one tap. It has
        // to be measured the same way the read will be, or the comparison is between two
        // different quantities and the difference means nothing.
        var baseline = ReadFileOnDay(rung1.IssueFile, baselineDay, chip.Track);

        var cycle = new Cycle
        {
            Id = Scope.NextCycleId(),
            TeamId = Id,
            Index = CycleHistory.Count + 1,
            IssueFile = rung1.IssueFile,
            Issue = rung1.Issue,
            Rung1ActionId = rung1.Id,
            Rung2ActionId = rung2.Id,
            ActionId = rung1.Id,
            TallyActionId = entry.ActionId,
            Routing = routing,
            TriggeringChipId = chip.Id,
            TriggeringChipLabel = chip.Label,
            TriggeringQuestionId = chip.QuestionId,
            TriggeringQuestionText = chip.QuestionText,
            BaselineDay = baselineDay,
            Baseline = baseline,
            SelectedDay = day,
            DeliveredDay = day,
            ConfirmationDueDay = AskedDaysAfter(day, Ctx.Config.ConfirmationLag),
            TallyVotes = entry.Votes,
            TallyContributors = entry.Contributors.ToList(),
            TallySeverityWeight = entry.SeverityWeight,
            TallyQuestionIds = entry.QuestionIds.ToList(),
        };
        cycle.ReadTrack = chip.Track;
        var readAt = Ctx.Rotation.NextReadDay(
            rung1.IssueFile,
            Calendar.RotationDay(this, day) + Ctx.Config.EarliestReadOffset,
            chip.Track);
        cycle.SignalReadDay = readAt is null ? null : CalendarDayForAsked(readAt.Value);

        // A worksheet counts as delivered the moment the cycle opens, and as completed only
        // if that cycle closes. A team that was handed V1 and got nowhere with it has not met
        // V1 as a prerequisite for V3. Where a worksheet comes round a second time the
        // earlier closure stands: it was run once, and running it again does not un-run it.
        foreach (var worksheet in routing.WorksheetsDelivered)
        {
            WorksheetHistory.TryGetValue(worksheet.Code, out var existing);
            WorksheetHistory[worksheet.Code] = new WorksheetRecord
            {
                Code = worksheet.Code,
                Title = worksheet.Title,
                DeliveredDay = day,
                FirstDeliveredDay = existing?.FirstDeliveredDay ?? day,
                CycleId = cycle.Id,
                Closed = existing is { Closed: true },
                ClosedDay = existing?.ClosedDay,
            };
        }
        return cycle;
    }

    private sealed record TriggerCandidate(string ChipId, Chip Chip, int BaselineDay, int OnBaseline, int WindowContributors);

    /// <summary>
    /// Pick the chip the signal will be read against, from the chips that fed the winning action.
    /// </summary>
    /// <remarks>
    /// Ranked on contributors present at the most recent asking of that chip's question,
    /// then on window-wide contributors, then on chip id so runs are reproducible.
    /// </remarks>
    public TriggerCandidateResult ChooseTriggeringChip(TallyEntry entry, int day, int windowFrom)
    {
        var candidates = new List<TriggerCandidate>();
        foreach (var chipId in entry.ChipOrder)
        {
            var members = entry.ChipContributors[chipId];
            var chip = Ctx.Data.Chip(chipId)!;
            // D24 amended. OccurrencesBetween answers in rotation space, which is now the
            // team's own asked-day index, so the window has to be handed over in that space
            // and the answer brought back out of it. Fed calendar days it returned a rotation
            // day that was then read as a date, and the baseline was taken on the wrong day.
            var occurrences = Ctx.Rotation.OccurrencesBetween(
                chip.QuestionId, Calendar.RotationDay(this, windowFrom), Calendar.RotationDay(this, day));
            var lastOccurrence = occurrences.Count > 0 ? occurrences[^1] : Calendar.RotationDay(this, day);
            var baselineDay = CalendarDayForAsked(lastOccurrence);
            var onBaseline = ReadChipOnDay(chipId, chip.QuestionId, baselineDay).Contributors;
            candidates.Add(new TriggerCandidate(chipId, chip, baselineDay, onBaseline, members.Count));
        }
        candidates.Sort((a, b) =>
        {
            if (b.OnBaseline != a.OnBaseline) return b.OnBaseline - a.OnBaseline;
            if (b.WindowContributors != a.WindowContributors) return b.WindowContributors - a.WindowContributors;
            return string.CompareOrdinal(a.ChipId, b.ChipId);
        });
        var best = candidates[0];
        return new TriggerCandidateResult(best.ChipId, best.Chip, best.BaselineDay, best.OnBaseline, best.WindowContributors);
    }

    public sealed record TriggerCandidateResult(string ChipId, Chip Chip, int BaselineDay, int OnBaseline, int WindowContributors);

    /// <summary>Distinct contributors selecting one chip on one day, plus that day's mean polarity.</summary>
    /// <summary>
    /// How full one issue file was on one day, plus that day's mean polarity.
    /// </summary>
    /// <remarks>
    /// D27. A member counts once however many of the day's chips they picked into the file,
    /// because the question is whether this condition is still being reported and not how
    /// many ways they found to say it. The day's question is whichever the rotation served,
    /// so the read and the baseline are usually different questions filling the same bucket:
    /// that is the point, and the question id is carried out so the comparison can say so.
    /// </remarks>
    public ChipRead ReadFileOnDay(string issueFile, int day, string track = "individual")
    {
        var responses = ResponsesBetween(day, day, track);
        var contributors = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var response in responses)
        {
            foreach (var chipId in response.Chips)
            {
                var chip = Ctx.Data.Chip(chipId);
                if (chip is not null && chip.IssueFile == issueFile)
                {
                    if (seen.Add(response.MemberId)) contributors.Add(response.MemberId);
                    break;
                }
            }
        }
        var indices = responses.Select(r => Vocabulary.SmileyIndex(r.Smiley)).ToList();
        double? mean = indices.Count > 0 ? (double)indices.Sum() / indices.Count : null;
        return new ChipRead
        {
            Day = day,
            IssueFile = issueFile,
            Contributors = contributors.Count,
            Members = contributors,
            QuestionId = responses.Count > 0 ? responses[0].QuestionId : null,
            Responses = responses.Count,
            MeanSmileyIndex = mean,
        };
    }

    /// <summary>Distinct contributors selecting one chip on one day, plus that day's mean polarity.</summary>
    public ChipRead ReadChipOnDay(string chipId, int questionId, int day)
    {
        var responses = ResponsesBetween(day, day).Where(r => r.QuestionId == questionId).ToList();
        var contributors = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var response in responses)
        {
            if (response.Chips.Contains(chipId) && seen.Add(response.MemberId)) contributors.Add(response.MemberId);
        }
        var indices = responses.Select(r => Vocabulary.SmileyIndex(r.Smiley)).ToList();
        double? mean = indices.Count > 0 ? (double)indices.Sum() / indices.Count : null;
        return new ChipRead
        {
            Day = day,
            Contributors = contributors.Count,
            Members = contributors,
            Responses = responses.Count,
            MeanSmileyIndex = mean,
        };
    }

    /// <summary>The manager's one tap (spec 9.2). Nothing goes to the team at this point.</summary>
    public Cycle? Confirm(int day, bool workedIfMet, string source = "manager")
    {
        var cycle = ActiveCycle;
        if (cycle is null || cycle.Confirmed) return null;
        cycle.Confirmed = true;
        cycle.ConfirmedDay = day;
        cycle.WorkedIfMet = workedIfMet;
        cycle.ConfirmationSource = source;
        return cycle;
    }

    public (int? DayOffset, bool WorkedIfMet)? ScriptedConfirmationFor(int cycleIndex, int rung)
    {
        foreach (var c in _scriptedConfirmations)
        {
            if (c.CycleIndex == cycleIndex && c.Rung == rung) return (c.DayOffset, c.WorkedIfMet);
        }
        return _defaultConfirmation;
    }

    /// <summary>
    /// The manager declines a contraindicated worksheet (spec 5.1.3).
    /// </summary>
    /// <remarks>
    /// The layer states the condition and does not evaluate it. It does not know who is in a
    /// performance process, it has no way to find out, and going looking would be worse than
    /// the exercise it is trying to avoid. A decline is a failed cycle rather than a
    /// cancelled one, because from the condition's point of view nothing has been done.
    /// </remarks>
    public Cycle? DeclineContent(int day, string? reason)
    {
        var cycle = ActiveCycle;
        if (cycle is null) return null;
        if (cycle.Routing.Route != "content") return null;
        if (cycle.Confirmed) return null;

        DeclinedActions.Add(cycle.Rung1ActionId);
        cycle.Declined = new Decline
        {
            Day = day,
            Reason = reason ?? "the manager judged one of the stated conditions to apply",
            Worksheets = cycle.Routing.WorksheetsDelivered.Select(w => w.Code).ToList(),
        };

        // The team never saw it, so it is not history. Leaving the record in would tell a
        // later prerequisite check that the team has run something it has not.
        foreach (var worksheet in cycle.Routing.WorksheetsDelivered)
        {
            if (WorksheetHistory.TryGetValue(worksheet.Code, out var record)
                && record.CycleId == cycle.Id && !record.Closed)
            {
                WorksheetHistory.Remove(worksheet.Code);
            }
        }

        return CloseOut(day, "failed");
    }

    /// <summary>
    /// Read the signal (spec 9.3) and resolve the cycle (spec 9.4, 9.5).
    /// </summary>
    public Resolution? ResolveCycle(int day, Cycle? target = null)
    {
        var cycle = target ?? ActiveCycle;
        if (cycle is null || cycle.SignalReadDay != day) return null;

        var floor = Windowed(day).Floor;
        var read = ReadFileOnDay(cycle.IssueFile, day, cycle.ReadTrack);

        // D23. A read needs something to read.
        //
        // Nobody selecting the chip and nobody being asked produce the same contributor
        // count, and the count alone cannot tell them apart. Read as a fall, an unanswered
        // day closes the cycle and reports to the manager that what they did worked, on the
        // strength of a measurement that was never taken. Measured on the eight scenarios
        // with weekends stripped out: 24 reads landed on a day with no answers and all 24
        // read as the signal having moved.
        if (read.Responses == 0)
        {
            cycle.ReadDeferrals += 1;
            cycle.ReadDeferredFrom.Add(day);
            if (cycle.ReadDeferrals <= Ctx.Config.MaxReadDeferrals)
            {
                var next = Ctx.Rotation.NextReadDay(
                    cycle.IssueFile, Calendar.RotationDay(this, day) + 1, cycle.ReadTrack);
                cycle.SignalReadDay = next is null ? null : CalendarDayForAsked(next.Value);
                return null;
            }
            cycle.SignalRead = new SignalRead
            {
                Day = day, Baseline = cycle.Baseline!, Read = read, FloorRequired = floor.Required,
                FellByOne = false, AlreadyAtFloor = false, PolarityStep = 0, PolarityImproved = false,
                Moved = false, Unread = true, HowRead = null,
            };
            return new Resolution
            {
                Cycle = cycle,
                Read = cycle.SignalRead,
                ClosureConditions = new Dictionary<string, bool>(StringComparer.Ordinal)
                {
                    ["manager confirmed the action was taken"] = cycle.Confirmed,
                    ["the rung's worked-if condition is met"] = cycle.WorkedIfMet == true,
                    ["the data signal moved at the next occurrence"] = false,
                },
                Closes = false,
                Outcome = "unread",
                NextStep = "cannot be read",
            };
        }

        var fellByOne = read.Contributors <= cycle.Baseline!.Contributors - 1;
        var alreadyAtFloor = cycle.Baseline.Contributors <= floor.Required;
        var polarityStep = read.MeanSmileyIndex is { } r && cycle.Baseline.MeanSmileyIndex is { } b
            ? r - b : 0;
        var polarityImproved = alreadyAtFloor && polarityStep >= 1;
        var moved = fellByOne || polarityImproved;

        cycle.SignalRead = new SignalRead
        {
            Day = day, Baseline = cycle.Baseline, Read = read, FloorRequired = floor.Required,
            FellByOne = fellByOne, AlreadyAtFloor = alreadyAtFloor, PolarityStep = polarityStep,
            PolarityImproved = polarityImproved, Moved = moved,
            HowRead = fellByOne ? "fewer people selected it"
                : polarityImproved ? "polarity improved" : null,
        };

        var closureConditions = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["manager confirmed the action was taken"] = cycle.Confirmed,
            ["the rung's worked-if condition is met"] = cycle.WorkedIfMet == true,
            ["the data signal moved at the next occurrence"] = moved,
        };
        var closes = closureConditions.Values.All(v => v);

        string outcome;
        string nextStep;
        if (closes)
        {
            outcome = "closed";
            nextStep = "close";
        }
        else if (!cycle.Confirmed)
        {
            // The system cannot attribute the change, so the cycle does not close (spec 9.4).
            outcome = "cannot_attribute";
            nextStep = "cannot attribute";
        }
        else if (cycle.Rung == 2)
        {
            outcome = "failed";
            nextStep = "fail";
        }
        else if (InstalledMechanisms.Contains(cycle.Rung2ActionId))
        {
            // Rung two is skipped where that mechanism has already closed on this file
            // during this run (spec 9.4.3), and the cycle fails on rung one.
            outcome = "failed";
            nextStep = "fail";
            cycle.Rung2Skipped = true;
            cycle.Rung2SkipReason =
                $"{cycle.Rung2ActionId} already closed for this team on {cycle.IssueFile} during this run";
        }
        else
        {
            outcome = "rung_two";
            nextStep = "deliver rung two";
        }

        return new Resolution
        {
            Cycle = cycle, Read = cycle.SignalRead, ClosureConditions = closureConditions,
            Closes = closes, Outcome = outcome, NextStep = nextStep,
        };
    }

    /// <summary>Move the cycle onto rung two: a new action, a new worked-if, a new read.</summary>
    public Cycle AdvanceToRungTwo(int day, Cycle? target = null)
    {
        var cycle = target ?? ActiveCycle!;
        var rung2 = Ctx.Data.Action(cycle.Rung2ActionId)!;
        cycle.RungOneRecord = new RungOneRecord
        {
            ActionId = cycle.Rung1ActionId,
            Confirmed = cycle.Confirmed,
            ConfirmedDay = cycle.ConfirmedDay,
            WorkedIfMet = cycle.WorkedIfMet,
            SignalRead = cycle.SignalRead,
        };
        cycle.Rung = 2;
        cycle.ActionId = rung2.Id;
        cycle.DeliveredDay = day;
        cycle.ConfirmationDueDay = AskedDaysAfter(day, Ctx.Config.ConfirmationLag);
        cycle.Confirmed = false;
        cycle.ConfirmedDay = null;
        cycle.WorkedIfMet = null;
        cycle.BaselineDay = day;
        cycle.Baseline = ReadFileOnDay(cycle.IssueFile, day, cycle.ReadTrack);
        cycle.SignalRead = null;
        var next = Ctx.Rotation.NextReadDay(
            cycle.IssueFile,
            Calendar.RotationDay(this, day) + Ctx.Config.EarliestReadOffset,
            cycle.ReadTrack);
        cycle.SignalReadDay = next is null ? null : CalendarDayForAsked(next.Value);
        // D29. Rung two is a move for the manager, so it takes the slot. If it came from a
        // background reading, whatever was in the slot is confirmed and goes behind it.
        if (!ReferenceEquals(ActiveCycle, cycle))
        {
            AwaitingRead.Remove(cycle);
            PendingRungTwo.Remove(cycle);
            ParkConfirmed();
            ActiveCycle = cycle;
        }
        return cycle;
    }

    /// <summary>Retire the cycle, install any mechanism, and set succession up for the next draw.</summary>
    public Cycle CloseOut(int day, string outcome, Cycle? target = null)
    {
        var cycle = target ?? ActiveCycle!;
        cycle.Outcome = outcome;
        cycle.ResolvedDay = day;

        if (outcome == "closed" && cycle.Rung == 2) InstalledMechanisms.Add(cycle.Rung2ActionId);

        // The cycle is the unit a prerequisite is measured in, so a cycle that carried a
        // worksheet and closed marks that worksheet complete, whichever rung it closed on.
        if (outcome == "closed")
        {
            foreach (var worksheet in cycle.Routing.WorksheetsDelivered)
            {
                if (WorksheetHistory.TryGetValue(worksheet.Code, out var record) && !record.Closed)
                {
                    record.Closed = true;
                    record.ClosedDay = day;
                }
            }
        }

        if (outcome == "failed")
        {
            if (!FailedFiles.TryGetValue(cycle.IssueFile, out var record))
            {
                record = new FailedFile { IssueFile = cycle.IssueFile, Issue = cycle.Issue, FirstDay = day, LastDay = day };
                FailedFiles[cycle.IssueFile] = record;
            }
            record.Count += 1;
            record.LastDay = day;
            // A declined action failed for a stated reason rather than for want of a signal,
            // and the escalation a manager eventually reads should say which.
            if (cycle.Declined is { } declined)
            {
                record.Declines.Add((day, cycle.Rung1ActionId, declined.Reason));
            }
            // Ids for the succession rule, and alongside them what each one actually
            // delivered. The description is captured here, while the cycle is still in hand:
            // an escalation fires days later with nothing but ids, and resolving a
            // substituted content action back to its text at that point names the exercise
            // the team was never given (spec 5.1.4).
            foreach (var rung in new[] { 1, 2 })
            {
                var id = rung == 1 ? cycle.Rung1ActionId : cycle.Rung2ActionId;
                if (cycle.Rung == 1 && rung == 2) continue; // rung two was never delivered
                if (!record.ActionsTried.Contains(id)) record.ActionsTried.Add(id);
                record.Delivered[id] = Route.DeliveredActionText(Ctx, cycle, rung);
            }
        }

        CycleHistory.Add(cycle);
        // D29. Out of whichever hold it was in. A background cycle closing must not clear
        // the slot, or the move the manager is in the middle of disappears from under them.
        if (ReferenceEquals(ActiveCycle, cycle)) ActiveCycle = null;
        AwaitingRead.Remove(cycle);
        PendingRungTwo.Remove(cycle);
        PendingSuccessionFile = cycle.IssueFile;
        return cycle;
    }

    /// <summary>
    /// A file that has produced a failed cycle and then draws again goes to the manager as a
    /// conversation, not another action from the same file (spec 9.5).
    /// </summary>
    /// <remarks>
    /// Suppressing the file would hide exactly the signal that matters most, so the draw
    /// still happens.
    /// </remarks>
    public Escalation? CheckEscalation(int day, string issueFile)
    {
        if (!FailedFiles.TryGetValue(issueFile, out var record) || record.Count < 1) return null;
        if (Escalations.Any(e => e.IssueFile == issueFile && e.DrawCount == record.Count)) return null;
        var escalation = new Escalation
        {
            Day = day,
            IssueFile = issueFile,
            Issue = record.Issue,
            FailedCycles = record.Count,
            DrawCount = record.Count,
            ActionsTried = record.ActionsTried.ToList(),
            // An id that predates the delivered record falls back to its authored text,
            // which is correct for everything that was never substituted.
            ActionsTriedText = record.ActionsTried
                .Select(id => record.Delivered.GetValueOrDefault(id) ?? Ctx.Data.Action(id)!.Text)
                .ToList(),
            WeeksSpan = Math.Max(1, (int)Math.Ceiling((day - record.FirstDay + 1) / 7.0)),
        };
        Escalations.Add(escalation);
        record.Escalated = true;
        return escalation;
    }

    public CycleCounts CycleCountsOf()
    {
        var counts = new CycleCounts { Running = ActiveCycle is not null ? 1 : 0 };
        foreach (var cycle in CycleHistory)
        {
            switch (cycle.Outcome)
            {
                case "closed": counts.Closed += 1; break;
                case "failed": counts.Failed += 1; break;
                case "cannot_attribute": counts.Unattributed += 1; break;
                // Counted separately because it is a different fact. An unattributed cycle
                // was read and the manager never confirmed; an unread one was never read at
                // all, and filing the two together would hide a team that has stopped
                // answering.
                case "unread": counts.Unread += 1; break;
            }
        }
        return counts;
    }
}
