using CloverBackoffice.Infrastructure.DTO;
using System;
using System.Threading.Tasks;

namespace CloverBackoffice.Repository.Interfaces
{
    /// <summary>
    /// Team Setup — the business unit, the team type, the manager's context and the
    /// team's goals, written and read as one thing.
    /// </summary>
    public interface ITeamSetupRepository
    {
        /// <summary>
        /// Stores a whole team setup in one transaction: the business unit (created when
        /// <c>UnitId</c> is 0, otherwise updated), its TeamSetups row, its goals, and an
        /// audit snapshot for every goal that was added, changed, reordered or removed.
        ///
        /// The goal list in the request is the complete set. A goal stored before and
        /// absent from it is soft-deleted and audited as a removal.
        ///
        /// A request that fails validation — an unknown company, a unit belonging to
        /// somebody else, too few goals — comes back as a failed result carrying the
        /// reason, not an exception. Nothing is written in that case.
        /// </summary>
        Task<UpsertTeamSetupResultDto> UpsertTeamSetupAsync(UpsertTeamSetupDto dto, int? actorUserId);

        /// <summary>
        /// Everything the setup screen needs for one team: the unit, its setup row and its
        /// live goals in display order.
        ///
        /// A team that exists but has never been through setup is a SUCCESS with
        /// <c>IsSetupComplete = false</c> and no goals — not an error. Only a unit that
        /// does not exist, or has been deleted, fails.
        /// </summary>
        Task<TeamSetupResultDto> GetTeamSetupByUnitIdAsync(long unitId);

        /// <summary>Name, size and team type of an existing team. Context and goals untouched.</summary>
        Task<UpsertTeamSetupResultDto> UpdateTeamAttributesAsync(UpdateTeamAttributesDto dto, int? actorUserId);

        /// <summary>The goal list of an existing team. Unit, type and context untouched.</summary>
        Task<UpsertTeamSetupResultDto> UpdateTeamGoalsAsync(UpdateTeamGoalsDto dto, int? actorUserId);
    }
}
