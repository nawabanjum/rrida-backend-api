using Rrida.Engine;

namespace Rrida.State;

/// <summary>Both limbs for one track on one day, with the answers behind them.</summary>
public sealed class TrackReading
{
    public required RelativeReading Relative { get; init; }
    public required AbsoluteReading Absolute { get; init; }
    public required int Answers { get; init; }
    public required int Negatives { get; init; }
}

/// <summary>
/// One day's resolved state for one team. Two axes and a flag, never one label (D19).
/// </summary>
public sealed class ResolvedRecord
{
    public required int Day { get; init; }

    /// <summary>cruise (gate closed), drift (gate open) or stall (D15).</summary>
    public required string Vertical { get; init; }

    /// <summary>unlocated, internal or external, from the section H classifier.</summary>
    public required string Horizontal { get; init; }

    /// <summary>
    /// Raised whenever the horizontal answer is external, <b>regardless of the vertical</b>.
    /// A team can be Stall and flagged Headwinds at once; on a large team with external
    /// causes that is the usual condition rather than a contradiction. The vertical axis
    /// says how bad it is and the horizontal says whose problem it is.
    /// </summary>
    public required bool HeadwindsFlag { get; init; }

    /// <summary>D18. Which limb opened the gate: relative, absolute or both. Null where shut.</summary>
    public required string? Limb { get; init; }

    /// <summary>Which track or tracks it opened on. Null where shut.</summary>
    public required List<string>? Track { get; init; }

    public required int? OpenedDay { get; init; }
    public required ConcentrationReading Concentration { get; init; }

    /// <summary>
    /// D21. Handed down with the state so the engine has no default and no way to decide it.
    /// </summary>
    public required LocusFilter? LocusFilter { get; init; }

    public required Dictionary<string, Dictionary<string, Dictionary<string, bool>>> Watch { get; init; }
    public required bool WatchExposed { get; init; }
    public required Dictionary<string, TrackReading> Tracks { get; init; }
    public required ClassifierReading Classifier { get; init; }
}

/// <summary>Where the records go. The resolver produces them and does not decide where.</summary>
public interface IStateSink
{
    void Write(string teamId, ResolvedRecord record);
}

/// <summary>
/// The resolved state. D13 to D22, provisional and pending ratification.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is not part of the engine, and that is the point.</b> Spec 7.1 draws the single
/// most important boundary in the document: the team state is a RRIDA-level computation,
/// and the engine emits the atom and consumes a resolved state as an input. This assembly
/// references the engine and is never referenced by it, which a test asserts. Swap it for a
/// call to the real service and nothing in the engine changes.
/// </para>
/// <para>
/// <b>Identities never leave here.</b> Member ids are used to compute the concentration
/// share and go no further; the reading that leaves is a word. A test asserts it.
/// </para>
/// <para>
/// Nothing here carries a default. Every setting arrives by injection and the constructor
/// throws without it.
/// </para>
/// </remarks>
public sealed class Resolver : IStateSource
{
    private sealed class TeamState
    {
        public int Day;
        public required Dictionary<string, (RelativeAccumulator Relative, AbsoluteAccumulator Absolute)> Limbs { get; init; }
        public required List<string> Tracks { get; init; }
        public required Classifier Classifier { get; init; }
        public Dictionary<string, TrackReading> Last { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// The day's observation per track, kept alongside the reading because D16's window
        /// needs the per-member levels the accumulator reduced away. Per team, not per
        /// resolver: one resolver serves every team and sharing this would attribute one
        /// team's answers to another.
        /// </summary>
        public Dictionary<string, DayObservation> LastObs { get; } = new(StringComparer.Ordinal);

        public List<Concentration.WindowDay> Window { get; } = [];
        public bool GateOpen;
        public (int Day, string Limb, List<string> Tracks)? Opening;
        public List<ResolvedRecord> Records { get; } = [];
        public string? LastState;
    }

