using Rrida.Engine;

namespace Rrida.State;

/// <summary>What one question's negative chips say about where causes live.</summary>
public sealed class LocusProfileEntry
{
    /// <summary>The external share among this question's located negative chips. Null where none locate.</summary>
    public required double? Base { get; init; }

    public required double NeutralShare { get; init; }

    /// <summary>unlocated, mixed, external-only or internal-only.</summary>
    public required string Klass { get; init; }
}

/// <summary>One answer, narrowed to what the classifier reads.</summary>
public sealed record ClassifierAnswer(bool Negative, IReadOnlyList<string> Loci);

public sealed class ClassifierReading
{
    public required int Day { get; init; }
    public required double? Z { get; init; }
    public required string Horizontal { get; init; }
    public required bool Accumulating { get; init; }
    public required double SumAttribution { get; init; }
    public required double SumLevel { get; init; }
    public required double VarianceAttribution { get; init; }
    public required double VarianceLevel { get; init; }
}

/// <summary>
/// The horizontal axis: the section H classifier, accumulated from the day the gate opened.
/// </summary>
/// <remarks>
/// <para>
/// Two components summed, each centred so that a team with no locus signal sits at zero by
/// construction rather than by calibration.
/// </para>
/// <para>
/// <b>Attribution, on mixed questions only.</b> A located negative contributes (1 - b) if
/// its locus is external and (-b) if internal, where b is that question's external share
/// among its located negative chips. Variance b(1 - b). Fixed-class questions are excluded
/// because their base rates are exactly 1.000 and 0.000, so they cannot express a change in
/// attribution at all.
/// </para>
/// <para>
/// <b>Level, on fixed-class questions only.</b> Each answer contributes (1 - r) if negative
/// and (-r) if not, signed by the question's class, where r is the team's own negative rate
/// over the trailing window. Variance r(1 - r). Centring on the team's own rate is what
/// makes this read the difference between question classes rather than the team's mood.
/// </para>
/// <para>
/// <c>z = (S_attr + S_level) / sqrt(V_attr + V_level)</c>
/// </para>
/// <para>
/// One departure from section H, forced by the difference between drawn and real answers.
/// Section H drew one locus per answer. A real answer can carry up to three chips and they
/// need not agree, so an answer contributes once per distinct located locus it names. An
/// answer naming both an internal and an external cause has genuinely named both, which is
/// the same rule §4.5a applies to the locus counts.
/// </para>
/// </remarks>
public sealed class Classifier
{
    private readonly IReadOnlyDictionary<int, LocusProfileEntry> _profile;
    private readonly ClassifierConfig _cfg;
    private readonly List<(int Answers, int Negatives)> _rate = [];
    private double _sAttr, _vAttr, _sLevel, _vLevel;
    private bool _accumulating;

    public Classifier(IReadOnlyDictionary<int, LocusProfileEntry> profile, ClassifierConfig cfg)
    {
        _profile = profile;
        _cfg = cfg ?? throw new ArgumentNullException(
            nameof(cfg), "Classifier needs its config. Nothing here carries a default.");
    }

    /// <summary>
    /// Per-question locus profile for one track, built from the bank the engine already loaded.
    /// </summary>
    /// <remarks>
    /// Negative chips only, and neutral excluded from the base rate, which is what §4.5b
    /// does with neutral negatives in the horizontal position itself.
    /// </remarks>
    public static Dictionary<int, LocusProfileEntry> BuildProfile(DataBank data, string track)
    {
        var byQuestion = new Dictionary<int, List<string>>();
        foreach (var chip in data.ChipList)
        {
            if (chip.Track != track || chip.Polarity != "negative") continue;
            if (!byQuestion.TryGetValue(chip.QuestionId, out var loci))
            {
                loci = [];
                byQuestion[chip.QuestionId] = loci;
            }
            loci.Add(chip.Locus ?? "");
        }
        var profile = new Dictionary<int, LocusProfileEntry>();
        foreach (var (questionId, loci) in byQuestion)
        {
            var external = loci.Count(l => l == "external");
            var @internal = loci.Count(l => l == "internal");
            var neutral = loci.Count(l => l == "neutral");
            var located = external + @internal;
            profile[questionId] = new LocusProfileEntry
            {
                Base = located > 0 ? (double)external / located : null,
                NeutralShare = (double)neutral / loci.Count,
                Klass = located == 0 ? "unlocated"
                    : external > 0 && @internal > 0 ? "mixed"
                    : external > 0 ? "external-only" : "internal-only",
            };
        }
        return profile;
    }

