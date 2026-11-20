namespace Rrida.Engine;

/// <summary>A question, with its three chip sets in authored order.</summary>
public sealed class Question
{
    public required int Id { get; init; }
    public required string Text { get; init; }

    /// <summary>Open value (spec 7.1a). Identity is the pair (Track, Element).</summary>
    public required string Element { get; init; }

    public required string Track { get; init; }

    /// <summary>Chip ids by polarity, ordered by the chip id's index so a member sees N1..N6.</summary>
    public Dictionary<string, List<string>> Chips { get; } = new(StringComparer.Ordinal)
    {
        ["negative"] = [], ["indifferent"] = [], ["positive"] = [],
    };

    public string ElementKey => $"{Track}:{Element}";
}

/// <summary>The three rung one routes a chip carries. A positive chip carries exactly one.</summary>
public sealed class RungOne
{
    public string? Ask { get; init; }
    public string? Team { get; init; }
    public string? Content { get; init; }

    public string? this[string route] => route switch
    {
        "ask" => Ask,
        "team" => Team,
        "content" => Content,
        _ => null,
    };
}

/// <summary>
/// D31. One condition's outward move: what the manager raises, and the role they raise it
/// with.
/// </summary>
/// <remarks>
/// An outward move opens no cycle. It is surfaced to the manager and never delivered,
/// because closure requires the team's own answers to move and a cause outside the team will
/// not move them. The recipient is always a role: the layer has no org chart above the
/// manager and must not invent one.
/// </remarks>
public sealed class OutwardMove
{
    public required string IssueFile { get; init; }
    public required ActionRow Action { get; init; }
    public required string WhoItGoesTo { get; init; }
}

public sealed class Chip
{
    public required string Id { get; init; }
    public required int QuestionId { get; init; }
    public string? QuestionText { get; init; }
    public required string Element { get; init; }
    public required string Polarity { get; init; }
    public required string Label { get; init; }
    public required string IssueFile { get; init; }
    public required bool CapabilityGap { get; init; }

    /// <summary>
    /// Where the chip says the cause lives (spec 4.5). Authored, never inferred at runtime.
    /// </summary>
    /// <remarks>
    /// A chip with no tag is a data fault rather than a neutral chip: neutral is a positive
    /// claim that the chip locates no cause, and reading an absent tag as neutral would put
    /// untagged chips into the unlocated count that drives the content route in 4.6.
    /// </remarks>
    public required string? Locus { get; init; }

    public required RungOne Rung1 { get; init; }
    public required string Rung2 { get; init; }
    public required string Track { get; init; }

    /// <summary>
    /// The identity this chip's rung one votes under. For a routed chip that is the ask
    /// action; a positive chip does not route, so it is whichever single action it carries.
    /// </summary>
    public string TallyActionId { get; set; } = string.Empty;
}

public sealed class ActionRow
{
    public required string Id { get; init; }
    public required string Text { get; init; }
    public required string IssueFile { get; init; }
    public string? Issue { get; init; }
    public string? Pillar { get; init; }
    public string? Kind { get; init; }
    public string? Mode { get; init; }
    public string? NoteType { get; init; }
    public string? WorkedIf { get; init; }

    /// <summary>ask, team, content, or locus for the locus-discovery actions of spec 4.6.</summary>
    public string? Route { get; init; }

    public string? ReviewStatus { get; init; }
}

public sealed class IssueFileRow
{
    public required string Id { get; init; }
    public string? Issue { get; init; }
    public string? Pillar { get; init; }
    public int Conversational { get; init; }
    public int Structural { get; init; }
    public int Gesture { get; init; }
    public int ActionCount => Conversational + Structural + Gesture;
}

/// <summary>
/// A phase. The unit of delivery is a phase, not a worksheet (spec 5.1.3a).
/// </summary>
public sealed class Phase
{
    public required string Id { get; init; }
    public int Number { get; init; }
    public string? Title { get; init; }

    /// <summary>[low, high]. Null entries where the library does not time the phase.</summary>
    public double?[] Minutes { get; init; } = [null, null];

    public string? Produces { get; init; }

    /// <summary>
    /// What must run in the SAME session if this one does (spec 5.1.3b). The opposite
    /// direction to a prerequisite, which says run that one first in an earlier session.
    /// </summary>
    public List<string> RequiresPhases { get; init; } = [];

    public string? Role { get; init; }
    public bool Excerptable { get; init; } = true;

    /// <summary>"session" or "async". An async phase is not a duration and carries none.</summary>
    public required string? Timing { get; init; }

    public string? TimingNote { get; init; }
    public required string Worksheet { get; init; }
}

public sealed class Worksheet
{
    public required string Code { get; init; }
    public string? Title { get; init; }
    public string? Pillar { get; init; }

    /// <summary>Sequences rather than gates. Null sorts last: unlevelled is not assumed easy.</summary>
    public int? Difficulty { get; init; }

    public List<string> Prerequisites { get; init; } = [];

    /// <summary>Stated to the manager and declined by them. The layer does not evaluate these.</summary>
    public List<string> Contraindications { get; init; } = [];

    public string? Formerly { get; init; }
    public bool FormerlyRebuilt { get; init; }
    public string? TimeMinutes { get; init; }
    public string? GroupSize { get; init; }
    public List<Phase> Phases { get; init; } = [];

    /// <summary>False for a bare-string entry, which carries no front matter at all.</summary>
    public bool Declared { get; init; }
}

public sealed class CapabilityFlag
{
    public required string IssueFile { get; init; }
    public required bool CapabilityGap { get; init; }
    public List<string> Worksheets { get; init; } = [];
    public string? Rationale { get; init; }

    /// <summary>The action that delivers this file's locus-discovery run (spec 4.6).</summary>
    public string? LocusDiscoveryAction { get; init; }

    /// <summary>The fix.</summary>
    public List<string> ContentPhases { get; init; } = [];

    /// <summary>The answer to a majority-neutral signal. Its output is data rather than change.</summary>
    public List<string> LocusDiscoveryPhases { get; init; } = [];
}