    private readonly StateConfig _config;
    private readonly Dictionary<string, TeamState> _states = new(StringComparer.Ordinal);
    private readonly List<Action<Team, ResolvedRecord>> _listeners = [];
    private readonly Dictionary<string, double> _levelOf;

    public StateConfig Config => _config;

    public Resolver(StateConfig config, IStateSink? sink = null)
    {
        _config = config ?? throw new ArgumentNullException(
            nameof(config),
            "Resolver needs its config. Every threshold is a decision about what the product calls "
            + "trouble, and this assembly holds no defaults.");
        // Task 2. One record per team per day, from day one. The sink is injected like
        // everything else: the resolver produces records and does not decide where they go.
        if (sink is not null) _listeners.Add((team, record) => sink.Write(team.Id, record));

        _levelOf = new Dictionary<string, double>(StringComparer.Ordinal);
        for (var i = 0; i < Vocabulary.SmileyOrder.Length; i += 1)
        {
            _levelOf[Vocabulary.SmileyOrder[i]] = _config.LevelScale[i];
        }
    }

    public void OnRecord(Action<Team, ResolvedRecord> listener) => _listeners.Add(listener);

    public void Reset() => _states.Clear();

    public IReadOnlyList<ResolvedRecord> History(string teamId) =>
        _states.TryGetValue(teamId, out var state) ? state.Records : [];

    /// <summary>
    /// Which days carry the weekly manager-facing message.
    /// </summary>
    /// <remarks>
    /// D20: this governs the message and nothing else. The state is resolved and applied
    /// every day. An earlier version used this predicate for both, which is why a gate
    /// opening could sit unapplied for up to six days.
    /// </remarks>
    public static bool IsRecalcDay(int day, EngineConfig config) =>
        day >= config.FirstMessageDay && (day - config.FirstMessageDay) % config.RecalcInterval == 0;

    private TeamState StateFor(EngineContext ctx, Team team)
    {
        if (_states.TryGetValue(team.Id, out var existing)) return existing;
        var tracks = ctx.Rotation.TracksAsked.ToList();
        var limbs = new Dictionary<string, (RelativeAccumulator, AbsoluteAccumulator)>(StringComparer.Ordinal);
        foreach (var track in tracks)
        {
            limbs[track] = (new RelativeAccumulator(_config.Relative), new AbsoluteAccumulator(_config.Absolute));
        }
        var state = new TeamState
        {
            Limbs = limbs,
            Tracks = tracks,
            Classifier = new Classifier(
                Classifier.BuildProfile(ctx.Data, _config.Classifier.Track), _config.Classifier),
        };
        _states[team.Id] = state;
        return state;
    }

    /// <summary>One track's answers for one day, reduced to what the accumulators need.</summary>
    private DayObservation Observe(IReadOnlyList<Response> responses)
    {
        double levelSum = 0;
        double levelSqSum = 0;
        var negatives = 0;
        var perMember = new List<(string, double)>();
        foreach (var response in responses)
        {
            var level = _levelOf[response.Smiley];
            levelSum += level;
            levelSqSum += level * level;
            if (Vocabulary.IsNegative(response.Smiley)) negatives += 1;
            perMember.Add((response.MemberId, level));
        }
        return new DayObservation
        {
            Answers = responses.Count,
            LevelSum = levelSum,
            LevelSqSum = levelSqSum,
            Negatives = negatives,
            PerMember = perMember,
        };
    }

