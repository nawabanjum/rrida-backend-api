namespace CloverBackoffice.Services.Interfaces
{
    /// <summary>
    /// Makes sure a team has engine settings, so the engine is on by default.
    ///
    /// Inserts only. Settings an admin has already set — a different working week, or the
    /// engine switched off — are never overwritten.
    /// </summary>
    public interface IEngineTeamProvisioner
    {
        /// <summary>One team. True when a row was created, false when it already had one.</summary>
        Task<bool> EnsureAsync(uint businessUnitId, CancellationToken ct = default);

        /// <summary>Every live team without settings. Returns how many were created.</summary>
        Task<int> EnsureAllAsync(CancellationToken ct = default);
    }
}
