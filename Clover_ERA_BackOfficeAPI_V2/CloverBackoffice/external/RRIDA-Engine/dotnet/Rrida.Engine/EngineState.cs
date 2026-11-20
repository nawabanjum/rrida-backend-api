namespace Rrida.Engine;

/// <summary>
/// What the engine legitimately owns of section 7: the tally gates and the postures.
/// </summary>
/// <remarks>
/// <para>
/// <b>The state is not computed here.</b> Spec 7.1 draws that boundary and calls it the
/// single most important one in the document: the team state is a RRIDA-level computation,
/// this engine emits the atom and consumes the resolved state as an input.
/// </para>
/// <para>
/// <b>Line 2 is fed by Question 2 and by nothing else.</b> An earlier version of the
/// reference inferred it from the individual stream, reading team conditions out of chip
/// content. Spec 7.1 withdraws that: a derived line 2 is an inference wearing the name of a
/// measurement.
/// </para>
/// </remarks>
/// <summary>D33. The two questions compared over one week, in words and in faces.</summary>
/// <remarks>The faces are for the machinery panel; a manager is given the reading only.</remarks>
public sealed record TrackComparison(
    double? OwnWeek,
    double? Team,
    double? GapInFaces,
    string Reading);

public static class EngineState
{
    /// <summary>
    /// D33. How the two questions compare over the team's own week.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both tracks have been asked from the start and both draw a line on the reference's
    /// board: line 1 is the average of the faces on the individual question, line 2 the
    /// average on the team question, on one scale so they are comparable. Neither has ever
    /// reached the manager's weekly message, which is the one place a manager reads about
    /// their week.
    /// </para>
    /// <para>
    /// The comparison rather than the lines. The lines carry a slope over a 28-day lookback
    /// with a carry-forward for days the team question went unanswered, and that machinery is
    /// display: it lives on the reference's board and this port does not have it or need it.
    /// What a manager needs is the thing the two lines are for.
    /// </para>
    /// <para>
    /// Counted in faces rather than on the level scale, which is why this can sit in
    /// Rrida.Engine at all. The scale is [0, 1/3, 2/3, 1], linear in the smiley index, so a
    /// mean of scale values is a mean of indices divided by three and the two orderings are
    /// identical. The scale is <c>Rrida.State</c>'s config and the engine holds no reference
    /// to it, so taking the index keeps the boundary intact without duplicating a constant
    /// across it. Half a face and a whole face also read better than a sixth.
    /// </para>
    /// <para>In words and never as a figure: the exact values are the machinery panel's.</para>
    /// </remarks>
    public static TrackComparison CompareTracks(Team team, int day, int windowDays)
    {
        var from = team.AskedWindowFrom(day, windowDays);

        double? MeanFace(string track)
        {
            var rows = team.ResponsesBetween(from, day, track);
            if (rows.Count == 0) return null;
            var sum = 0;
            foreach (var row in rows) sum += Vocabulary.SmileyIndex(row.Smiley);
            return (double)sum / rows.Count;
        }

        var own = MeanFace("individual");
        var group = MeanFace("team");
        if (own is null || group is null)
        {
            // Said rather than guessed. A week with no answers on one of the two questions
            // has no comparison in it, and inventing one from the other track would be
            // reporting a gap between a number and nothing.
            var absent = own is null && group is null
                ? "neither question was answered this week"
                : group is null
                    ? "the team question went unanswered this week, so there is nothing to compare"
                    : "the individual question went unanswered this week, so there is nothing to compare";
            return new TrackComparison(own, group, null, absent);
        }

        // Half a face. Less than that is the same answer twice and not a difference worth a
        // manager's attention; a whole face is people answering one step apart about the two
        // things, which is as much as this instrument can say.
        var gap = group.Value - own.Value;
        var size = Math.Abs(gap);
        var reading = size < 0.5
            ? "the two questions are saying the same thing: how people rate the team and how "
              + "they rate their own week are in the same place"
            : gap > 0
                ? "people rate the team better than they rate their own week"
                  + (size >= 1 ? ", and the difference is wide" : "")
                : "people rate their own week better than they rate the team"
                  + (size >= 1 ? ", and the difference is wide" : "");

        return new TrackComparison(own, group, gap, reading);
    }

