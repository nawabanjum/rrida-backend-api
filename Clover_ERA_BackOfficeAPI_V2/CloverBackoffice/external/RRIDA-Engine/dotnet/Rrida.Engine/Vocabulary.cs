namespace Rrida.Engine;

/// <summary>
/// The closed value sets, and the one number the specification turns a smiley into.
/// </summary>
/// <remarks>
/// <para>
/// Element is deliberately absent. Spec 7.1a: <b>element is an open value, not an
/// enumeration.</b> The team bank covers Potency, Belonging, Structure, Coordination
/// Quality, Accountability and Goal Affinity, none of which are CLOVER elements, and a
/// port that enumerated them would reject that bank. Element identity is the pair
/// (track, element), never the name on its own: the team bank has a condition called
/// Learning which is the old Improvement Loop, not the CLOVER element of the same name.
/// </para>
/// <para>
/// These values also live in <c>kit/data/thresholds.json</c> under <c>vocabulary</c>. They
/// are duplicated here rather than read, because they are the shape of the type system
/// rather than a setting: a run with a fifth smiley is not a differently-configured run.
/// The loader asserts the two agree.
/// </para>
/// </remarks>
public static class Vocabulary
{
    /// <summary>Ordered worst to best, so "improved by one step" (spec 9.3) is one index move.</summary>
    public static readonly string[] SmileyOrder = ["red", "orange", "yellow", "green"];

    /// <summary>
    /// Severity weight per smiley (spec 4.2). Green is 0.5 by design, not by oversight.
    /// </summary>
    /// <remarks>
    /// These are the table's values. §4.2's prose says "Red and Orange are the only weights
    /// above 0.5", which contradicts its own table, and §6.4 sides with the table. The
    /// contradiction is reported and left open (D6); see kit/definitions/spec-findings.md.
    /// They are used in exactly one place, the tally tie-break, and feed neither trend line
    /// nor either accumulator.
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, double> Severity =
        new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["red"] = 1.0, ["orange"] = 0.5, ["yellow"] = 0.5, ["green"] = 0.5,
        };

    /// <summary>Which tally a smiley's chips land in (spec 4.1, 6.2).</summary>
    public static readonly IReadOnlyDictionary<string, string> SmileyTally =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["red"] = "fix", ["orange"] = "fix", ["yellow"] = "yellow", ["green"] = "build",
        };

    /// <summary>Which chip set a smiley presents (spec 4.1). Red and Orange share the negative set.</summary>
    public static readonly IReadOnlyDictionary<string, string> SmileyPolarity =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["red"] = "negative", ["orange"] = "negative",
            ["yellow"] = "indifferent", ["green"] = "positive",
        };

    public static readonly IReadOnlyDictionary<string, string> SmileyLabel =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["red"] = "Angry", ["orange"] = "Sad", ["yellow"] = "Indifferent", ["green"] = "Happy",
        };

    /// <summary>
    /// Where a chip says the cause lives (spec 4.5). Internal pulls the state toward Drift,
    /// external toward Headwinds, neutral pulls nowhere and is a condition in its own right.
    /// </summary>
    public static readonly string[] LocusValues = ["internal", "external", "neutral"];

    /// <summary>The two answer tracks. Both are asked; only some drive delivery.</summary>
    public static readonly string[] Tracks = ["individual", "team"];

    /// <summary>The three rung one routes (spec 5.1.1), in the order the share walks them.</summary>
    public static readonly string[] Routes = ["ask", "team", "content"];

    /// <summary>
    /// The tally key (spec 4.2, build spec 4.4.1).
    /// </summary>
    /// <remarks>
    /// Votes accumulate on the ask action and the route is swapped at selection, once the
    /// winner and its contributor share are known. Counting on team or content instead
    /// would collapse distinct conditions before the diagnosis has been made: there is one
    /// team action per issue file and one content action per capability-gap file.
    /// </remarks>
    public const string TallyKey = "ask";

    public static readonly IReadOnlyDictionary<string, string> RouteColumn =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ask"] = "rung_1_ask", ["team"] = "rung_1_team", ["content"] = "rung_1_content",
        };

    /// <summary>The index of a smiley in <see cref="SmileyOrder"/>, or -1.</summary>
    public static int SmileyIndex(string smiley) => Array.IndexOf(SmileyOrder, smiley);

    public static bool IsNegative(string smiley) =>
        SmileyPolarity.TryGetValue(smiley, out var p) && p == "negative";
}

/// <summary>
/// A fault in the data, raised where it happened.
/// </summary>
/// <remarks>
/// Nothing in the loader repairs data. The counts in meta.json are asserted against what
/// was loaded and a mismatch halts. If the source changes, re-run the exporter and fix the
/// workbook, never the JSON.
/// </remarks>
public sealed class DataException : Exception
{
    public DataException(string message) : base(message) { }
}
