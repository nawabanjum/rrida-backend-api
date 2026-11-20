using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Repository.Interfaces
{
    public interface ICompanyTeamRepository
    {
        Task<BusinessUnit> GetByIdAsync(int id);
        Task AddAsync(BusinessUnit unit);
        Task UpdateAsync(BusinessUnit unit);
        Task<bool> DeleteAsync(uint id);
        Task<bool> UserExistsAsync(int userId);
        Task<bool> UnitExistsAsync(int UnitId);
        Task AddUserTeamAsync(UserTeam userTeam);
        Task<bool> IsUserAlreadyAssigned(int userId, int teamId);
        Task<UserTeam?> GetUserActiveAssignmentAsync(int userId, int unitId);
        Task<UserTeam?> GetUnitActiveAssignmentAsync(int unitId);
        Task<List<CompanyTeam>> GetTeamsByProfileIdAsync(int profileId);
        Task<UserTeam?> GetUserCurrentAssignmentAsync(int userId);
        Task UpdateUserTeamAsync(UserTeam userTeam);
        Task<UserTeam?> GetUserTeamAsync(long userId, int unitId);
        Task<TeamNotificationDto> GetTeamNotificationsAsync(int profileId, List<int>? filteredUnitIds = null);

    }
}
