namespace Rrida.Engine;

/// <summary>What a chip read as on one day: distinct contributors, and that day's mean polarity.</summary>
public sealed class ChipRead
{
    public required int Day { get; init; }
    public required int Contributors { get; init; }
    public required List<string> Members { get; init; }
    public required int Responses { get; init; }
    public required double? MeanSmileyIndex { get; init; }

    /// <summary>
    /// D27. Which bucket was read, and which question happened to fill it that day.
    /// </summary>
    /// <remarks>
    /// The read and the baseline are usually different questions filling the same file, and
    /// that is the point of keying the read on the file. The question is carried out so the
    /// arithmetic can say which one it was. Null on a chip-level read.
    /// </remarks>
    public string? IssueFile { get; init; }

    public int? QuestionId { get; init; }
}

/// <summary>
/// D28. One condition the floor has seen, and every day it was above it.
/// </summary>
/// <remarks>
/// Samples are kept per day rather than folded into running totals, so the watch can answer
/// for any past day and not only for the latest one. The weekly card has a week selector: a
/// list that reported today's figures against an older week would be telling a manager what
/// they are only learning now that they knew a month ago.
/// </remarks>
public sealed class WatchRecord
{
    public required string IssueFile { get; init; }
    public required string Issue { get; init; }
    public List<WatchSample> Samples { get; } = [];
}

/// <summary>One asked day on which a condition stood above the floor.</summary>
public sealed class WatchSample
{
    public required int Day { get; init; }
    public required int AskedDay { get; init; }
    public int Contributors { get; set; }
    public double Severity { get; set; }
}

/// <summary>D31. One row of the outward list: a condition, and the move that is not the manager's.</summary>
public sealed record OutwardEntry(
    string IssueFile,
    string Issue,
    int FirstAboveDay,
    int LastAboveDay,
    int DaysAbove,
    int Consecutive,
    /// <summary>Carried for the sort and never for a surface: the row hands out words.</summary>
    int PeakContributors,
    string StrengthInWords,
    double Severity,
    int AskedDaysSinceLastAbove,
    string? Move,
    string? HowYouWillKnow,
    string? WhoItGoesTo);

/// <summary>D28. One row of the watch list, as at the day it was asked for.</summary>
public sealed record WatchEntry(
    string IssueFile,
    string Issue,
    int FirstAboveDay,
    int LastAboveDay,
    int DaysAbove,
    int Consecutive,
    int PeakContributors,
    string StrengthInWords,
    double Severity,
    int AskedDaysSinceLastAbove);

public sealed class SignalRead
{
    public required int Day { get; init; }
    public required ChipRead Baseline { get; init; }
    public required ChipRead Read { get; init; }
    public required int FloorRequired { get; init; }
    public required bool FellByOne { get; init; }
    public required bool AlreadyAtFloor { get; init; }
    public required double PolarityStep { get; init; }
    public required bool PolarityImproved { get; init; }
    public required bool Moved { get; init; }
    public bool Unread { get; init; }
    public string? HowRead { get; init; }
}

/// <summary>What rung one did, kept when the ladder climbs. The only place it survives.</summary>
public sealed class RungOneRecord
{
    public required string ActionId { get; init; }
    public required bool Confirmed { get; init; }
    public required int? ConfirmedDay { get; init; }
    public required bool? WorkedIfMet { get; init; }
    public required SignalRead? SignalRead { get; init; }
}

public sealed class Decline
{
    public required int Day { get; init; }
    public required string Reason { get; init; }
    public required List<string> Worksheets { get; init; }
}

/// <summary>
/// One active action, from selection to resolution. One per team at a time (spec 8.1).
/// </summary>
/// <remarks>
/// It holds through the full cycle and is displaced only when it closes or fails. <b>The
/// cycle mutates in place when it ladders</b>: after <see cref="Team.AdvanceToRungTwo"/>
/// its rung is two, its action is the structural one, and its baseline has been retaken, so
/// anything reporting on a cycle after the fact has to say which rung it means.
/// </remarks>
public sealed class Cycle
{
    public required string Id { get; init; }
    public required string TeamId { get; init; }
    public required int Index { get; init; }
    public required string IssueFile { get; init; }
    public string? Issue { get; init; }

    public int Rung { get; set; } = 1;
    public required string Rung1ActionId { get; init; }
    public required string Rung2ActionId { get; init; }
    public string ActionId { get; set; } = "";

    /// <summary>
    /// The action the votes were filed under, which is the ask action on every routed chip.
    /// </summary>
    /// <remarks>
    /// Kept because it is the identity the tally and the suppression rule share, and on the
    /// team and content routes it is not the action that was delivered.
    /// </remarks>
    public required string TallyActionId { get; init; }

    public required Routing Routing { get; init; }
    public required string TriggeringChipId { get; init; }
    public string? TriggeringChipLabel { get; init; }
    public required int TriggeringQuestionId { get; init; }
    public string? TriggeringQuestionText { get; init; }