    /// <summary>
    /// Which tally the state gates as live (spec 7.4).
    /// </summary>
    /// <remarks>
    /// <b>Headwinds is not here, and that is the whole change.</b> It was a state and is now
    /// a flag (D19), raised whenever the horizontal answer is external and independent of
    /// the vertical, so it cannot gate the tally by name: a team can be Stall and flagged at
    /// once, and on a large team with external causes that is the usual condition rather
    /// than an edge case. What the flag does instead is filter (D21).
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, string> LiveTally =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Cruise"] = "build", ["Drift"] = "fix", ["Stall"] = "yellow",
        };

    public static readonly IReadOnlyDictionary<string, string> NoFloorPosture =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Cruise"] = "Maintain and monitor. No action delivered.",
            ["Stall"] = "Priority intervention. Calm tone. Draw from the Yellow set.",
            ["Drift"] = "Monitor and check in. Something is moving without a clear source.",
        };
}

/// <summary>
/// What the resolver hands the engine. Spec 7.1: an input, not an output.
/// </summary>
/// <remarks>
/// This type lives in the engine because the engine has to name what it consumes. It knows
/// nothing about how the values were computed, and the engine holds no reference to
/// anything that could compute them.
/// </remarks>
public sealed class StateApplication
{
    public required int Day { get; init; }

    /// <summary>Cruise, Drift or Stall. The engine keys the tally on this alone (D21).</summary>
    public required string State { get; init; }

    /// <summary>The whole resolved record, opaque to the engine.</summary>
    public object? Resolved { get; init; }

    /// <summary>D21. The Headwinds flag, as a filter. Null means no filter, and it is said.</summary>
    public LocusFilter? LocusFilter { get; init; }

    public bool Changed { get; init; }
    public string? StateBefore { get; init; }
    public string? Reason { get; init; }

    /// <summary>What the weekly and monthly messages read: "positive"/"negative", and the horizontal.</summary>
    public string? VerticalLeaning { get; init; }

    public string? HorizontalLeaning { get; init; }
}

/// <summary>Where a resolved state comes from. The engine takes this by injection.</summary>
/// <remarks>
/// Anything implementing this will do, which is what lets a scenario pin a state and the
/// real RRIDA service drop in later. <b>There is no default.</b> The engine does not reach
/// for a state computation even to fall back on one.
/// </remarks>
public interface IStateSource
{
    StateApplication? Resolve(EngineContext ctx, Team team, int day);
}

/// <summary>One payload handed to Eran. The prose it writes back is not the layer's.</summary>
public sealed class FeedItem
{
    public required string Id { get; init; }
    public required string TeamId { get; init; }
    public required int Day { get; init; }
    public required string Kind { get; init; }
    public string? CycleId { get; init; }
    public int? Rung { get; init; }
    public string? Situation { get; init; }
    public bool Reminder { get; init; }
    public string? NoteType { get; init; }
    public bool NoteTypeDerived { get; init; }
    public string? NoteTypeReason { get; init; }
    public string? Month { get; init; }
    public required Dictionary<string, object?> Payload { get; init; }
}

/// <summary>A senior-level pattern across child teams (build spec 7.4). Provisional.</summary>
public sealed class CrossTeamCondition
{
    public required int Day { get; init; }
    public required string IssueFile { get; init; }
    public required string Issue { get; init; }
    public required List<string> ChildTeams { get; init; }
    public required string Basis { get; init; }
    public bool Confirmed { get; set; }
    public int? ConfirmedDay { get; set; }
    public string? ConfirmationSource { get; set; }
    public int? ResolvedDay { get; set; }
    public string? Outcome { get; set; }
    public bool Provisional => true;
}

/// <summary>An event a presenter can step between (build spec 8).</summary>
public sealed class SimEvent
{
    public required string Type { get; init; }
    public required int Day { get; init; }
    public required string TeamId { get; init; }
    public string? CycleId { get; init; }
    public int? Rung { get; init; }
    public bool? RungTwo { get; init; }
    public bool? Moved { get; init; }
    public string? Outcome { get; init; }
    public string? Source { get; init; }
    public bool? WorkedIfMet { get; init; }
    public string? StateBefore { get; init; }
    public string? StateAfter { get; init; }
    public bool? Changed { get; init; }
    public bool? FloorCleared { get; set; }
    public string? Posture { get; set; }
    public string? IssueFile { get; init; }
    public string? Month { get; init; }
    public bool? Provisional { get; init; }
    public string? FeedId { get; init; }
    public required string Label { get; init; }
}
