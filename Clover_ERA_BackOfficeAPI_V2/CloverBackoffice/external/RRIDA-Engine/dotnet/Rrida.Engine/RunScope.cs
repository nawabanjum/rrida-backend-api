namespace Rrida.Engine;

/// <summary>
/// The counters that number one run's cycles and messages.
/// </summary>
/// <remarks>
/// <para>
/// <b>Per run, not per process.</b> The reference holds these as module-level variables,
/// which is correct there because it runs one simulation at a time in one process. It is not
/// correct in a server: two runs in flight at once would interleave their ids, and a run
/// that happened to start second would number its first cycle C83 rather than C1. That is
/// the sort of difference nothing crashes on and every comparison then disagrees about.
/// </para>
/// <para>
/// So the counters live on an object the run owns. A <see cref="Simulation"/> creates one
/// and hands it to every team it builds. A team built on its own gets its own, which is
/// right: it is its own run.
/// </para>
/// <para>
/// Ids are stable across runs of the same scenarios in the same order, which is what lets a
/// fixture record them and a port reproduce them.
/// </para>
/// </remarks>
public sealed class RunScope
{
    private int _cycles;
    private int _feedItems;

    public string NextCycleId() => $"C{++_cycles}";
    public string NextFeedId() => $"F{++_feedItems}";

    /// <summary>How many cycles this run has opened, across every team in it.</summary>
    public int CyclesOpened => _cycles;

    /// <summary>How many messages this run has handed Eran, across every team in it.</summary>
    public int MessagesSent => _feedItems;
}
