using CloverBackoffice.Infrastructure.DTO;
using System;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Interfaces
{
    /// <summary>
    /// Team Setup — the business unit, the team type, the manager's context and the
    /// team's goals, written and read as one thing.
    /// </summary>
    public interface ITeamSetupService
    {
        /// <summary>
        /// Creates or updates a team and everything its setup holds, in one transaction.
        /// The goal list in the request is the complete set.
        /// </summary>
        Task<UpsertTeamSetupResultDto> UpsertTeamSetupAsync(UpsertTeamSetupDto dto, int? actorUserId);

        /// <summary>
        /// One team's setup: the unit, its setup row and its live goals in display order.
        /// A team that has never been set up comes back with IsSetupComplete = false.
        /// </summary>
        Task<TeamSetupResultDto> GetTeamSetupByUnitIdAsync(long unitId);

        Task<UpsertTeamSetupResultDto> UpdateTeamAttributesAsync(UpdateTeamAttributesDto dto, int? actorUserId);

        Task<UpsertTeamSetupResultDto> UpdateTeamGoalsAsync(UpdateTeamGoalsDto dto, int? actorUserId);
    }
}
