using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Repository.Interfaces
{

    public interface IUserRepository
    {
        Task<UserDto> GetByUsername(string username, string token);
        Task<UserDto> CreateUserAsync(string accessToken, User user);
        Task<List<UserStatusDto>> GetUserList(int profileId, int loggedInUserId);
        Task<List<UserStatusDto>> GetTeamManagers(int profileId, int loggedInUserId);
        Task<UserDto> GetUserInfoById(int UserId);
        Task<User> UpdateUserGroup(int UserId, int GroupId);
        Task<bool> UserExistsAsync(string email, string password);

        Task<UserInfoResponseDto> GetUserInfoAsync();
        Task<List<Profile>> GetAllCompaniesAsync();
        Task<List<User>> GetAllUsersAsync();

        Task<User> GetUserByEmailAsync(string email);
        Task<Profile> GetCompanyByUserEmailAsync(string email);
        Task<User> GetUserByIdAsync(int userId);
        Task<bool> UpdateUserAsync(User user);
        Task<List<NotificationDto>> GetNotificationUserByIdAsync(string token);
        Task<bool> InvalidateUserTokensAsync(int userId);
    }
}
