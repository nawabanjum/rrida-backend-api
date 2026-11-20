namespace Rrida.Engine;

/// <summary>
/// OURS, not upstream: the manager-tap replay (step 0 of the day loop, the reference's
/// applyManualConfirmations). Kept in its own file so a re-sync of Simulation.cs carries only
/// the <c>partial</c> keyword and the five-line step-0 call. See VENDORED.md.
/// </summary>
public sealed partial class Simulation
{
    /// <summary>A confirmation the manager actually gave, kept as a fact by the host.</summary>
    public sealed record ManagerConfirmation(string TeamId, string CycleId, int Rung, int Day, bool WorkedIfMet);

    /// <summary>A content action the manager declined, kept as a fact by the host.</summary>
    public sealed record ManagerDecline(string TeamId, string CycleId, int Day, string? Reason);

    /// <summary>
    /// The manager's real confirmations, replayed at the start of the day they were given.
    /// The reference keeps the same journal (<c>manualConfirmations</c>) so a reload
    /// reproduces the run exactly.
    /// </summary>
    public List<ManagerConfirmation> ManagerConfirmations { get; } = [];

    /// <summary>The manager's real declines, replayed the same way as the confirmations.</summary>
    public List<ManagerDecline> ManagerDeclines { get; } = [];

    private readonly HashSet<ManagerConfirmation> _confirmationsApplied = [];
    private readonly HashSet<ManagerDecline> _declinesApplied = [];

    /// <remarks>
    /// The reference matches a tap on its exact day. A tap given on the day its action was
    /// delivered cannot match that way, because at the start of that day the cycle did not
    /// exist yet; the reference loses it on reload. Here it is applied at the start of the
    /// next day the cycle is in the slot, under the day it was given, which is what the
    /// live board did.
    /// </remarks>
    private void ApplyManagerTaps(Team team, int day, List<SimEvent> events)
    {
        var cycle = team.ActiveCycle;
        if (cycle is null || cycle.Confirmed) return;

        var decline = ManagerDeclines.FirstOrDefault(d =>
            d.TeamId == team.Id && d.CycleId == cycle.Id && d.Day <= day && !_declinesApplied.Contains(d));
        if (decline is not null)
        {
            _declinesApplied.Add(decline);
            team.DeclineContent(decline.Day, decline.Reason);
            return;
        }

        var tap = ManagerConfirmations.FirstOrDefault(c =>
            c.TeamId == team.Id && c.CycleId == cycle.Id && c.Rung == cycle.Rung
            && c.Day <= day && !_confirmationsApplied.Contains(c));
        if (tap is null) return;

        _confirmationsApplied.Add(tap);
        team.Confirm(tap.Day, tap.WorkedIfMet, "manager");
        events.Add(new SimEvent
        {
            Type = "confirmed", Day = day, TeamId = team.Id, CycleId = cycle.Id,
            Source = "manager", WorkedIfMet = cycle.WorkedIfMet,
            Label = $"{team.Name}: manager confirmed rung {cycle.Rung}",
        });
    }
}
