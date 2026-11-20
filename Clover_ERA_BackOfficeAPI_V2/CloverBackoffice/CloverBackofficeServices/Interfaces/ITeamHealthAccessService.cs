namespace CloverBackoffice.Services.Interfaces
{
    /// <summary>Role check shared by every Team Health endpoint.</summary>
    public interface ITeamHealthAccessService
    {
        /// <summary>
        /// True when the user may see and manage this team's Team Health data.
        /// <paramref name="unitProfileId"/> is the team's own profile; callers have
        /// already confirmed the team exists.
        /// </summary>
        Task<bool> CanAccessTeamAsync(uint unitId, uint unitProfileId, int userId, CancellationToken ct = default);

        /// <summary>
        /// True only when the user holds the Team Manager role and is assigned to this team
        /// or one above it. Unlike <see cref="CanAccessTeamAsync"/>, no other role passes.
        /// </summary>
        Task<bool> ManagesTeamAsync(uint unitId, uint unitProfileId, int userId, CancellationToken ct = default);
    }
}
