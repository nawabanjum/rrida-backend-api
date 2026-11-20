using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using Microsoft.Extensions.Primitives;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Interfaces
{
    public interface IUserService
    {
        Task<UserDto> UserExistsAsync(string username, string token);
        Task<List<UserStatusDto>> GetUserList(int profileId, int loggedInUserId);
        Task<List<UserStatusDto>> GetTeamManagers(int profileId, int loggedInUserId);
        Task<UserDto> CreateUserAsync(string accessToken, User user);
        Task<UserDto> GetUserInfoById(int UserId);
        Task<User> UpdateUserGroup(int UserId, int GroupId);
        Task<bool> AuthenticateAsync(string email, string password);
        Task<UserInfoResponseDto> GetUserInfoAsync();
        Task<UserInfoResponseDto> GetUserInfoByEmailAsync(string email);
        Task<(bool IsSuccess, string Message)> UpdatePasswordAsync(UpdatePasswordDto dto);
        Task<List<NotificationDto>> GetNotificationUserByIdAsync(string token);


    }
}
