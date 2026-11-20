namespace Rrida.State;

/// <summary>
/// The state log. One record per team per day, from day one.
/// </summary>
/// <remarks>
/// <para>
/// <b>Designed so a production store can take it unchanged.</b> What makes it portable: a
/// schema name and version on every record, so a store can migrate without guessing; a
/// natural key of (teamId, day), so <b>a write is an upsert and replaying a run is
/// idempotent rather than duplicating</b>; a configId on every record and the config
/// written once, so a record can be read back without knowing which settings were live when
/// it was made, because a state figure without its thresholds is not interpretable and the
/// thresholds are provisional; and flat, stable keys and plain numbers, so nothing needs
/// this codebase to deserialise.
/// </para>
/// <para>
/// <b>No identities.</b> The concentration reading is a word and a share. Member ids are
/// used to compute the share and never leave the resolver. On a team of three, naming who
/// carried the evidence names the person.
/// </para>
/// </remarks>
public static class StateLog
{
    public const string Schema = "rrida.state.daily.v1";
    public const string ConfigSchema = "rrida.state.config.v1";

    /// <summary>
    /// Round half away from zero at the boundary, the way JavaScript's Math.round does it.
    /// </summary>
    /// <remarks>
    /// <b>Not .NET's default.</b> <c>Math.Round</c> is banker's rounding, which turns 2.5
    /// into 2, and <c>MidpointRounding.AwayFromZero</c> turns -0.5 into -1 where JavaScript
    /// gives -0. JavaScript rounds a midpoint toward positive infinity, which is what
    /// <c>floor(x + 0.5)</c> does, and the classifier's sums are routinely negative, so this
    /// is not a theoretical difference.
    /// </remarks>
    public static double? RoundTo(double? value, int places = 6)
    {
        if (value is not { } v || !double.IsFinite(v)) return null;
        var factor = Math.Pow(10, places);
        return Math.Floor(v * factor + 0.5) / factor;
    }

    public sealed class BaselineRecord
    {
        public required double? M0 { get; init; }
        public required double? S0 { get; init; }
        public required double? S0Raw { get; init; }
        public required bool Floored { get; init; }
        public required int Age { get; init; }
        public required int Answers { get; init; }
        public required bool Frozen { get; init; }
    }

    public sealed class RelativeRecord
    {
        public required double? C { get; init; }
        public required double Bound { get; init; }
        public required bool Open { get; init; }
        public required bool Available { get; init; }
        public required BaselineRecord? Baseline { get; init; }
    }

    public sealed class AbsoluteRecord
    {
        public required double? A { get; init; }
        public required double Bound { get; init; }
        public required bool Open { get; init; }
    }

    public sealed class TrackRecord
    {
        public required int Answers { get; init; }
        public required int Negatives { get; init; }
        public required RelativeRecord Relative { get; init; }
        public required AbsoluteRecord Absolute { get; init; }
    }

    public sealed class OpeningRecord
    {
        public required int Day { get; init; }
        public required string? Limb { get; init; }
        public required List<string>? Tracks { get; init; }
    }

    public sealed class StateRecord
    {
        public required string Schema { get; init; }
        public required string ConfigId { get; init; }
        public required string TeamId { get; init; }
        public required int Day { get; init; }
        public required string Vertical { get; init; }
        public required string Horizontal { get; init; }
        public required bool HeadwindsFlag { get; init; }

        /// <summary>D18. Null where the gate is shut, which is a different thing from an unknown limb.</summary>
        public required OpeningRecord? Opening { get; init; }

        public required Dictionary<string, TrackRecord> Tracks { get; init; }
        public required double? ClassifierZ { get; init; }
        public required string? ClassifierHorizontal { get; init; }
        public required bool ClassifierAccumulating { get; init; }
        public required double? SumAttribution { get; init; }
        public required double? SumLevel { get; init; }
        public required double? VarianceAttribution { get; init; }
        public required double? VarianceLevel { get; init; }

