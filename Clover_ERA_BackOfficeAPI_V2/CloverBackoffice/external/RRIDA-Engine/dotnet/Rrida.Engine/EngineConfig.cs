using System.Text.Json;

namespace Rrida.Engine;

/// <summary>
/// The constants the engine computes against, injected from <c>kit/data/thresholds.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>There is no default.</b> Every value here is a decision about how the product behaves
/// and none of them is the arithmetic's to make, so the engine takes them and throws
/// without them. That is the same rule the state source and the delivery tracks follow.
/// </para>
/// <para>
/// Three of these are guessed rather than derived and are flagged provisional wherever they
/// appear (build spec 7.4): the cross-team cycle length and the two cross-team thresholds.
/// </para>
/// </remarks>
public sealed class EngineConfig
{
    /// <summary>The rolling window (spec 6.1), counted in the team's own asked days.</summary>
    public required int WindowDays { get; init; }

    /// <summary>The floor's percentage (spec 6.3). 33 per cent of enrolled, rounded down.</summary>
    public required double FloorPercentage { get; init; }

    /// <summary>The floor's absolute minimum (spec 6.3). Two.</summary>
    public required int FloorAbsoluteMinimum { get; init; }

    /// <summary>Simple majority (spec 5.1.1): at or below takes the ask route, above does not.</summary>
    public required double ShareThreshold { get; init; }

    /// <summary>No state before day seven (spec 7.3). Counted in the team's own days.</summary>
    public required int CalibrationDays { get; init; }

    /// <summary>72 hours after delivery, in the team's own days. See the open question in the guide.</summary>
    public required int ConfirmationLag { get; init; }

    /// <summary>The read is the first occurrence at or after the day after confirmation falls due.</summary>
    public required int EarliestReadOffset { get; init; }

    /// <summary>D23. How many times a read may wait for a day with answers before it gives up.</summary>
    public required int MaxReadDeferrals { get; init; }

    /// <summary>Provisional (build spec 7.4). How long a cross-team condition stays open.</summary>
    public required int CrossTeamCycleDays { get; init; }

    /// <summary>Provisional. The same file active in this many child teams at once.</summary>
    public required int CrossTeamActiveTeams { get; init; }

    /// <summary>Provisional. The same file failing in this many child teams.</summary>
    public required int CrossTeamFailingTeams { get; init; }

    /// <summary>Below this many enrolled, a child row's state is withheld (build spec 7.5).</summary>
    public required int RollUpVisibilityFloor { get; init; }

    /// <summary>The cap on a delivered run, in minutes of session time. The data's value wins.</summary>
    public required double RunMinutesCap { get; init; }

    /// <summary>Five working days, which is one week. Governs the weekly message and nothing else (D20).</summary>
    public required int RecalcInterval { get; init; }

    /// <summary>The first day the weekly manager-facing message can land.</summary>
    public required int FirstMessageDay { get; init; }

    public static EngineConfig Load(string thresholdsFile)
    {
        var raw = JsonDocument.Parse(File.ReadAllText(thresholdsFile)).RootElement;
        var e = raw.GetProperty("engine");
        var floor = e.GetProperty("floor");
        return new EngineConfig
        {
            WindowDays = e.GetProperty("windowDays").GetInt32(),
            FloorPercentage = floor.GetProperty("percentage").GetDouble(),
            FloorAbsoluteMinimum = floor.GetProperty("absoluteMinimum").GetInt32(),
            ShareThreshold = e.GetProperty("shareThreshold").GetDouble(),
            CalibrationDays = e.GetProperty("calibrationDays").GetInt32(),
            ConfirmationLag = e.GetProperty("confirmationLag").GetInt32(),
            EarliestReadOffset = e.GetProperty("earliestReadOffset").GetInt32(),
            MaxReadDeferrals = e.GetProperty("maxReadDeferrals").GetInt32(),
            CrossTeamCycleDays = e.GetProperty("crossTeamCycleDays").GetInt32(),
            CrossTeamActiveTeams = e.GetProperty("crossTeamActiveTeams").GetInt32(),
            CrossTeamFailingTeams = e.GetProperty("crossTeamFailingTeams").GetInt32(),
            RollUpVisibilityFloor = e.GetProperty("rollUpVisibilityFloor").GetInt32(),
            RunMinutesCap = e.GetProperty("runMinutesCap").GetDouble(),
            RecalcInterval = e.GetProperty("recalcInterval").GetInt32(),
            FirstMessageDay = e.GetProperty("firstMessageDay").GetInt32(),
        };
    }
}

/// <summary>
/// The loaded data, the rotation, the calendar and the composition-root decisions, in one
/// place. One object to build a run from, so every caller shares a path.
/// </summary>
public sealed class EngineContext
{
    public required DataBank Data { get; init; }
    public required IRotation Rotation { get; init; }
    public required CalendarConfig Calendar { get; init; }
    public required EngineConfig Config { get; init; }

