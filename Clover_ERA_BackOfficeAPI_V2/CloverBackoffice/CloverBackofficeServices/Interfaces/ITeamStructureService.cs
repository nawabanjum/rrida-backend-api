using CloverBackoffice.Infrastructure.DTO;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Interfaces
{
    // Manager-centric Team Structure page: who runs which team, and drag a team
    // onto another Team Manager.
    public interface ITeamStructureService
    {
        // Null when the profile does not exist.
        Task<TeamStructureDto?> GetTeamStructureAsync(int profileId);

        // Assigns the team to the Team Manager, moving it from its current manager
        // if it has one. Error is the message to return when the move is refused.
        Task<(TeamAssignmentResultDto? Result, string? Error)> AssignTeamAsync(AssignTeamManagerDto dto, int actorUserId);

        // Moves a manager under another manager (who becomes their Portfolio
        // Manager) or back under the company. Their own reports and teams move
        // with them. Error is the message to return when the move is refused.
        Task<(ManagerMoveResultDto? Result, string? Error)> MoveManagerAsync(MoveManagerDto dto, int actorUserId);
    }
}