        /// <summary>A word and a share. <b>Never a member.</b></summary>
        public required string? ConcentrationReading { get; init; }

        public required double? ConcentrationTopShare { get; init; }
        public required double? ConcentrationCut { get; init; }
        public required int? ConcentrationContributors { get; init; }
        public required int? ConcentrationResponders { get; init; }
        public required Dictionary<string, Dictionary<string, Dictionary<string, bool>>> Watch { get; init; }
        public required bool WatchExposed { get; init; }

        /// <summary>The natural key. A write against it is an upsert.</summary>
        public (string TeamId, int Day) Key => (TeamId, Day);
    }

    private static BaselineRecord? BaselineOf(RelativeReading relative)
    {
        if (relative.BaselineValue is not { } b) return null;
        return new BaselineRecord
        {
            M0 = RoundTo(b.M0),
            S0 = RoundTo(b.S0),
            S0Raw = RoundTo(b.S0Raw),
            Floored = b.Floored,
            Age = b.Age,
            Answers = b.Answers,
            Frozen = relative.BaselineFrozen,
        };
    }

    /// <summary>
    /// One day's record for one team, as plain data.
    /// </summary>
    /// <remarks>
    /// Pure: it takes what the resolver produced and returns data. Nothing here reads a
    /// clock or a filesystem, so a production writer can call it directly.
    /// </remarks>
    public static StateRecord Of(string teamId, ResolvedRecord record, string configId)
    {
        var tracks = new Dictionary<string, TrackRecord>(StringComparer.Ordinal);
        foreach (var (track, side) in record.Tracks)
        {
            tracks[track] = new TrackRecord
            {
                Answers = side.Answers,
                Negatives = side.Negatives,
                Relative = new RelativeRecord
                {
                    C = RoundTo(side.Relative.C),
                    Bound = side.Relative.Bound,
                    Open = side.Relative.Open,
                    Available = side.Relative.Available,
                    Baseline = BaselineOf(side.Relative),
                },
                Absolute = new AbsoluteRecord
                {
                    A = RoundTo(side.Absolute.A),
                    Bound = side.Absolute.Bound,
                    Open = side.Absolute.Open,
                },
            };
        }

        return new StateRecord
        {
            Schema = Schema,
            ConfigId = configId,
            TeamId = teamId,
            Day = record.Day,
            Vertical = record.Vertical,
            Horizontal = record.Horizontal,
            HeadwindsFlag = record.HeadwindsFlag,
            Opening = record.OpenedDay is { } openedDay
                ? new OpeningRecord { Day = openedDay, Limb = record.Limb, Tracks = record.Track }
                : null,
            Tracks = tracks,
            ClassifierZ = RoundTo(record.Classifier.Z),
            ClassifierHorizontal = record.Classifier.Horizontal,
            ClassifierAccumulating = record.Classifier.Accumulating,
            SumAttribution = RoundTo(record.Classifier.SumAttribution),
            SumLevel = RoundTo(record.Classifier.SumLevel),
            VarianceAttribution = RoundTo(record.Classifier.VarianceAttribution),
            VarianceLevel = RoundTo(record.Classifier.VarianceLevel),
            ConcentrationReading = record.Concentration.Reading,
            ConcentrationTopShare = RoundTo(record.Concentration.TopShare),
            ConcentrationCut = RoundTo(record.Concentration.Cut),
            ConcentrationContributors = record.Concentration.Contributors,
            ConcentrationResponders = record.Concentration.Responders,
            Watch = record.Watch,
            WatchExposed = record.WatchExposed,
        };
    }
}

/// <summary>An in-memory sink, for tests and for anything that wants the records without a store.</summary>
public sealed class MemorySink : IStateSink
{
    private readonly string _configId;
    public List<StateLog.StateRecord> Records { get; } = [];

    public MemorySink(string configId) => _configId = configId;

    public void Write(string teamId, ResolvedRecord record) =>
        Records.Add(StateLog.Of(teamId, record, _configId));

    public int Count => Records.Count;
}