    /// <summary>
    /// Advance one team by one day and return the day's record.
    /// </summary>
    /// <remarks>
    /// The accumulators are recurrences, so a day is fed once and only once. Feeding the
    /// same day twice would double-count it, which is why the caller advances through every
    /// day rather than jumping to the one it wants.
    /// </remarks>
    private ResolvedRecord Advance(EngineContext ctx, Team team, TeamState state, int day)
    {
        foreach (var track in state.Tracks)
        {
            var obs = Observe(team.ResponsesBetween(day, day, track));
            var (relativeLimb, absoluteLimb) = state.Limbs[track];
            // The gate's state entering the day is what freezes the relative baseline.
            var relative = relativeLimb.Push(day, obs, state.GateOpen);
            var absolute = absoluteLimb.Push(day, obs);
            state.Last[track] = new TrackReading
            {
                Relative = relative, Absolute = absolute,
                Answers = obs.Answers, Negatives = obs.Negatives,
            };
            state.LastObs[track] = obs;
        }

        var relOpen = state.Tracks.Where(t => state.Last[t].Relative.Open).ToList();
        var absOpen = state.Tracks.Where(t => state.Last[t].Absolute.Open).ToList();
        var gateOpen = relOpen.Count > 0 || absOpen.Count > 0;

        // D18. Which limb opened it, and on which track. Recorded once, at the opening.
        if (gateOpen && state.Opening is null)
        {
            var limb = relOpen.Count > 0 && absOpen.Count > 0 ? "both"
                : relOpen.Count > 0 ? "relative" : "absolute";
            var opened = new List<string>();
            foreach (var t in relOpen.Concat(absOpen)) if (!opened.Contains(t)) opened.Add(t);
            state.Opening = (day, limb, opened);
        }
        if (!gateOpen) state.Opening = null;
        state.GateOpen = gateOpen;

        // The horizontal axis, on the track section H ran on.
        var classTrack = _config.Classifier.Track;
        var classAnswers = state.Tracks.Contains(classTrack)
            ? team.ResponsesBetween(day, day, classTrack).Select(r => new ClassifierAnswer(
                Vocabulary.IsNegative(r.Smiley),
                r.Chips.Select(ctx.Data.Chip).OfType<Chip>()
                    .Select(c => c.Locus)
                    .Where(l => l is "internal" or "external")
                    .OfType<string>()
                    .Distinct(StringComparer.Ordinal)
                    .ToList())).ToList()
            : [];
        // D24 amended. The rotation answers in the team's own asked-day index. Handed a
        // calendar day it named the wrong question on every day of a five-day week, and the
        // question is what the classifier's locus profile is keyed on: it says how often an
        // internal or external chip is chosen on that question, which is the expected rate
        // the z statistic is measured against. Wrong question, wrong expectation, wrong
        // horizontal. This is the one crossing the state layer is permitted to make.
        var questionId = state.Tracks.Contains(classTrack)
            ? ctx.Rotation.QuestionIdForDay(Calendar.RotationDay(team, day), classTrack)
            : null;
        var classified = state.Classifier.Push(day, questionId, classAnswers, gateOpen);

        // D16's window trails the gate, so it is kept whether or not the gate is open.
        var primary = state.Tracks.Contains("individual") ? "individual" : state.Tracks[0];
        state.Window.Add(new Concentration.WindowDay(
            day, state.Last[primary].Relative.BaselineValue, state.LastObs[primary].PerMember));
        if (state.Window.Count > _config.Concentration.WindowDays) state.Window.RemoveAt(0);

        // D19. Two axes and a flag. No precedence label.
        // D15. Stall is the absolute accumulator open on BOTH tracks. With only one track
        // asked there is no "both", so Stall is unreachable and the state says drift rather
        // than quietly treating one track as two.
        var stall = state.Tracks.Count >= 2 && absOpen.Count == state.Tracks.Count;
        var vertical = !gateOpen ? "cruise" : stall ? "stall" : "drift";
        var horizontal = classified.Horizontal;
        var headwindsFlag = horizontal == "external";
        var concentration = gateOpen
            ? Concentration.Read(state.Window, _config.Concentration, _config.Relative.K)
            : new ConcentrationReading
            {
                Reading = "unknown", TopShare = null,
                Basis = "the gate is closed, so nothing is being attributed",
            };

        state.Day = day;
        return new ResolvedRecord
        {
            Day = day,
            Vertical = vertical,
            Horizontal = horizontal,
            HeadwindsFlag = headwindsFlag,
            Limb = state.Opening?.Limb,
            Track = state.Opening?.Tracks,
            OpenedDay = state.Opening?.Day,
            Concentration = concentration,
            LocusFilter = headwindsFlag ? new LocusFilter { Exclude = ["external"] } : null,
            // Watch levels. D13's are computed and logged only; only D14's 0.75 is exposed.
            Watch = state.Tracks.ToDictionary(
                t => t,
                t => new Dictionary<string, Dictionary<string, bool>>(StringComparer.Ordinal)
                {
                    ["relative"] = Watch.Levels(state.Last[t].Relative.C, _config.Relative.H, _config.Watch.Levels),
                    ["absolute"] = Watch.Levels(state.Last[t].Absolute.A, _config.Absolute.G, _config.Watch.Levels),
                },
                StringComparer.Ordinal),
            WatchExposed = state.Tracks.Any(t =>
                state.Last[t].Absolute.A >= _config.Watch.ExposedLevel * _config.Absolute.G),
            Tracks = state.Tracks.ToDictionary(t => t, t => state.Last[t], StringComparer.Ordinal),
            Classifier = classified,
        };
    }

