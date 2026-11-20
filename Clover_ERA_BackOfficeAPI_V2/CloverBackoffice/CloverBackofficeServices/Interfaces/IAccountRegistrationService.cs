using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Interfaces
{
    public interface IAccountRegistrationService
    {
        Task<CompanyDto> CompanyExistsAsync(string domainName);
        Task<ProfileCreateResponseDto> CreateProfileWithUserAsync(ProfileCreateDto dto);
        Task<TrialProfileResponseDto> CreateTrialProfileWithUserAsync(TrialProfileCreateDto dto);
        Task<CompanyDto?> GetCompanyByIdAsync(int id);
        Task<bool> AuthenticateAsync(string email, string password);
        Task<User> GetUserInfoByEmailAsync(string email);
        Task<UserDto> UserExistsAsync(string username);
        Task<DomainValidationResult> IsDomainValidAsync(string input);
        bool IsRegistrationEmailAcceptable(string emailAddress);
        Task<ValidateDomainAndRegisterEmail> checkCompany(string input);
        Task<string> CreateUnitAsync(int profileId, string BussinessUnitName);
        Task<UserCompanyResponseDto> CreateBusinessWithUserAsync(UnitCreateDto dto);
        Task<List<CompanyDto>> GetCompanyListAsync(int userId);
        Task<bool> VerifyUserEmailAsync(VerifyUserEmailDto dto);
        Task<LoginDto> UserInfoAsync(string email);
        Task<(bool IsSuccess, string Message)> ForgotPasswordAsync(string email);
        Task<(bool IsSuccess, string Message)> ResetPasswordAsync(ResetPasswordDto dto);

        // Sends the invitation email and records its link as single-use, so the
        // same link cannot set a password twice. Prefer this over calling
        // BussinessManager.SendEmailToUserAsync directly — that overload still
        // works but mints an unrecorded, reusable link.
        Task<bool> SendInvitationEmailAsync(string email, int userId, string? companyName);

        // Resend Invite: mints a fresh single-use invitation and emails it; the URL
        // comes back too, so it can still be copied when the email fails.
        Task<InvitationLinkResult> ResendInvitationEmailAsync(User user, string? companyName);

        // Copy Invite Link: the user's newest still-valid invitation link, else a
        // new one (recorded single-use, no email).
        Task<InvitationLinkResult> GetOrCreateInvitationLinkAsync(User user);

        // verify-email with an invitation link: checks the link can still be
        // accepted (latest, unused, not expired). Changes nothing.
        Task<(bool IsSuccess, string Message)> ValidateInvitationLinkAsync(string param);
        Task<List<BusinessUnitDto>> GetUnitsByProfileIdAsync(long profileId, int userId);
        Task<UserUpsertDto> UpsertUserAsync(UserUpsertDto dto);
        Task<bool> DeleteUserAsync(int id);
        Task<User?> GetUserByIdAsync(int userId);
        Task UpdateUserAsync(User user);

        /// <summary>Signs the user out everywhere: removes all their access/refresh tokens.</summary>
        Task InvalidateUserTokensAsync(int userId);
        Task<List<UserGroupsDto>> GetUserGroupsAsync(int? userId);

        // GET api/profile/role-hierarchy — the caller's own role and every role
        // beneath it, highest first. Distinct from GetUserGroupsAsync, which is the
        // narrower "which roles may this caller HAND OUT" picker.
        Task<List<RoleHierarchyDto>> GetRoleHierarchyAsync(int userId);
        Task<CompanyDto> CompanyNameExistsAsync(string ComapnyName);
        Task<BusinessUnitDto> BussinessNameExistsAsync(string BussinessName, int ProfileId);
        Task<ContactInfoDto?> ContactEmailExistsAsync(string EmailAddress);

        // Companion to ContactEmailExistsAsync — checks the Users table, which is
        // the real identity/login store. See IAccountRegistrationRepository.
        Task<User?> UserEmailExistsAsync(string email);

        Task<bool> EmailExistsAsync(string email);

        // Reports whether the email is already the contact email of a Company or a
        // BusinessUnit, or a login in Users — and whether that account is verified.
        // See IAccountRegistrationRepository.
        Task<EmailUsageResult> GetEmailUsageAsync(string email);

        Task<bool> LogoutUser(int userId, string token);
        Task<RefreshTokenDto> RefreshToken(int userId, string token);
        Task<string> GenerateManualResetPasswordLink(User user);
        Task<User> GetUserById(int userId);
        Task<(bool IsSuccess, string Message)> UpdateResetPasswordAsync(ResetPasswordDto dto);
        Task<List<CompanyDto>> GetCompanyPatnerListAsync(int userId);
        Task<List<CompanyDto>> GetAllPartnerCompaniesAsync();
        Task<(bool Success, string Message)> AssignPartnerAsync(int profileId, int partnerId);
        Task<(bool Success, string Message)> UnassignPartnerAsync(int profileId, int partnerId);
        Task IncrementFailedLoginAttemptsAsync(int userId);
        Task ResetFailedLoginAttemptsAsync(int userId);
    }
}
