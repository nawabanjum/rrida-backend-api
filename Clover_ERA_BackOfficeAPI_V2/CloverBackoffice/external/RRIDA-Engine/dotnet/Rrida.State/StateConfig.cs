using System.Text.Json;
using Rrida.Engine;

namespace Rrida.State;

/// <summary>
/// Every setting the state computation uses, in one place, injected at the composition root.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing under Rrida.State carries a default.</b> These values are injected and the
/// resolver throws without them. That is the same rule the state source and the delivery
/// tracks follow, for the same reason: a default here is a decision about what the product
/// calls trouble, and that decision does not belong inside the thing doing the arithmetic.
/// </para>
/// <para>
/// The values are the ones measured in the handover, not chosen here. D13's come from
/// section J, D14's and D16's from section K, D17 is why g is calibrated against the full
/// gate rather than its own limb, and the classifier's from section H.
/// </para>
/// <para>
/// <b>Every one of them is PROVISIONAL and pending ratification.</b> Changing a number here
/// changes what the product calls trouble and should be treated as that, not as a tuning knob.
/// </para>
/// </remarks>
public sealed class StateConfig
{
    /// <summary>Red 0, Orange 1/3, Yellow 2/3, Green 1. Worst to best, matching the smiley order.</summary>
    public required double[] LevelScale { get; init; }

    public required RelativeConfig Relative { get; init; }
    public required AbsoluteConfig Absolute { get; init; }
    public required ClassifierConfig Classifier { get; init; }
    public required ConcentrationConfig Concentration { get; init; }
    public required WatchConfig Watch { get; init; }

    public static StateConfig Load(string thresholdsFile)
    {
        var raw = JsonDocument.Parse(File.ReadAllText(thresholdsFile)).RootElement;
        var s = raw.GetProperty("state");
        var rel = s.GetProperty("relative");
        var abs = s.GetProperty("absolute");
        var cls = s.GetProperty("classifier");
        var con = s.GetProperty("concentration");
        var watch = s.GetProperty("watch");
        return new StateConfig
        {
            LevelScale = s.GetProperty("levelScale").EnumerateArray().Select(x => x.GetDouble()).ToArray(),
            Relative = new RelativeConfig
            {
                Lag = rel.GetProperty("lag").GetInt32(),
                MinDays = rel.GetProperty("minDays").GetInt32(),
                MaxDays = rel.GetProperty("maxDays").GetInt32(),
                K = rel.GetProperty("k").GetDouble(),
                H = rel.GetProperty("h").GetDouble(),
                S0Floor = rel.GetProperty("s0Floor").GetDouble(),
            },
            Absolute = new AbsoluteConfig
            {
                PRef = abs.GetProperty("pRef").GetDouble(),
                G = abs.GetProperty("g").GetDouble(),
            },
            Classifier = new ClassifierConfig
            {
                ZBound = cls.GetProperty("zBound").GetDouble(),
                RateWindow = cls.GetProperty("rateWindow").GetInt32(),
                Track = cls.GetProperty("track").GetString()!,
            },
            Concentration = new ConcentrationConfig
            {
                WindowDays = con.GetProperty("windowDays").GetInt32(),
                Lookup = con.GetProperty("lookup").EnumerateArray()
                    .Select(row =>
                    {
                        var pair = row.EnumerateArray().ToArray();
                        return (Responders: pair[0].GetDouble(), Cut: pair[1].GetDouble());
                    })
                    .ToArray(),
            },
            Watch = new WatchConfig
            {
                Levels = watch.GetProperty("levels").EnumerateArray().Select(x => x.GetDouble()).ToArray(),
                ExposedLimb = watch.GetProperty("exposed").GetProperty("limb").GetString()!,
                ExposedLevel = watch.GetProperty("exposed").GetProperty("level").GetDouble(),
            },
        };
    }

    /// <summary>
    /// A short stable id for a settings object, so a record can name what produced it.
    /// </summary>
    /// <remarks>
    /// <b>This is deliberately not a hash computed here.</b> The reference hashes the
    /// JavaScript object's own JSON serialisation, which a C# object cannot reproduce
    /// without agreeing about key order, number formatting and float printing. The config id
    /// is carried in the fixture instead and handed in, so the two implementations agree
    /// about the label without agreeing about a serialiser.
    /// </remarks>
    public string ConfigId { get; init; } = "";
}

public sealed class RelativeConfig
{
    /// <summary>The baseline window ends this many days before today.</summary>
    public required int Lag { get; init; }

    /// <summary>And needs at least this many days of answers to exist at all.</summary>
    public required int MinDays { get; init; }

    /// <summary>Growing to this, then rolling.</summary>
    public required int MaxDays { get; init; }

    /// <summary>Per-answer allowance, so a team at its baseline drifts back to zero.</summary>
    public required double K { get; init; }

    /// <summary>The bound. A single value for all team sizes (D17).</summary>
    public required double H { get; init; }

    /// <summary>A guard against a degenerate baseline, not an active part.</summary>
    public required double S0Floor { get; init; }
}

public sealed class AbsoluteConfig
{
    public required double PRef { get; init; }
    public required double G { get; init; }
}

public sealed class ClassifierConfig
{
    public required double ZBound { get; init; }

    /// <summary>Trailing days the team's own negative rate is read over.</summary>
    public required int RateWindow { get; init; }

    /// <summary>Section H ran on the team bank and this reads the same one.</summary>
    public required string Track { get; init; }
}

public sealed class ConcentrationConfig
{
    public required int WindowDays { get; init; }

    /// <summary>[responders, cut], interpolated between rows and clamped outside them.</summary>
    public required (double Responders, double Cut)[] Lookup { get; init; }
}

public sealed class WatchConfig
{
    public required double[] Levels { get; init; }

    /// <summary>D18: only the D14 0.75 level is exposed. D13's are computed and logged only.</summary>
    public required string ExposedLimb { get; init; }

    public required double ExposedLevel { get; init; }
}
