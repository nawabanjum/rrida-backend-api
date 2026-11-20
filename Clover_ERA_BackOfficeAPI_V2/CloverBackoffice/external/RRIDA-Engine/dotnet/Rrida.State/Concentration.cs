namespace Rrida.State;

public sealed class ConcentrationReading
{
    /// <summary>unknown, marginal, concentrated or spread. <b>A word, never an identity.</b></summary>
    public required string Reading { get; init; }

    public required double? TopShare { get; init; }
    public double? Cut { get; init; }
    public string? CutBasis { get; init; }
    public int? Contributors { get; init; }
    public int? Responders { get; init; }
    public required string Basis { get; init; }
}

/// <summary>
/// D16. Was the evidence one person or the team?
/// </summary>
/// <remarks>
/// <para>
/// Over the days before the gate opened, each answer's own increment to the relative
/// accumulator is <c>(m0 - x) / s0 - k</c>. An answer whose increment is positive is
/// below-baseline evidence. Summing those per member gives the share carried by the largest
/// single contributor, and that share is compared against a cut that depends on how many
/// people answered.
/// </para>
/// <para>
/// <b>The cut is a lookup, not a constant.</b> Measured, the equal-error cut runs 0.680,
/// 0.525, 0.430, 0.320 and 0.255 at 2, 3, 4, 6 and 8 responders, tracking roughly 1/n. One
/// cut for every size reaches 8.0 per cent worst-case error against 4.1 for the lookup, and
/// it fails in different directions at each end: too high for small teams, too low for
/// large ones.
/// </para>
/// <para>
/// <b>The output is a word, never an identity.</b> Contributor identity is used to compute
/// the share and does not leave this file. That is not a politeness: on a team of three,
/// naming who carried the evidence names the person, and this layer is not permitted to do
/// that.
/// </para>
/// </remarks>
public static class Concentration
{
    public sealed record Cut(double Value, string Basis);

    /// <summary>The D16 cut for a responder count, interpolated between measured rows and clamped.</summary>
    public static Cut CutFor(int responders, IReadOnlyList<(double Responders, double Cut)> lookup)
    {
        if (lookup.Count == 0)
        {
            throw new ArgumentException("CutFor needs the D16 lookup. Nothing here carries a default.", nameof(lookup));
        }
        var rows = lookup.OrderBy(r => r.Responders).ToList();
        if (responders <= rows[0].Responders) return new Cut(rows[0].Cut, "clamped to the smallest measured row");
        var last = rows[^1];
        if (responders >= last.Responders) return new Cut(last.Cut, "clamped to the largest measured row");
        for (var i = 0; i < rows.Count - 1; i += 1)
        {
            var (n0, c0) = rows[i];
            var (n1, c1) = rows[i + 1];
            if (responders >= n0 && responders <= n1)
            {
                var t = (responders - n0) / (n1 - n0);
                return new Cut(c0 + t * (c1 - c0), $"interpolated between {n0} and {n1} responders");
            }
        }
        return new Cut(last.Cut, "clamped");
    }

    /// <summary>One day of the concentration window: the baseline that stood, and who answered.</summary>
    public sealed record WindowDay(int Day, Baseline? Baseline, IReadOnlyList<(string MemberId, double Level)> Answers);

    /// <summary>
    /// Read the concentration from a window of per-member increments.
    /// </summary>
    /// <remarks>
    /// Days with no baseline contribute nothing, because without one there is no increment.
    /// A team of 3 is reported as <c>marginal</c> rather than concentrated or spread: at two
    /// responders one person is half the team and the two distributions overlap at 7.6 per
    /// cent equal error, the worst row in the study. That is structural and no cut fixes it.
    /// </remarks>
    public static ConcentrationReading Read(
        IReadOnlyList<WindowDay> window, ConcentrationConfig cfg, double k)
    {
        var perMember = new Dictionary<string, double>(StringComparer.Ordinal);
        double total = 0;
        var members = new HashSet<string>(StringComparer.Ordinal);
        foreach (var day in window)
        {
            if (day.Baseline is not { } baseline) continue;
            foreach (var (memberId, level) in day.Answers)
            {
                members.Add(memberId);
                var increment = (baseline.M0 - level) / baseline.S0 - k;
                if (increment <= 0) continue;
                perMember[memberId] = perMember.GetValueOrDefault(memberId) + increment;
                total += increment;
            }
        }

        if (total <= 0 || perMember.Count == 0)
        {
            return new ConcentrationReading
            {
                Reading = "unknown",
                TopShare = null,
                Contributors = 0,
                Responders = members.Count,
                Basis = "no below-baseline evidence in the window, so there is nothing to attribute",
            };
        }

        var topShare = perMember.Values.Max() / total;
        var responders = members.Count;
        var cut = CutFor(responders, cfg.Lookup);
        var reading = responders <= 2 ? "marginal" : topShare > cut.Value ? "concentrated" : "spread";
        return new ConcentrationReading
        {
            Reading = reading,
            TopShare = topShare,
            Cut = cut.Value,
            CutBasis = cut.Basis,
            Contributors = perMember.Count,
            Responders = responders,
            Basis = responders <= 2
                ? "two responders, so one person is half the team and the reading cannot separate them"
                : $"top contributor carried {Pct(topShare * 100)} per cent against a cut of {Pct(cut.Value * 100)}",
        };
    }

    /// <summary>Zero decimal places, as the reference's toFixed(0) writes it.</summary>
    private static string Pct(double value) =>
        Math.Floor(value + 0.5).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
}
