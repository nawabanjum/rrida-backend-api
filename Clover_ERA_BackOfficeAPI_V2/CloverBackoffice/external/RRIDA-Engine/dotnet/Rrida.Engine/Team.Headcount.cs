namespace Rrida.Engine;

/// <summary>
/// A team size in force from <paramref name="FromDay"/> (the team's own day number) until the
/// next period. A null or non-positive size means none is set for those days.
/// </summary>
public sealed record HeadcountPeriod(int FromDay, int? Size);

/// <summary>
/// OURS, not upstream: the headcount the floor and the share are counted against. Kept in its
/// own file so a re-sync of Team.cs, Tally.cs and Simulation.cs carries only the small edits
/// listed in VENDORED.md.
/// </summary>
/// <remarks>
/// Upstream counts the members enrolled on the day. The host enrols a device, not a person,
/// so a reinstall enrols the same person again and the floor (max(2, 33% of enrolled)) rises
/// with every duplicate. The host therefore hands in the team size the manager set, dated so a
/// replay reads each past day against the size in force that day. With no period covering a
/// day, or a period with no size, the count is exactly upstream's: the members enrolled.
/// Which is also why every authored scenario, which sets no headcount, runs unchanged.
/// </remarks>
public sealed partial class Team
{
    private IReadOnlyList<HeadcountPeriod> _headcount = [];

    private void InitHeadcount(TeamConfig config) =>
        _headcount = config.Headcount.OrderBy(p => p.FromDay).ToList();

    /// <summary>
    /// The number of people the floor and the share are counted against on <paramref name="day"/>:
    /// the team size in force that day when one is set, otherwise the members enrolled.
    /// </summary>
    public int EnrolledCountOn(int day)
    {
        HeadcountPeriod? inForce = null;
        foreach (var period in _headcount)
        {
            if (period.FromDay > day) break;
            inForce = period;
        }

        return inForce?.Size is > 0 and var size ? size : EnrolledOn(day).Count;
    }
}