    public int BaselineDay { get; set; }
    public ChipRead? Baseline { get; set; }
    public required int SelectedDay { get; init; }
    public int DeliveredDay { get; set; }
    public int ConfirmationDueDay { get; set; }
    public bool Confirmed { get; set; }
    public int? ConfirmedDay { get; set; }
    public string? ConfirmationSource { get; set; }
    public bool? WorkedIfMet { get; set; }
    public int? SignalReadDay { get; set; }

    /// <summary>D27. Which bank the read runs against. The file is read on the track it was raised on.</summary>
    public string ReadTrack { get; set; } = "individual";

    public SignalRead? SignalRead { get; set; }

    /// <summary>D23. How many times this read has waited for a day with answers on it.</summary>
    public int ReadDeferrals { get; set; }

    public List<int> ReadDeferredFrom { get; } = [];

    public bool Rung2Skipped { get; set; }
    public string? Rung2SkipReason { get; set; }
    public string? Outcome { get; set; }
    public int? ResolvedDay { get; set; }
    public Decline? Declined { get; set; }
    public RungOneRecord? RungOneRecord { get; set; }

    // What the tally held when this was selected, kept for the machinery panel.
    public required int TallyVotes { get; init; }
    public required List<string> TallyContributors { get; init; }
    public required double TallySeverityWeight { get; init; }
    public required List<int> TallyQuestionIds { get; init; }
}

public sealed class SuccessionRecord
{
    public required string HeldFile { get; init; }
    public string? Issue { get; init; }
    public required int EligibleInFile { get; init; }
    public required Tally.FileSpread QuestionSpread { get; init; }
    public required List<string> MechanismsReachable { get; init; }
    public required List<string> MechanismsInstalled { get; init; }
    public required List<string> MechanismsRemaining { get; init; }
    public required bool Exhausted { get; init; }
    public required bool InForce { get; init; }
    public bool Released => !InForce;
    public required string Reason { get; init; }
}

/// <summary>One candidate kept out of a selection, and why. Reported rather than silent.</summary>
public sealed record SelectionHold(string ActionId, string IssueFile, int Contributors, string Reason);

public sealed class SelectionAttempt
{
    public required int Day { get; init; }
    public required string? State { get; init; }
    public required string? LiveTally { get; init; }
    public required WindowedTallies Windowed { get; init; }
    public Floor Floor => Windowed.Floor;
    public SuccessionRecord? Succession { get; set; }
    public Tally.Selection? Selection { get; set; }
    public List<TallyEntry> Eligible { get; set; } = [];
    public List<TallyEntry> Suppressed { get; set; } = [];

    /// <summary>D30. Candidates held back because the only move for them is a session and a session ran too recently.</summary>
    public List<SelectionHold> CadenceHeld { get; set; } = [];

    /// <summary>D29. Candidates held back because a cycle on the same condition is still being measured.</summary>
    public List<SelectionHold> AwaitingRead { get; set; } = [];

    public bool Cleared { get; set; }
    public string? Posture { get; set; }
}

public sealed class Escalation
{
    public required int Day { get; init; }
    public required string IssueFile { get; init; }
    public string? Issue { get; init; }
    public required int FailedCycles { get; init; }
    public required int DrawCount { get; init; }
    public required List<string> ActionsTried { get; init; }

    /// <summary>
    /// What those actions actually put in front of the team, in the same order (spec 5.1.4).
    /// </summary>
    /// <remarks>
    /// Captured while the cycle is still in hand. An escalation fires days later with
    /// nothing but ids, and resolving a substituted content action back to its text at that
    /// point names the exercise the team was never given.
    /// </remarks>
    public required List<string> ActionsTriedText { get; init; }

    public required int WeeksSpan { get; init; }
}

public sealed class FailedFile
{
    public required string IssueFile { get; init; }
    public string? Issue { get; init; }
    public int Count { get; set; }
    public List<string> ActionsTried { get; } = [];
    public List<(int Day, string ActionId, string Reason)> Declines { get; } = [];
    public Dictionary<string, string> Delivered { get; } = new(StringComparer.Ordinal);
    public required int FirstDay { get; init; }
    public int LastDay { get; set; }
    public bool Escalated { get; set; }
}

public sealed class CycleCounts
{
    public int Closed { get; set; }
    public int Failed { get; set; }
    public int Unattributed { get; set; }
    public int Unread { get; set; }
    public int Running { get; set; }
}

/// <summary>The resolution a signal read produces, which drives the outcome and succession.</summary>
public sealed class Resolution
{
    public required Cycle Cycle { get; init; }
    public required SignalRead Read { get; init; }
    public required Dictionary<string, bool> ClosureConditions { get; init; }
    public required bool Closes { get; init; }
    public required string Outcome { get; init; }
    public required string NextStep { get; init; }
}