    /// <summary>
    /// Feed one day.
    /// </summary>
    /// <remarks>
    /// Before the gate opens there is nothing to accumulate: the classifier is consulted
    /// once the vertical axis says a team is slipping, and asking it earlier would be asking
    /// it to locate a problem nobody has reported. Days before the gate are still fed,
    /// because the trailing negative rate the level component centres on needs them.
    /// </remarks>
    public ClassifierReading Push(int day, int? questionId, IReadOnlyList<ClassifierAnswer> answers, bool gateOpen)
    {
        _rate.Add((answers.Count, answers.Count(a => a.Negative)));
        if (_rate.Count > _cfg.RateWindow) _rate.RemoveAt(0);

        // D22. The classifier accumulates from the gate and resets when the gate closes.
        //
        // An earlier version never restarted, on the reasoning that a team whose gate closes
        // and reopens is the same team continuing. Task 2 measured what that costs: on
        // team-care the gate closes on day 41 and the flag stayed raised to day 90, because
        // the evidence gathered while the gate was open kept z above the bound long after
        // the team had recovered. A team back in Cruise is not carrying causes it reported
        // six weeks ago, so the horizontal answer returns to unlocated and the flag drops on
        // the day the vertical returns to Cruise.
        if (gateOpen)
        {
            _accumulating = true;
        }
        else
        {
            _accumulating = false;
            _sAttr = 0;
            _vAttr = 0;
            _sLevel = 0;
            _vLevel = 0;
        }

        LocusProfileEntry? meta = null;
        if (questionId is { } qid) _profile.TryGetValue(qid, out meta);

        if (_accumulating && meta is not null && answers.Count > 0)
        {
            if (meta.Klass == "mixed")
            {
                var b = meta.Base!.Value;
                foreach (var answer in answers)
                {
                    if (!answer.Negative) continue;
                    foreach (var locus in answer.Loci)
                    {
                        if (locus == "external") { _sAttr += 1 - b; _vAttr += b * (1 - b); }
                        else if (locus == "internal") { _sAttr += -b; _vAttr += b * (1 - b); }
                    }
                }
            }
            else if (meta.Klass is "external-only" or "internal-only")
            {
                var totalAnswers = 0;
                var totalNegatives = 0;
                foreach (var row in _rate) { totalAnswers += row.Answers; totalNegatives += row.Negatives; }
                var r = totalAnswers > 0 ? (double)totalNegatives / totalAnswers : 0;
                var sign = meta.Klass == "external-only" ? 1 : -1;
                foreach (var answer in answers)
                {
                    _sLevel += sign * (answer.Negative ? 1 - r : -r);
                    _vLevel += r * (1 - r);
                }
            }
        }

        var variance = _vAttr + _vLevel;
        double? z = variance > 0 ? (_sAttr + _sLevel) / Math.Sqrt(variance) : null;
        var horizontal = "unlocated";
        if (z is { } value && value >= _cfg.ZBound) horizontal = "external";
        else if (z is { } v2 && v2 <= -_cfg.ZBound) horizontal = "internal";

        return new ClassifierReading
        {
            Day = day,
            Z = z,
            Horizontal = horizontal,
            Accumulating = _accumulating,
            SumAttribution = _sAttr,
            SumLevel = _sLevel,
            VarianceAttribution = _vAttr,
            VarianceLevel = _vLevel,
        };
    }
}
