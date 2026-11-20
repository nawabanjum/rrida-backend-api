using CloverBackoffice.Infrastructure.DTO.TeamHealth;

namespace CloverBackoffice.Services.Interfaces
{
    /// <summary>
    /// The Portfolio View, per manager, for a Portfolio Manager or a RRIDA Admin. A null or
    /// 0 managerId is All Managers: for a Portfolio Manager, every manager below them,
    /// directly or through other Portfolio Managers; for a RRIDA Admin, every manager in the
    /// profile they name in profileId, which they must supply. A managerId outside that set
    /// is refused, never answered with someone else's teams. Read only.
    /// </summary>
    public interface IPortfolioViewService
    {
        /// <summary>The managers the dropdown offers.</summary>
        Task<TeamMilestoneResult<PortfolioManagersDto>> GetManagersAsync(int actorUserId, uint? profileId = null, CancellationToken ct = default);

        /// <summary>The teams of the selected manager (or all), with state, weeks and trend.</summary>
        Task<TeamMilestoneResult<PortfolioTeamsDto>> GetTeamsAsync(int actorUserId, int? managerId, uint? profileId = null, CancellationToken ct = default);

        /// <summary>The teams in Headwinds or Stall, for the banner and its Review action.</summary>
        Task<TeamMilestoneResult<PortfolioAlertsDto>> GetAlertsAsync(int actorUserId, int? managerId, uint? profileId = null, CancellationToken ct = default);

        /// <summary>What Eran is told about the scope, and the brief the Eran column opens with.</summary>
        Task<TeamMilestoneResult<PortfolioEranContextDto>> GetEranContextAsync(int actorUserId, int? managerId, uint? profileId = null, CancellationToken ct = default);

        /// <summary>
        /// The system block that grounds an Eran chat turn on the Portfolio View in this scope
        /// (only these teams). Fails exactly as the other calls do for a caller or manager out of scope.
        /// </summary>
        Task<TeamMilestoneResult<string>> BuildEranContextTextAsync(int actorUserId, int? managerId, uint? profileId = null, CancellationToken ct = default);
    }
}
