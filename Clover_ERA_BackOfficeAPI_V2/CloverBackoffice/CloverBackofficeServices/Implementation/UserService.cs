using CloverBackoffice.Components;
using CloverBackoffice.Components.Enums;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static CloverBackoffice.Components.BussinessManager;

namespace CloverBackoffice.Services.Implementation
{
    public class UserService : IUserService
    {
        private readonly IUserRepository _userRepository;

        public UserService(IUserRepository userRepository)
        {
            _userRepository = userRepository;

        }
        public async Task<UserDto> UserExistsAsync(string username, string token)
        {
            return await _userRepository.GetByUsername(username, token);

        }

        public async Task<List<UserStatusDto>> GetUserList(int profileId, int loggedInUserId)
        {
            return await _userRepository.GetUserList(profileId, loggedInUserId);
        }

        public async Task<List<UserStatusDto>> GetTeamManagers(int profileId, int loggedInUserId)
        {
            return await _userRepository.GetTeamManagers(profileId, loggedInUserId);
        }

        public async Task<UserDto> CreateUserAsync(string accessToken, User user)
        {
            return await _userRepository.CreateUserAsync(accessToken, user);
        }

        public async Task<UserDto> GetUserInfoById(int UserId)
        {
            return await _userRepository.GetUserInfoById(UserId);
        }
        public async Task<User> UpdateUserGroup(int UserId, int GroupId)
        {
            return await _userRepository.UpdateUserGroup(UserId, GroupId);
        }
        public async Task<bool> AuthenticateAsync(string email, string password)
        {
            return await _userRepository.UserExistsAsync(email, password);
        }
        public async Task<UserInfoResponseDto> GetUserInfoAsync()
        {
            var users = await _userRepository.GetAllUsersAsync();
            var companies = await _userRepository.GetAllCompaniesAsync();

            return new UserInfoResponseDto
            {
                Users = users,
                Profiles = companies
            };
        }
        public async Task<UserInfoResponseDto> GetUserInfoByEmailAsync(string email)
        {
            var user = await _userRepository.GetUserByEmailAsync(email);
            if (user == null)
                return null;

            var company = await _userRepository.GetCompanyByUserEmailAsync(email);

            return new UserInfoResponseDto
            {
                Users = new List<User> { user },
                Profiles = company != null ? new List<Profile> { company } : new List<Profile>()
            };
        }

        public async Task<(bool IsSuccess, string Message)> UpdatePasswordAsync(UpdatePasswordDto dto)
        {
            var user = await _userRepository.GetUserByIdAsync(dto.UserId);
            if (user == null)
                return (false, ResponseMessages.Errors.UserNotFound);

            // Encrypt the provided current password
            var encryptedCurrentPassword = PasswordUtility.Encrypt(dto.currentPassword);

            bool isCorrect = BCrypt.Net.BCrypt.Verify(dto.currentPassword, user.Password);

            // Compare encrypted input with stored encrypted password
            if (!isCorrect)
                return (false, ResponseMessages.Errors.IncorrectOldPassword);

            // Encrypt the new password for comparison
            var encryptedNewPassword = PasswordUtility.Encrypt(dto.newPassword);
            bool isSame = BCrypt.Net.BCrypt.Verify(dto.newPassword, user.Password);

            // Prevent password reuse
            if (isSame)
                return (false, ResponseMessages.Errors.PasswordReuse);

            // Save the new encrypted password
            user.Password = encryptedNewPassword;
            var updated = await _userRepository.UpdateUserAsync(user);

            if (!updated)
                return (false, ResponseMessages.Errors.PasswordUpdateFailed);

            // Invalidate all existing tokens for this user to force re-authentication
            // This ensures that all active sessions (other browsers/devices) are logged out
            await _userRepository.InvalidateUserTokensAsync(user.UserId);

            return (true, ResponseMessages.Success.PasswordUpdated);
        }

        public async Task<List<NotificationDto>> GetNotificationUserByIdAsync(string token)
        {
            return await _userRepository.GetNotificationUserByIdAsync(token);

        }

    }
}