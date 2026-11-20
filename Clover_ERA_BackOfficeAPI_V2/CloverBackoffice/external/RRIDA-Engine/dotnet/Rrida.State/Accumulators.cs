namespace Rrida.State;

/// <summary>One track's answers for one day, reduced to what the accumulators need.</summary>
public sealed class DayObservation
{
    public required int Answers { get; init; }
    public required double LevelSum { get; init; }
    public required double LevelSqSum { get; init; }
    public required int Negatives { get; init; }
    public required List<(string MemberId, double Level)> PerMember { get; init; }
}

public sealed class Baseline
{
    public required double M0 { get; init; }
    public required double S0 { get; init; }
    public required double S0Raw { get; init; }
    public required bool Floored { get; init; }
    public required int Age { get; init; }
    public required int Answers { get; init; }
}

public sealed class RelativeReading
{
    public required int Day { get; init; }
    public required double C { get; init; }
    public required double Bound { get; init; }
    public required bool Open { get; init; }
    public required Baseline? BaselineValue { get; init; }
    public required bool BaselineFrozen { get; init; }

    /// <summary>
    /// No baseline means the limb cannot open, not that it is quiet: before the window fills
    /// there is nothing for a relative measure to be relative to.
    /// </summary>
    public required bool Available { get; init; }
}

public sealed class AbsoluteReading
{
    public required int Day { get; init; }
    public required double A { get; init; }
    public required double Bound { get; init; }
    public required bool Open { get; init; }
}

/// <summary>
/// D13. The team against its own recent history. Provisional, pending ratification.
/// </summary>
/// <remarks>
/// <para>
/// <c>C = max(0, C + sum over the day's answers of ((m0 - x) / s0 - k))</c>, where m0 and s0
/// are the pooled mean and per-answer standard deviation over a baseline window that ends
/// <c>lag</c> days before today, holds up to <c>maxDays</c> and needs at least
/// <c>minDays</c> of answers to exist at all.
/// </para>
/// <para>
/// The max(0, .) is what makes this an accumulator rather than a running total: a team that
/// recovers drains the evidence away rather than banking it. The <c>k</c> allowance is what
/// makes a team sitting exactly at its baseline drift back to zero rather than wandering up.
/// </para>
/// <para>
/// <b>The baseline freezes while the gate is open.</b> Otherwise a team that stays bad long
/// enough teaches the baseline that bad is normal, and the limb that is supposed to be
/// holding it open quietly lets go.
/// </para>
/// <para>
/// <b>This is incremental and a day is fed once.</b> The accumulator is defined by a
/// recurrence, and recomputing it from history every day would be a different program that
/// happens to agree. Feeding a day twice double-counts it.
/// </para>
/// </remarks>
public sealed class RelativeAccumulator
{
    private readonly RelativeConfig _cfg;
    private readonly List<DayObservation> _history = [];
    private double _c;
    private Baseline? _frozen;

    public RelativeAccumulator(RelativeConfig cfg)
    {
        _cfg = cfg ?? throw new ArgumentNullException(
            nameof(cfg), "RelativeAccumulator needs its config. Nothing here carries a default.");
    }

    public double Value => _c;

    /// <summary>Feed one day. <paramref name="gateOpen"/> is the gate's state on the day being fed.</summary>
    public RelativeReading Push(int day, DayObservation obs, bool gateOpen)
    {
        _history.Add(obs);
        var hi = _history.Count - 1 - _cfg.Lag;          // the baseline window ends here
        var lo = Math.Max(0, hi - _cfg.MaxDays + 1);
        Baseline? baseline = null;
        if (hi >= 0 && hi - lo + 1 >= _cfg.MinDays)
        {
            var answers = 0;
            double sum = 0;
            double sq = 0;
            for (var i = lo; i <= hi; i += 1)
            {
                answers += _history[i].Answers;
                sum += _history[i].LevelSum;
                sq += _history[i].LevelSqSum;
            }
            if (answers > 0)
            {
                var m0 = sum / answers;
                var variance = Math.Max(0, sq / answers - m0 * m0);
                var raw = Math.Sqrt(variance);
                baseline = new Baseline
                {
                    M0 = m0,
                    S0 = Math.Max(raw, _cfg.S0Floor),
                    S0Raw = raw,
                    Floored = raw < _cfg.S0Floor,
                    Age = hi - lo + 1,
                    Answers = answers,
                };
            }
        }

        // Frozen while the gate is open, and released when it closes.
        if (gateOpen && _frozen is null && baseline is not null) _frozen = baseline;
        if (!gateOpen) _frozen = null;
        var live = _frozen ?? baseline;

        if (live is not null && obs.Answers > 0)
        {
            var increment = (obs.Answers * live.M0 - obs.LevelSum) / live.S0 - obs.Answers * _cfg.K;
            _c = Math.Max(0, _c + increment);
        }

        return new RelativeReading
        {
            Day = day,
            C = _c,
            Bound = _cfg.H,
            Open = live is not null && _c >= _cfg.H,
            BaselineValue = live,
            BaselineFrozen = _frozen is not null,
            Available = live is not null,
        };
    }
}

/// <summary>
/// D14. The team against a fixed reference, live from day one. Provisional.
/// </summary>
/// <remarks>
/// <para>
/// <c>n = 1</c> for a Red or Orange answer and 0 otherwise;
/// <c>A = max(0, A + sum over the day's answers of ((n - p_ref) / s_ref))</c>.
/// </para>
/// <para>
/// D13 asks whether a team has departed from itself, and a team that was already negative on
/// day one never departs from anything: its baseline is the bad place. Measured, a team
/// answering negatively 45 per cent of the time gives a D13 accumulator of 2.8 at day 90
/// against 3.2 for a healthy team, which is to say slightly quieter, because a worse
/// baseline is a lower bar to clear. <b>D14 is the limb that sees it</b>, and it replaces D2
/// as a gate-opening rule entirely, in warm-up and after.
/// </para>
/// </remarks>
public sealed class AbsoluteAccumulator
{
    private readonly AbsoluteConfig _cfg;
    private readonly double _sRef;
    private double _a;

    public AbsoluteAccumulator(AbsoluteConfig cfg)
    {
        _cfg = cfg ?? throw new ArgumentNullException(
            nameof(cfg), "AbsoluteAccumulator needs its config. Nothing here carries a default.");
        _sRef = Math.Sqrt(_cfg.PRef * (1 - _cfg.PRef));
    }

    public double Value => _a;

    public AbsoluteReading Push(int day, DayObservation obs)
    {
        if (obs.Answers > 0)
        {
            _a = Math.Max(0, _a + (obs.Negatives - _cfg.PRef * obs.Answers) / _sRef);
        }
        return new AbsoluteReading { Day = day, A = _a, Bound = _cfg.G, Open = _a >= _cfg.G };
    }
}

public static class Watch
{
    /// <summary>Watch levels as fractions of a bound. Reported for both limbs; only one is exposed.</summary>
    public static Dictionary<string, bool> Levels(double value, double bound, IReadOnlyList<double> levels)
    {
        var output = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var level in levels) output[Key(level)] = value >= level * bound;
        return output;
    }

    /// <summary>
    /// The key a level is recorded under, matching how JavaScript stringifies the number:
    /// 0.5 becomes "0.5" and 0.75 becomes "0.75", with no trailing zeroes.
    /// </summary>
    public static string Key(double level) =>
        level.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
}
