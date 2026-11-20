using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Interfaces
{
    public interface ICompanyTeamService
    {
        Task<UpsertBusinessUnitDto> UpsertBusinessUnitAsync(UpsertBusinessUnitDto dto);
        Task<bool> DeleteAsync(uint id, int? actorUserId = null);
        Task<UserTeam?> AssignUserToTeamAsync(UserTeamDto dto, int createdBy);
        Task<GetCompanyUnitsWithHierarchyDto?> GetCompanyUnitsWithHierarchy(int profileId, int userId);
        Task<UserTeam?> UnAssignUserFromTeamAsync(long userId, int unitId);
        Task<TeamNotificationDto> GetTeamNotificationsAsync(int profileId, int userId);
        Task<Dictionary<uint, TeamLicenceDto>> GetTeamLicencesAsync(int profileId, IEnumerable<uint> unitIds);

    }
}
