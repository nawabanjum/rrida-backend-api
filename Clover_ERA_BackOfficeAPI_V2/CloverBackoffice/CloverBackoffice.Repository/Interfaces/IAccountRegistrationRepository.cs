using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Repository.Interfaces
{
    public interface IAccountRegistrationRepository
    {
        Task<ProfileCreateResponseDto> AddProfileWithUserAsync(ProfileCreateDto dto);
        Task<TrialProfileResponseDto> AddTrialProfileWithUserAsync(TrialProfileCreateDto dto);
        Task<CompanyDto> CompanyExistsAsync(string domainName);
        Task<CompanyDto?> GetCompanyByIdAsync(int id);
        Task<bool> UserExistsAsync(string email, string password);

        Task<Profile> GetCompanyByUserEmailAsync(string email);
        Task<UserDto> GetByUsername(string username);
        Task<ExistingCompany?> GetCompanyByDomainAsync(string domain);
        Task<string> CreateBusinessUnitAsync(int ProfileId, string BussinessUnitName);
        Task<UserCompanyResponseDto> CreateBusinessWithUserAsync(UnitCreateDto dto);
        Task<List<CompanyDto>> GetCompanyListAsync(int userId);
        Task<User?> GetUserAsync(int userId, string username);
        Task<bool> UpdateUserVerificationAsync(User user, bool isVerified, string status);
        Task<LoginDto> UserInfoAsync(string email);
        Task<User> GetUserByEmailAsync(string email);
        Task<User> GetUserByEmailIncludingUnverifiedAsync(string email);
        Task<User> GetUserByEmailForgot(string email);
        Task<bool> UpdateUserPasswordAsync(string email, string newPassword);
        Task<List<BusinessUnitDto>> GetUnitsByProfileIdAsync(long profileId, int userId);
        Task<User?> GetUpsertUserByEmailAsync(string email);

        // Total non-deleted users belonging to a company, INCLUDING the Company
        // Admin auto-created at registration. Drives the firstTimeUserAdded flag:
        // exactly 2 means admin + the first added user.
        Task<int> CountCompanyUsersAsync(int profileId);

        Task AddUserAsync(User user);
        Task UpdateUserAsync(User user);
        Task<User> GetUserByIdAsync(int id);
        // Soft-deletes the user AND unassigns every team they manage. Returns the
        // business unit ids that lost their manager, so the caller can reconcile
        // license state for those units.
        Task<List<uint>> DeleteUserAsync(int id);
        Task<List<UserGroupsDto>> GetAllUserGroupsAsync();

        // Every role, RRIDA Admin included, with its description. The picker above
        // deliberately hides that role; the hierarchy endpoint must show it.
        Task<List<RoleHierarchyDto>> GetAllRolesAsync();
        Task<Profile?> GetByIdAsync(uint profileId);

        Task<ExistingCompany?> GetCompanyByDomainOnlyAsync(string domain);
        Task<CompanyDto> CompanyNameExistsAsync(string CompanyName);
        Task<BusinessUnitDto> BussinessNameExistsAsync(string BussinessName, int ProfileId);
        Task<ContactInfoDto?> ContactEmailExistsAsync(string EmailAddress);

        // Identity-level duplicate check. The organisation contact is now inline on
        // Profiles.Contact* and BusinessUnits.Contact*, so an account created through
        // the add-user endpoint lives in Users with no organisation contact at all.
        // Registration must check Users.Username as well, otherwise those accounts are
        // invisible to the duplicate guard.
        Task<User?> UserEmailExistsAsync(string email);

        Task<bool> EmailExistsAsync(string email);

        // Registration-time duplicate guard across every store an email can land
        // in: the organisation contact on a Profile, the organisation contact on
        // a BusinessUnit, and the Users login table. Also reports whether the
        // matched account has been verified.
        Task<EmailUsageResult> GetEmailUsageAsync(string email);

        Task<UserToken?> GetTokenByUserIdAsync(int userId, string token);
        Task UpdateTokenAsync(UserToken tokenEntity);
        Task<UserToken> GetTokenInfoAsync(int userId, string token);
        Task SaveResetPasswordLink(ResetPasswordLinks model);
        Task<ResetPasswordLinks> GetResetPasswordLink(int userId, string encryptedLink);

        // Marks one link consumed and nothing else.
        //
        // Deliberately NOT UpdateResetPasswordLink, which also writes the
        // password and flips IsVerified — that belongs to the manual-reset flow.
        // The invitation flow has already set the password by the time it calls
        // this, so all it needs is the used marker.
        Task MarkResetPasswordLinkUsedAsync(int id);
        Task UpdateResetPasswordLink(ResetPasswordLinks model, string Password);
        Task<User> GetUserByEmailAndIdAsync(string email, int UserId);
        Task<List<CompanyDto>> GetCompanyPatnerListAsync(int userId);
        Task<List<CompanyDto>> GetAllPartnerCompaniesAsync();
        Task<(bool Success, string Message)> AssignPartnerAsync(int profileId, int partnerId);
        Task<(bool Success, string Message)> UnassignPartnerAsync(int profileId, int partnerId);
        Task IncrementFailedLoginAttemptsAsync(int userId);
        Task ResetFailedLoginAttemptsAsync(int userId);
    }
}