    /// <summary>
    /// Which question tracks may cast a vote and therefore drive delivery.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both tracks are asked every day and both produce atoms, which is what the engine
    /// owes upward under spec 7.1. Delivery is a narrower question: whose answers reach the
    /// tally, and so selection, routing and an action in front of a manager.
    /// </para>
    /// <para>
    /// <b>Team is recorded and not implemented.</b> It waits on the external-locus route,
    /// because 163 of the team bank's negative chips name a cause outside the team and
    /// every one currently routes to a manager-behaviour action, and on the cross-track
    /// tally decision in spec 7.1b. Neither is this build's to decide, so team answers are
    /// recorded, produce atoms and cast nothing. See the developer guide for how to switch
    /// it on when it is ratified.
    /// </para>
    /// </remarks>
    public required IReadOnlyList<string> DeliveryTracks { get; init; }

    /// <summary>
    /// D32. Both tracks deliver. See src/context.js for the reasoning; in short, D31 gave the
    /// team bank's 163 external negatives somewhere to go that nothing selects from, and
    /// spec 7.1b is settled: the tallies are shared and a person saying a thing about
    /// themselves and about the team has said two things, so the signal amplifies rather than
    /// being deduplicated. Sharing the buckets is also what keeps one condition to one
    /// winner to one cycle, which is the thing a manager cannot be given twice.
    /// </summary>
    private static readonly HashSet<string> Implemented =
        new(StringComparer.Ordinal) { "individual", "team" };

    /// <summary>
    /// Halt on a delivery track that is recorded but not implemented.
    /// </summary>
    /// <remarks>
    /// The failure it prevents is silent: a team chip that reaches the tally is two
    /// contributors on one action from two answers, which is the floor for a team of four,
    /// and an action goes out.
    /// </remarks>
    public static IReadOnlyList<string> AssertDeliveryTracks(IReadOnlyList<string> tracks)
    {
        if (tracks.Count == 0) throw new DataException("deliveryTracks must name at least one track.");
        foreach (var track in tracks)
        {
            if (!Vocabulary.Tracks.Contains(track))
            {
                throw new DataException($"deliveryTracks names \"{track}\", which is not a question track.");
            }
            if (!Implemented.Contains(track))
            {
                throw new DataException(
                    $"deliveryTracks names \"{track}\", which is recorded but not implemented as a "
                    + "delivery track. Team delivery waits on the external-locus route and on the "
                    + "cross-track tally decision in spec 7.1b. Team answers are recorded and produce "
                    + "atoms today; they cast no vote.");
            }
        }
        return tracks;
    }

    /// <summary>Build a context from the kit's data directory.</summary>
    public static EngineContext FromKit(string kitDataRoot, IReadOnlyList<string>? deliveryTracks = null)
    {
        var data = DataBank.Load(kitDataRoot);
        var rotation = new Rotation(
            data,
            Path.Combine(kitDataRoot, "rotation", "individual.json"),
            Path.Combine(kitDataRoot, "rotation", "team.json"),
            Path.Combine(kitDataRoot, "schedule.json"));
        var calendar = CalendarConfig.Load(Path.Combine(kitDataRoot, "calendar.json"));
        var config = EngineConfig.Load(Path.Combine(kitDataRoot, "thresholds.json"));
        // The cap is a spec value carried in meta.json; thresholds.json holds the fallback.
        // Where they disagree the data's own value wins, which is what the reference does.
        config = new EngineConfig
        {
            WindowDays = config.WindowDays,
            FloorPercentage = config.FloorPercentage,
            FloorAbsoluteMinimum = config.FloorAbsoluteMinimum,
            ShareThreshold = config.ShareThreshold,
            CalibrationDays = config.CalibrationDays,
            ConfirmationLag = config.ConfirmationLag,
            EarliestReadOffset = config.EarliestReadOffset,
            MaxReadDeferrals = config.MaxReadDeferrals,
            CrossTeamCycleDays = config.CrossTeamCycleDays,
            CrossTeamActiveTeams = config.CrossTeamActiveTeams,
            CrossTeamFailingTeams = config.CrossTeamFailingTeams,
            RollUpVisibilityFloor = config.RollUpVisibilityFloor,
            RunMinutesCap = data.RunMinutesCap,
            RecalcInterval = config.RecalcInterval,
            FirstMessageDay = config.FirstMessageDay,
        };
        return new EngineContext
        {
            Data = data,
            Rotation = rotation,
            Calendar = calendar,
            Config = config,
            // D32. Both tracks by default, matching DELIVERY_TRACKS in src/context.js. The
            // allow-list above says what may be asked for; this says what is asked for when
            // the caller does not say, and the two have to move together or the port runs a
            // different product from the reference.
            DeliveryTracks = AssertDeliveryTracks(deliveryTracks ?? ["individual", "team"]),
        };
    }
}