    /// <summary>
    /// The state source the engine is injected with.
    /// </summary>
    /// <remarks>
    /// Per-team running state is held here because the accumulators are recurrences. That is
    /// also what a production resolver looks like: it sees each day once, in order, and
    /// keeps what it needs between days.
    /// </remarks>
    public StateApplication? Resolve(EngineContext ctx, Team team, int day)
    {
        var state = StateFor(ctx, team);
        ResolvedRecord? record = null;
        // Catch up rather than jump, so no day is fed twice and none is skipped.
        for (var d = state.Day + 1; d <= day; d += 1)
        {
            // D24. Both accumulators are incremental, one push per call, so their windows
            // count pushes rather than dates. Feeding a day the team was never asked on puts
            // an empty entry in the baseline window and quietly returns the whole gate to
            // counting calendar days, which is the thing D24 exists to stop. A team not
            // asked today has told us nothing today.
            if (!team.IsAskedOn(d)) { state.Day = d; continue; }
            record = Advance(ctx, team, state, d);
            state.Records.Add(record);
            foreach (var listener in _listeners) listener(team, record);
        }
        if (record is null) return null;

        var label = char.ToUpperInvariant(record.Vertical[0]) + record.Vertical[1..];
        // `Changed` and `StateBefore` are what the weekly message renders. An earlier
        // version reported every day as a change with no previous state, so every weekly
        // message read "calibrating to Drift" whatever had happened.
        var before = state.LastState;
        state.LastState = label;
        return new StateApplication
        {
            Day = record.Day,
            // The engine keys the tally on the vertical alone (D21).
            State = label,
            Resolved = record,
            LocusFilter = record.LocusFilter,
            Changed = before != label,
            StateBefore = before,
            VerticalLeaning = record.Vertical == "cruise" ? "positive" : "negative",
            HorizontalLeaning = record.Horizontal,
            Reason = Describe(record),
        };
    }

    private static string Describe(ResolvedRecord record)
    {
        if (record.Vertical == "cruise")
        {
            return "the gate is closed: neither accumulator has reached its bound";
        }
        var limb = record.Limb == "absolute"
            ? "the absolute limb, so this team is running low rather than departing from itself"
            : record.Limb == "relative"
                ? "the relative limb, so this team has departed from its own recent baseline"
                : "both limbs on the same day";
        var horizontal = record.Horizontal == "external"
            ? "the causes named sit outside the team, so the flag goes upward"
            : record.Horizontal == "internal"
                ? "the causes named sit inside the team"
                : "the causes named do not locate yet";
        return $"{record.Vertical}, opened by {limb}. {horizontal}";
    }
}
