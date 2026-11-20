using CloverBackoffice.Components;
using CloverBackoffice.Components.Enums;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.BusinessModel;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Implementation;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static CloverBackoffice.Components.BussinessManager;

namespace CloverBackoffice.Services.Implementation
{
    public class AccountRegistrationService : IAccountRegistrationService
    {
        private readonly IAccountRegistrationRepository _repository;
        private readonly DomainValidator _domainValidator;
        private readonly IConfiguration _config;
        private readonly ApplicationDbContext _context;
        public readonly SendGridSettings _sendGrid;
        private readonly TokenReaderService _tokenReaderService;
        private readonly JwtTokenGenerator _tokenGenerator;
        private readonly IUserRepository _userRepository;
        private readonly ICompanyLicenseService _licenseService;
        private readonly ITeamManagerHandoverService _handover;
        private readonly ILogger<AccountRegistrationService> _logger;

        // How long a password link stays usable, by what minted it.
        //
        // Forgot-password is a reply to something the user did seconds ago, so
        // it stays short. An invitation is unsolicited and its recipient has no
        // reason to be waiting for it; 3 days matches the UserToken registration
        // already mints for a new account, so the two halves of accepting an
        // invitation expire together.
        // Users.Status is a stringified UserStatus; "1" is Active.
        private static readonly string ActiveStatus = ((int)UserStatus.Active).ToString();
        private static readonly string SuspendedStatus = ((int)UserStatus.Suspend).ToString();

        private static readonly TimeSpan ForgotPasswordLinkValidity = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan InvitationLinkValidity = TimeSpan.FromDays(3);

        /// <summary>
        /// The role hierarchy, highest first. THE definition of rank in this
        /// service — the ids cannot supply it (1 Profile Admin outranks 2 Team
        /// Manager, and 3 RRIDA Admin outranks both), and neither can the
        /// UserGroups table, which stores no rank column.
        ///
        /// A role added later belongs in this array at its rank, not appended.
        /// </summary>
        private static readonly UserGroups[] RoleHierarchyHighestFirst =
        {
            UserGroups.RridaAdmin,
            UserGroups.ProfileAdmin,
            UserGroups.TeamManager,
            UserGroups.PortfolioManager
        };

        public AccountRegistrationService(JwtTokenGenerator tokenGenerator, IOptions<SendGridSettings> sendGridDto, IConfiguration config, IAccountRegistrationRepository repository, ApplicationDbContext context, IUserRepository userRepository, ICompanyLicenseService licenseService, ITeamManagerHandoverService handover, ILogger<AccountRegistrationService> logger)
        {
            _handover = handover;
            _repository = repository;
            _licenseService = licenseService;
            _logger = logger;
            _domainValidator = new DomainValidator(
                "wwwroot/JsonFile/CompanyDomains.json",
                IsDomainBlacklistEnabled(config));
            _config = config;
            _sendGrid = sendGridDto.Value;
            _context = context;
            _tokenGenerator = tokenGenerator;
            _userRepository = userRepository;

        }

        // Disposable-domain blacklist (wwwroot/JsonFile/CompanyDomains.json) is a
        // PRODUCTION-only guard. Development and UAT keep it off so QA can register
        // with yopmail.com and the other blocked test domains.
        //
        // Resolution order:
        //   1. "DomainValidation:Enabled" — explicit override. Set it to force the
        //      blacklist on or off in any environment (e.g. to smoke-test the block
        //      in UAT, or to switch it off in prod without a redeploy).
        //   2. "Environment:Name" — the project's existing environment switch. The
        //      mapping below is deliberately identical to the one Program.cs uses to
        //      pick per-environment Stripe secrets, so both read the same way.
        //
        // Note: appsettings.Production.json ships with Environment:Name set to
        // "Development"; the real value is supplied per deployment via Azure App
        // Settings. That is why this keys off Environment:Name rather than
        // ASPNETCORE_ENVIRONMENT / the appsettings file name.
        private static bool IsDomainBlacklistEnabled(IConfiguration config)
        {
            var explicitFlag = config.GetValue<bool?>("DomainValidation:Enabled");
            if (explicitFlag.HasValue)
                return explicitFlag.Value;

            return (config["Environment:Name"] ?? string.Empty)
                .Trim().ToUpperInvariant() switch
            {
                "LIVE" or "LIVE2" or "PROD" or "PRODUCTION" => true,
                _ => false // Development / UAT / local / anything unset
            };
        }

        public Task<ProfileCreateResponseDto> CreateProfileWithUserAsync(ProfileCreateDto dto)
            => _repository.AddProfileWithUserAsync(dto);

        public Task<TrialProfileResponseDto> CreateTrialProfileWithUserAsync(TrialProfileCreateDto dto)
            => _repository.AddTrialProfileWithUserAsync(dto);

        public async Task<UserCompanyResponseDto> CreateBusinessWithUserAsync(UnitCreateDto dto)
        {
            return await _repository.CreateBusinessWithUserAsync(dto);
        }


        public async Task<CompanyDto> CompanyNameExistsAsync(string ComapnyName)
        {
            return await _repository.CompanyNameExistsAsync(ComapnyName.Trim().ToLower());
        }

        public async Task<BusinessUnitDto> BussinessNameExistsAsync(string BussinessName, int ProfileId)
        {
            return await _repository.BussinessNameExistsAsync(BussinessName.Trim().ToLower(), ProfileId);
        }

        public async Task<ContactInfoDto?> ContactEmailExistsAsync(string EmailAddress)
        {
            return await _repository.ContactEmailExistsAsync(EmailAddress.Trim().ToLower());
        }

        public async Task<User?> UserEmailExistsAsync(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return null;

            return await _repository.UserEmailExistsAsync(email.Trim().ToLower());
        }

        public async Task<CompanyDto> CompanyExistsAsync(string domainName)
        {
            return await _repository.CompanyExistsAsync(domainName.Trim().ToLower());
        }


        public async Task<List<CompanyDto>> GetCompanyListAsync(int userId)
        {
            return await _repository.GetCompanyListAsync(userId);
        }
        public async Task<CompanyDto?> GetCompanyByIdAsync(int id)
        {
            return await _repository.GetCompanyByIdAsync(id);
        }
        public async Task<bool> AuthenticateAsync(string email, string password)
        {
            return await _repository.UserExistsAsync(email, password);
        }
        public async Task<User> GetUserInfoByEmailAsync(string email)
        {
            // Login gate: resolve the user even when unverified so the caller can
            // distinguish "not registered" from "email not verified" and block
            // login with the correct message until verification completes.
            return await _repository.GetUserByEmailIncludingUnverifiedAsync(email);
        }

        public async Task<LoginDto> UserInfoAsync(string email)
        {
            return await _repository.UserInfoAsync(email);
        }

        public async Task<UserDto> UserExistsAsync(string username)
        {
            return await _repository.GetByUsername(username);
        }
        public async Task<DomainValidationResult> IsDomainValidAsync(string input)
        {
            if (!input.Contains("@"))
                return new DomainValidationResult { IsValid = false };

            var parts = input.Split('@');
            if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
                return new DomainValidationResult { IsValid = false };

            string domain = parts[1].ToLower();

            // Validate domain against allowed list
            if (_domainValidator.IsDomainAllowed(domain))
                return new DomainValidationResult { IsValid = false };

            // Check for domain in Companies
            var existingCompany = await _repository.GetCompanyByDomainAsync(input);
            if (existingCompany == null)
            {
                existingCompany = await _repository.GetCompanyByDomainOnlyAsync(domain);
                if (existingCompany != null)
                {
                    return new DomainValidationResult
                    {
                        IsValid = true,
                        ExistsInDb = false,
                        ExistingCompany = existingCompany,
                        ProfileId = existingCompany.Id
                    };
                }

            }
            // Check if user exists with this email
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == input.ToLower());
            uint? profileId = user?.ProfileId != null ? (uint?)user.ProfileId : null;

            return new DomainValidationResult
            {
                IsValid = true,
                ExistsInDb = existingCompany != null,
                ExistingCompany = existingCompany,
                ProfileId = profileId
            };
        }

        public async Task<string> CreateUnitAsync(int profileId, string BussinessUnitName)
        {
            // 1. Ensure the Business Unit exists or create it
            var unit = await _repository.CreateBusinessUnitAsync(profileId, BussinessUnitName);

            return "Success";
        }

        public async Task<bool> VerifyUserEmailAsync(VerifyUserEmailDto dto)
        {
            var user = await _repository.GetUserAsync(dto.UserId, dto.Email);
            if (user == null)
                return false;

            // Verifying the email never re-activates a user a Profile Admin made inactive.
            var status = user.Status == SuspendedStatus ? SuspendedStatus : ActiveStatus;
            return await _repository.UpdateUserVerificationAsync(user, true, status);
        }

        public async Task<(bool IsSuccess, string Message)> ForgotPasswordAsync(string email)
        {
            var user = await _repository.GetUserByEmailForgot(email);

            if (user == null)
                return (false, ResponseMessages.Errors.InvalidEmail);

            if (!user.IsVerified)
                return (false, ResponseMessages.Warnings.EmailNotVerified);

            var payload = new ResetPasswordPayload
            {
                Email = user.Username,
                UserId = user.UserId,
                RequestTime = DateTime.UtcNow
            };
            var encrypted = BussinessManager.EncryptObject(payload);
            var baseUrl = _sendGrid.FrontendBaseUrl;
            var resetLink = $"{baseUrl}/resetPassword?data={encrypted}";


            var emailSent = await EmailHelper.SendEmailAsync(
                _sendGrid,
                user.Username,
                "Reset Your Password \u2013 RRIDA",
                "PasswordReset",
                _sendGrid.ResetPasswordTemplateId,
                new Dictionary<string, object>
                {
                    { "reset_link", resetLink },
                    { "username", user.Username }
                });

            return emailSent
                ? (true, ResponseMessages.Success.Linkemail)
                : (false, ResponseMessages.Errors.Failedemail);
        }


        public async Task<(bool IsSuccess, string Message)> ResetPasswordAsync(ResetPasswordDto dto)
        {
            ResetPasswordPayload decrypted;
            try
            {
                decrypted = BussinessManager.DecryptObject<ResetPasswordPayload>(dto.Param);
            }
            catch (FormatException)
            {
                return (false, ResponseMessages.Errors.InvalidLink);
            }
            catch (Exception)
            {
                return (false, ResponseMessages.General.ResetFailed);
            }
            if (decrypted.IsManuallyReset)
            {
                var resp = UpdateResetPasswordAsync(dto);
                return (resp.Result.IsSuccess, resp.Result.Message);
            }
            // An INVITATION is the one password flow whose user is, by definition,
            // not verified yet: create-manager and upsert-user both create the
            // account unverified and the invitation is what proves the mailbox.
            // GetUserByEmailAsync filters IsVerified == true, so using it here
            // returned null for every invited user and the whole flow answered
            // "Invalid or expired reset request." — the invitation could never be
            // accepted. Forgot-password keeps the verified-only lookup, where
            // refusing an unverified account is the intended behaviour.
            var user = decrypted.IsInvitation
                ? await _repository.GetUserByEmailIncludingUnverifiedAsync(decrypted.Email)
                : await _repository.GetUserByEmailAsync(decrypted.Email);

            if (user == null || user.UserId != decrypted.UserId)
                return (false, ResponseMessages.Warnings.InvalidLinkReset);

            // Single use — invitations only.
            //
            // A recorded link that has already been consumed is refused, so an
            // invitation cannot set a password twice. A link with NO record is
            // allowed through: every invitation minted before this existed has
            // none, and refusing those would lock out people holding one right
            // now. Forgot-password links never reach this branch.
            //
            // Latest only: a new invitation supersedes every earlier one
            // (CheckInvitationLinkAsync).
            ResetPasswordLinks? invitationLink = null;
            if (decrypted.IsInvitation)
            {
                var (link, error) = await CheckInvitationLinkAsync(decrypted, dto.Param);
                if (error != null)
                    return (false, error);
                invitationLink = link;
            }

            // An invitation and a forgot-password link arrive here as the same
            // payload, but they are not the same thing. Forgot-password answers
            // something the user did seconds ago, so 30 minutes is right. An
            // invitation is unsolicited — the recipient may open it the next
            // morning — and its window matches the 3-day UserToken registration
            // already mints for a new account. Before this, an invited manager
            // who did not act within half an hour could never set a password.
            var validFor = decrypted.IsInvitation
                ? InvitationLinkValidity
                : ForgotPasswordLinkValidity;

            var isExpired = decrypted.RequestTime.Add(validFor) < DateTime.UtcNow;
            if (isExpired)
                return (false, ResponseMessages.General.ResetExpired);

            var updated = await _repository.UpdateUserPasswordAsync(decrypted.Email, dto.NewPassword);
            if (!updated)
                return (false, ResponseMessages.Errors.PasswordUpdateFailed);

            // Accepting an invitation IS the email verification: the recipient
            // proved they hold the mailbox by following a link only that mailbox
            // received. Without this the account stays Pending and unverified, so
            // the manager sets a password and then cannot log in with it.
            //
            // Forgot-password never reaches this branch, so an unverified account
            // cannot be verified by resetting a password it never had.
            // An inactive (suspended) user is verified but stays inactive: only a
            // Profile Admin re-activates them (update-user-status).
            if (decrypted.IsInvitation && (!user.IsVerified || user.Status != ActiveStatus))
            {
                user.IsVerified = true;
                if (user.Status != SuspendedStatus)
                    user.Status = ActiveStatus;
                user.ModifiedDate = DateTime.UtcNow.ToString("o");
                await _repository.UpdateUserAsync(user);
            }

            // Invalidate all existing tokens for this user to force re-authentication
            // This ensures that all active sessions (other browsers/devices) are logged out
            await _userRepository.InvalidateUserTokensAsync(decrypted.UserId);

            // Consumed. Only now, so a failed password write leaves the link usable.
            if (invitationLink != null)
                await _repository.MarkResetPasswordLinkUsedAsync(invitationLink.Id);

            // The invitation is accepted: a Team Manager replacement that was
            // waiting on this user takes effect now, and only now. Never throws.
            if (decrypted.IsInvitation)
                await _handover.FinalizePendingForUserAsync(user.UserId);

            return (true, ResponseMessages.Success.PasswordReset);
        }

        /// <summary>
        /// Sends the invitation email, and records its link so it can only be
        /// used once.
        ///
        /// The parameter is minted first, stored as an unused ResetPasswordLinks
        /// row, and only then emailed — so the link is single-use from the moment
        /// it is clickable. <c>ResetPasswordAsync</c> consumes the row.
        ///
        /// Recording it is BEST-EFFORT and deliberately cannot fail the send: if
        /// the row cannot be written the invitation still goes out, and that link
        /// simply stays reusable until it expires, which is what every invitation
        /// did before this method existed. Losing single-use is a far smaller
        /// problem than a manager who never receives an invitation at all.
        /// </summary>
        public async Task<bool> SendInvitationEmailAsync(string email, int userId, string? companyName)
        {
            var invitationParam = await MintInvitationParamAsync(email, userId);

            return await BussinessManager.SendEmailToUserAsync(
                _sendGrid, email, userId, 0, companyName ?? string.Empty, invitationParam);
        }

        // Resend Invite: a fresh invitation (new 3-day window, single-use) emailed
        // exactly as upsert-user sends it. The URL is returned either way so the
        // admin can still copy it when the email fails.
        public async Task<InvitationLinkResult> ResendInvitationEmailAsync(User user, string? companyName)
        {
            var invitationParam = await MintInvitationParamAsync(user.Username, user.UserId);

            // A provider outage throws rather than returning false; either way the
            // caller gets "not sent" and the link, never a bare 500.
            bool sent;
            try
            {
                sent = await BussinessManager.SendEmailToUserAsync(
                    _sendGrid, user.Username, user.UserId, 0, companyName ?? string.Empty, invitationParam);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Invitation email for user {UserId} could not be sent.", user.UserId);
                sent = false;
            }

            return new InvitationLinkResult(BuildInvitationUrl(invitationParam), DateTime.UtcNow.Add(InvitationLinkValidity), sent);
        }

        // Copy Invite Link: the user's latest invitation link, when it can still be
        // accepted (unused, not revoked, not expired, minted for their current
        // email). Only the latest — reset-password refuses any older one. Otherwise
        // a new one is minted and recorded (revoking the rest), no email.
        public async Task<InvitationLinkResult> GetOrCreateInvitationLinkAsync(User user)
        {
            var now = DateTime.UtcNow;

            var latest = await InvitationLinkRows(user.UserId)
                .OrderByDescending(l => l.CreatedAt)
                .ThenByDescending(l => l.Id)
                .FirstOrDefaultAsync();

            if (latest != null && !latest.IsDel && !latest.IsLinkUsed && latest.ExpiryDateTime > now)
            {
                ResetPasswordPayload? payload = null;
                try
                {
                    payload = BussinessManager.DecryptObject<ResetPasswordPayload>(latest.ResetLink);
                }
                catch
                {
                    // Unreadable — mint a new one below.
                }

                // Same expiry test ResetPasswordAsync applies when the link is accepted.
                var expiresAt = payload?.RequestTime.Add(InvitationLinkValidity);
                if (payload != null
                    && payload.IsInvitation
                    && payload.UserId == user.UserId
                    && string.Equals(payload.Email, user.Username, StringComparison.OrdinalIgnoreCase)
                    && expiresAt > now)
                {
                    return new InvitationLinkResult(BuildInvitationUrl(latest.ResetLink), expiresAt.Value, null);
                }
            }

            var newParam = await MintInvitationParamAsync(user.Username, user.UserId);
            return new InvitationLinkResult(BuildInvitationUrl(newParam), now.Add(InvitationLinkValidity), null);
        }

        // Accept-invite page load (verify-email with an invitation link): the link
        // must still be acceptable, under exactly the rules reset-password applies.
        // Changes nothing — the account is verified and made active only when the
        // password is set (ResetPasswordAsync), so opening an old, used or
        // superseded link can no longer activate anyone.
        public async Task<(bool IsSuccess, string Message)> ValidateInvitationLinkAsync(string param)
        {
            ResetPasswordPayload decrypted;
            try
            {
                decrypted = BussinessManager.DecryptObject<ResetPasswordPayload>(param);
            }
            catch (Exception)
            {
                return (false, ResponseMessages.Errors.InvalidLink);
            }

            if (decrypted == null || !decrypted.IsInvitation)
                return (false, ResponseMessages.Errors.InvalidLink);

            var user = await _repository.GetUserByEmailIncludingUnverifiedAsync(decrypted.Email);
            if (user == null || user.UserId != decrypted.UserId)
                return (false, ResponseMessages.Warnings.InvalidLinkReset);

            var (_, error) = await CheckInvitationLinkAsync(decrypted, param);
            return error == null
                ? (true, ResponseMessages.Success.EmailVerified)
                : (false, error);
        }

        // Whether an invitation link can still be accepted. Refused when it was
        // already used, revoked by a later mint, or older than any recorded
        // invitation (this also catches unrecorded legacy links once a newer one
        // exists — the newer-row test is the real guard, revocation alone could
        // miss a link whose own row failed to record), or expired. Returns its
        // recorded row (null for an unrecorded link) or the refusal message.
        private async Task<(ResetPasswordLinks? Link, string? Error)> CheckInvitationLinkAsync(
            ResetPasswordPayload decrypted, string param)
        {
            var invitationRows = await InvitationLinkRows(decrypted.UserId).ToListAsync();

            var link = invitationRows.FirstOrDefault(l => l.ResetLink == param);
            if (link != null && link.IsLinkUsed)
                return (link, ResponseMessages.Errors.LinkAlreadyUsed);

            var superseded = (link != null && link.IsDel)
                || invitationRows.Any(l => l.Id != link?.Id && l.CreatedAt > decrypted.RequestTime);
            if (superseded)
                return (link, ResponseMessages.Errors.InvitationLinkSuperseded);

            if (decrypted.RequestTime.Add(InvitationLinkValidity) < DateTime.UtcNow)
                return (link, ResponseMessages.General.ResetExpired);

            return (link, null);
        }

        // Every recorded invitation link of a user, revoked and used ones included.
        // Manual set-password links store the whole URL; invitations store the bare param.
        private IQueryable<ResetPasswordLinks> InvitationLinkRows(int userId)
            => _context.ResetPasswordLinks
                .AsNoTracking()
                .Where(l => l.UserId == userId && l.ResetLink != null && !l.ResetLink.Contains("data="));

        // The URL the invitation email carries (BussinessManager.SendEmailToUserAsync).
        private string BuildInvitationUrl(string invitationParam)
            => $"{_sendGrid.FrontendBaseUrl}/acceptInvite?data={invitationParam}";

        // Mints an invitation param and records it as an unused single-use link.
        private async Task<string> MintInvitationParamAsync(string email, int userId)
        {
            var mintedAt = DateTime.UtcNow;
            var invitationParam = BussinessManager.BuildInvitationParam(email, userId);

            try
            {
                // Kind=Utc, and it matters. EF maps CreatedAt / CreatedBy /
                // ExpiryDateTime as `timestamp with time zone`, which Npgsql will
                // only accept a UTC DateTime for. An earlier comment here claimed
                // the opposite and used SpecifyKind(Unspecified); the result was
                // that EVERY invitation link failed to record with
                //
                //   Cannot write DateTime with Kind=Unspecified to PostgreSQL type
                //   'timestamp with time zone', only UTC is supported
                //
                // swallowed by the catch below, so invitations silently stopped
                // being single-use. GenerateManualResetLink always used UtcNow and
                // always worked, which is what the two paths differed by.
                var recordedAt = DateTime.UtcNow;

                await _repository.SaveResetPasswordLink(new ResetPasswordLinks
                {
                    UserId = userId,
                    ResetLink = invitationParam,
                    IsLinkUsed = false,
                    IsDel = false,
                    CreatedAt = recordedAt,

                    // CreatedBy is a DateTime on this table, not a user id. Kept
                    // consistent with CreatedAt rather than left at MinValue.
                    CreatedBy = recordedAt,
                    ExpiryDateTime = DateTime.UtcNow.Add(InvitationLinkValidity)
                });
            }
            catch (Exception ex)
            {
                // Still swallowed — an unsendable invitation is worse than an
                // unrecorded one — but no longer SILENT. Every invitation link was
                // failing to record here and nothing said so, which quietly turned
                // single-use invitations back into reusable ones: ResetPasswordAsync
                // lets a link with no record through on purpose, so a failure here
                // does not deny anyone access, it just stops being enforced.
                _logger.LogError(ex,
                    "Invitation link for user {UserId} could not be recorded; the link will not be single-use.",
                    userId);
            }

            // Only the latest invitation stays usable: revoke every earlier unused
            // one (IsDel), whether or not the new row recorded. ResetPasswordAsync
            // refuses revoked and older links. Only rows minted before this one, so
            // two resends at once never revoke each other.
            try
            {
                await _context.ResetPasswordLinks
                    .Where(l => l.UserId == userId
                        && l.CreatedAt < mintedAt
                        && !l.IsDel
                        && !l.IsLinkUsed
                        && l.ResetLink != null
                        && !l.ResetLink.Contains("data=")
                        && l.ResetLink != invitationParam)
                    .ExecuteUpdateAsync(s => s.SetProperty(l => l.IsDel, true));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Earlier invitation links for user {UserId} could not be revoked; reset-password still refuses them once a newer one is recorded.",
                    userId);
            }

            return invitationParam;
        }

        public async Task<List<BusinessUnitDto>> GetUnitsByProfileIdAsync(long profileId, int userId)
        {
            return await _repository.GetUnitsByProfileIdAsync(profileId, userId);
        }

        public async Task<UserUpsertDto> UpsertUserAsync(UserUpsertDto dto)
        {
            try
            {
                User user = new User();

                // First, check if there's a deleted user with the same email (IsDel = true)
                // This bypasses any global query filters by explicitly checking IsDel
                var deletedUser = await _context.Users
                    .FirstOrDefaultAsync(u => u.Username.ToLower() == dto.Email.ToLower() && u.IsDel == true);

                if (dto.UserId == 0 && deletedUser != null)
                {
                    // Update the deleted user's username by prefixing with "del_" to avoid conflict
                    // Pre-compute values for ExecuteUpdateAsync (EF can't translate expressions like DateTime.UtcNow.ToString())
                    var newUsername = $"del_{deletedUser.Username}";
                    var modifiedDate = DateTime.UtcNow.ToString("o");

                    await _context.Users
                        .Where(u => u.UserId == deletedUser.UserId)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(u => u.Username, newUsername)
                            .SetProperty(u => u.ModifiedDate, modifiedDate));

                    // Create new user with the original email
                    var newUser = new User
                    {
                        Username = dto.Email,
                        Name = $"{dto.FirstName} {dto.LastName}".Trim(),
                        CreatedDate = DateTime.UtcNow.ToString("o"),
                        ModifiedDate = DateTime.UtcNow.ToString("o"),
                        IsActive = true,
                        IsDel = false,
                        UserGroupId = dto.UserGroupId,
                        LastLoginDate = null,
                        IsVerified = false,
                        Status = ((int)UserStatus.Pending).ToString(),
                        CreatedBy = CreatedByType.System.ToString(),
                        TenantId = "",
                        Type = UserType.PatnerUser.ToString(),
                        ProfileId = dto.ProfileId
                    };

                    await _repository.AddUserAsync(newUser);
                    user = newUser;
                }
                else
                {
                    // Normal flow: check for active user
                    var userInfo = await _repository.GetUpsertUserByEmailAsync(dto.Email);

                    // Create flow (UserId == 0) must never fall through to an update.
                    // It previously did — an email belonging to an active user was
                    // treated as "edit that user", silently rewriting their name,
                    // group and company. The controller rejects this case up front;
                    // this is the guard for anything that reaches the service
                    // directly (or races past the check).
                    if (dto.UserId == 0 && userInfo != null)
                        return null;

                    if (dto.UserId > 0)
                    {
                        // Update existing active user
                        var existingUser = await _repository.GetUserByIdAsync(dto.UserId);
                        if (existingUser == null)
                            return null;

                        existingUser.Username = dto.Email;
                        existingUser.Name = $"{dto.FirstName} {dto.LastName}".Trim();
                        existingUser.ModifiedDate = DateTime.UtcNow.ToString("o");
                        // UserGroupId is NOT written here any more. Roles are additive
                        // and live in UserRoles; the column is derived (highest role)
                        // and kept in step by UserRoleAssignmentService.
                        existingUser.ProfileId = dto.ProfileId;

                        await _repository.UpdateUserAsync(existingUser);
                        user = existingUser;
                    }
                    else
                    {
                        // Create new user (no existing user found)
                        var newUser = new User
                        {
                            Username = dto.Email,
                            Name = $"{dto.FirstName} {dto.LastName}".Trim(),
                            CreatedDate = DateTime.UtcNow.ToString("o"),
                            ModifiedDate = DateTime.UtcNow.ToString("o"),
                            IsActive = true,
                            IsDel = false,
                            UserGroupId = dto.UserGroupId,
                            LastLoginDate = null,
                            IsVerified = false,
                            Status = ((int)UserStatus.Pending).ToString(),
                            CreatedBy = CreatedByType.System.ToString(),
                            TenantId = "",
                            Type = UserType.PatnerUser.ToString(),
                            ProfileId = dto.ProfileId
                        };

                        await _repository.AddUserAsync(newUser);
                        user = newUser;
                    }
                }

                // First user the Company Admin adds after registration.
                //
                // Derived from the data, not a stored flag: total non-deleted users
                // of the company, counted AFTER the insert above.
                //   1 → admin alone (registration only)
                //   2 → admin + this, the first added user → true
                //  >2 → a later user                       → false
                //
                // Deliberately a pure count with no "did this call insert" guard, so
                // get-users returns the identical value for the same company — the
                // frontend calls it right after this endpoint and the two must agree.
                bool firstTimeUserAdded = false;
                if (dto.ProfileId.HasValue)
                {
                    firstTimeUserAdded =
                        await _repository.CountCompanyUsersAsync(dto.ProfileId.Value) == 2;
                }

                // Fetch company name using ProfileId
                string companyName = string.Empty;
                if (dto.ProfileId.HasValue)
                {
                    var company = await _repository.GetByIdAsync((uint)dto.ProfileId.Value);
                    companyName = company?.ProfileName ?? "";
                }

                // Split Name into FirstName and LastName
                var nameParts = (user.Name ?? "").Trim().Split(' ', 2);
                var firstName = nameParts.Length > 0 ? nameParts[0] : "";
                var lastName = nameParts.Length > 1 ? nameParts[1] : "";

                return new UserUpsertDto
                {
                    UserId = user.UserId,
                    Email = user.Username,
                    FirstName = firstName,
                    LastName = lastName,
                    UserGroupId = user.UserGroupId,
                    ProfileId = dto.ProfileId,
                    CompanyName = companyName,
                    FirstTimeUserAdded = firstTimeUserAdded
                };
            }
            catch (Exception ex)
            {
                throw;
            }
        }
        public async Task<bool> DeleteUserAsync(int id)
        {
            var user = await _repository.GetUserByIdAsync(id);
            if (user == null)
            {
                return false;
            }

            // Also unassigns every team this user managed, so the team hierarchy
            // stops naming them as manager.
            var unassignedUnitIds = await _repository.DeleteUserAsync(id);

            // A deleted user leaves every portfolio relationship, on either side, so
            // their managers become assignable to another Portfolio Manager.
            var releasedAt = DateTime.UtcNow;
            await _context.PortfolioManagerAssignments
                .Where(a => !a.IsUnAssigned && (a.ManagerUserId == id || a.PortfolioManagerUserId == id))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.IsUnAssigned, true)
                    .SetProperty(a => a.UnAssignedDate, releasedAt));

            // A complimentary license exists only because two teams share a
            // manager, so a team losing its manager can leave that grant without
            // its basis. Run the same reconciliation the manual unassign path
            // runs, per released unit: it picks up every active grant where the
            // unit is either side (primary or beneficiary), re-tests shared
            // eligibility against the now-unassigned UserTeams rows, and revokes
            // the ones that no longer qualify. Purchased assignments are never
            // touched — a team keeps its own bought license, it just has no
            // manager until one is assigned.
            //
            // Best-effort: the delete is already committed and the reconciliation
            // job is the backstop, so a hiccup here must not fail the delete.
            foreach (var unitId in unassignedUnitIds)
            {
                try
                {
                    await _licenseService.ReconcileGrantsForUnitAsync(unitId, null);
                }
                catch
                {
                    // Swallowed by design; the safety-net job will catch it.
                }
            }

            return true;
        }

        public async Task<User?> GetUserByIdAsync(int userId)
        {
            return await _repository.GetUserByIdAsync(userId);
        }

        public async Task UpdateUserAsync(User user)
        {
            await _repository.UpdateUserAsync(user);
        }

        public async Task InvalidateUserTokensAsync(int userId)
        {
            await _userRepository.InvalidateUserTokensAsync(userId);
        }
        public async Task<List<UserGroupsDto>> GetUserGroupsAsync(int? userId)
        {
            var allGroups = await _repository.GetAllUserGroupsAsync();
            var user = await _context.Users.AsNoTracking()
                                           .FirstOrDefaultAsync(u => u.UserId == userId);

            if (user == null)
                return new List<UserGroupsDto>();

            switch (user.UserGroupId)
            {
                // A RRIDA Admin and a Profile Admin may both hand out the three
                // assignable roles. Neither can create another RRIDA Admin —
                // allGroups never contains it (see GetAllUserGroupsAsync).
                case (int)UserGroups.RridaAdmin:
                case (int)UserGroups.ProfileAdmin:
                    return allGroups.Where(g =>
                        g.Id == (int)UserGroups.ProfileAdmin ||
                        g.Id == (int)UserGroups.TeamManager ||
                        g.Id == (int)UserGroups.PortfolioManager
                    ).ToList();

                // Everyone else — a manager — may only ever create a Team Manager.
                default:
                    return allGroups.Where(g =>
                        g.Id == (int)UserGroups.TeamManager
                    ).ToList();
            }
        }

        /// <summary>
        /// The role hierarchy as the caller is allowed to see it: their own role
        /// first, then every role beneath it, highest first.
        ///
        ///   RRIDA Admin       → all four
        ///   Profile Admin     → Profile Admin, Team Manager, Portfolio Manager
        ///   Team Manager      → Team Manager, Portfolio Manager
        ///   Portfolio Manager → Portfolio Manager
        ///
        /// Deliberately NOT the same list as <see cref="GetUserGroupsAsync"/>, and
        /// the two must not be collapsed into one. That one answers "which roles
        /// may this caller HAND OUT", which is a narrower question with different
        /// answers: it hides RRIDA Admin from everybody, and it gives a Team
        /// Manager only Team Manager. This one answers "which roles may this caller
        /// SEE", so a RRIDA Admin does see their own role — flagged
        /// <c>isAssignable: false</c>, because upsert-user still refuses to create
        /// one.
        /// </summary>
        public async Task<List<RoleHierarchyDto>> GetRoleHierarchyAsync(int userId)
        {
            var user = await _context.Users.AsNoTracking()
                                           .FirstOrDefaultAsync(u => u.UserId == userId && !u.IsDel);

            if (user == null)
                return new List<RoleHierarchyDto>();

            // Rank is read from THIS array, never from the ids: 1 Profile Admin
            // outranks 2 Team Manager, and 3 RRIDA Admin outranks both. See the
            // UserGroups enum for why the numbers ended up out of order.
            var rank = Array.IndexOf(RoleHierarchyHighestFirst, (UserGroups)user.UserGroupId);

            // A group id that is not one of the four — a leftover from the retired
            // six-role model, say. Showing the whole hierarchy to an account whose
            // rank cannot be established would be the wrong way to fail.
            if (rank < 0)
                return new List<RoleHierarchyDto>();

            var roles = await _repository.GetAllRolesAsync();
            var byId = roles.ToDictionary(r => r.Id);

            var visible = new List<RoleHierarchyDto>();

            for (var level = rank; level < RoleHierarchyHighestFirst.Length; level++)
            {
                var role = RoleHierarchyHighestFirst[level];

                // Driven by the array, not by the table, so the order is the
                // hierarchy's and a row missing from UserGroups is skipped rather
                // than silently reordering everything after it.
                if (!byId.TryGetValue((int)role, out var row))
                    continue;

                row.Level = level + 1;
                row.IsAssignable = role != UserGroups.RridaAdmin;
                row.IsOwnRole = level == rank;
                visible.Add(row);
            }

            return visible;
        }


        /// <summary>
        /// Email-shape and domain-policy check for POST api/profile/Create.
        ///
        /// Deliberately NOT checkCompany: that method looks an existing company up by
        /// domain and refuses registration when the company it finds has an unverified
        /// admin, which is precisely the domain-based identity matching the Profile
        /// Admin ticket rules out. This keeps only the two parts that are about the
        /// address itself - is it well formed, and is its domain on the blocked list
        /// (free/consumer providers, production only) - and never touches Profiles.
        ///
        /// Multi-level domains are passed through whole: abc.co.uk is checked as
        /// abc.co.uk, with no attempt to reduce it to an organisation key.
        /// </summary>
        public bool IsRegistrationEmailAcceptable(string emailAddress)
        {
            var normalized = (emailAddress ?? string.Empty).Trim().ToLowerInvariant();

            var atIndex = normalized.LastIndexOf('@');
            if (atIndex <= 0 || atIndex == normalized.Length - 1)
                return false;

            var domain = normalized[(atIndex + 1)..];
            if (!domain.Contains('.'))
                return false;

            // Named IsDomainAllowed but returns true when the domain is BLOCKED.
            return !_domainValidator.IsDomainAllowed(domain);
        }

        public async Task<ValidateDomainAndRegisterEmail> checkCompany(string input)
        {
            if (!input.Contains("@"))
                return new ValidateDomainAndRegisterEmail { IsValid = false };

            var parts = input.Split('@');
            if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
                return new ValidateDomainAndRegisterEmail { IsEmailValid = false, IsValid = false };

            string domain = parts[1].ToLower();
            // Validate domain against allowed list
            if (_domainValidator.IsDomainAllowed(domain))
                return new ValidateDomainAndRegisterEmail { IsDomainValid = false, IsValid = false };

            // Check for domain in Companies
            var existingCompany = await _repository.GetCompanyByDomainAsync(domain);
            
            // NEW LOGIC: If domain exists in Companies, check company admin user verification
            if (existingCompany != null)
            {
                // Check if there's a user with Type="ProfileAdmin" for this company
                var companyAdminUser = await _context.Users
                    .FirstOrDefaultAsync(u => u.ProfileId == (int)existingCompany.Id 
                        && u.Type == UserType.ProfileAdmin.ToString());
                
                if (companyAdminUser != null)
                {
                    // If Type = "ProfileAdmin" but IsVerified = false, return failure
                    if (!companyAdminUser.IsVerified)
                    {
                        return new ValidateDomainAndRegisterEmail
                        {
                            IsValid = false,
                            CompanyNotVerified = true,
                            ProfileId = (int)existingCompany.Id,
                            IsVerified = false
                        };
                    }
                    // If Type = "ProfileAdmin" and IsVerified = true, continue with existing logic
                }
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == input.ToLower());


            return new ValidateDomainAndRegisterEmail
            {
                IsValid = true,
                UserId = user != null ? (int)user.UserId : 0,
                ProfileId = existingCompany != null ? (int)existingCompany.Id : 0,
                //IsVerified = user?.IsVerified
            };
        }
        public async Task<bool> EmailExistsAsync(string email)
        {
            return await _repository.EmailExistsAsync(email.Trim().ToLower());
        }

        public async Task<EmailUsageResult> GetEmailUsageAsync(string email)
        {
            return await _repository.GetEmailUsageAsync(email);
        }
        public async Task<bool> LogoutUser(int userId, string token)
        {
            // Get token for this user
            var userToken = await _repository.GetTokenByUserIdAsync(userId, token);

            if (userToken == null)
                return false;

            // Invalidate token by setting expiry to yesterday
            userToken.ExpiryDate = DateTime.UtcNow.AddDays(-1);

            await _repository.UpdateTokenAsync(userToken);
            return true;
        }

        public async Task<RefreshTokenDto> RefreshToken(int userId, string token)
        {
            // Get token for this user
            var userToken = await _repository.GetTokenInfoAsync(userId, token);

            if (userToken == null)
            {
                return new RefreshTokenDto
                {
                    isVaild = false
                };
            }

            var user = await _repository.GetUserByIdAsync(userId);

            var accesstoken = _tokenGenerator.GenerateToken(user, user.Username);
            // Save token in AuthTokens table
            var authToken = new UserToken
            {
                UserId = userToken.UserId,
                Token = accesstoken,
                ExpiryDate = DateTime.UtcNow.AddMinutes(4320)
            };

            var refreshtoken = _tokenGenerator.GenerateToken(user, user.Username);
            // refresh token for 6 days 
            var authToken6Days = new UserToken
            {
                UserId = user.UserId,
                Token = refreshtoken,
                ExpiryDate = DateTime.UtcNow.AddMinutes(4320 * 2)
            };
            _context.UserTokens.AddRange(authToken, authToken6Days);
            await _context.SaveChangesAsync();

            return new RefreshTokenDto
            {
                accessToken = accesstoken,
                refreshToken = refreshtoken,
                expiresIn = 4320,
                isVaild = true
            };
        }
        public async Task<string> GenerateManualResetPasswordLink(User user)
        {
            // Step 2 - Get environment base URL 
            string baseUrl = _config["SendGrid:FrontendBaseUrl"];

            // Step 3 - Create reset password payload
            var payload = new ResetPasswordPayload
            {
                Email = user.Username,
                UserId = user.UserId,
                RequestTime = DateTime.UtcNow,
                IsManuallyReset = true
            };

            // Step 4 - Encrypt payload
            var encrypted = BussinessManager.EncryptObject(payload);

            // Step 5 - Final link 
            var resetLink = $"{baseUrl}/resetPassword?data={encrypted}";

            // Step 6 - Save record in DB
            var model = new ResetPasswordLinks
            {
                UserId = user.UserId,
                ResetLink = resetLink,
                CreatedAt = DateTime.UtcNow,
                IsLinkUsed = false,
                IsDel = false
            };

            await _repository.SaveResetPasswordLink(model);

            return resetLink;
        }

        public async Task<User> GetUserById(int userId)
        {
            return await _context.Users
                .FirstOrDefaultAsync(x => x.UserId == userId && x.IsDel == false);
        }

        public async Task<(bool IsSuccess, string Message)> UpdateResetPasswordAsync(ResetPasswordDto dto)
        {
            ResetPasswordPayload decrypted;
            try
            {
                // Step 1: Decrypt the payload from link
                decrypted = BussinessManager.DecryptObject<ResetPasswordPayload>(dto.Param);
            }
            catch (FormatException)
            {
                return (false, ResponseMessages.Errors.InvalidLink);
            }
            catch (Exception)
            {
                return (false, ResponseMessages.General.ResetFailed);
            }

            // Step 2: Get user
            var user = await _repository.GetUserByEmailAndIdAsync(decrypted.Email, decrypted.UserId);
            if (user == null || user.UserId != decrypted.UserId)
                return (false, ResponseMessages.Warnings.InvalidLinkReset);

            // Single use — invitations only.
            //
            // A recorded link that has already been consumed is refused, so an
            // invitation cannot set a password twice. A link with NO record is
            // allowed through: every invitation minted before this existed has
            // none, and refusing those would lock out people holding one right
            // now. Forgot-password links never reach this branch.
            ResetPasswordLinks? invitationLink = null;
            if (decrypted.IsInvitation)
            {
                invitationLink = await _repository.GetResetPasswordLink(decrypted.UserId, dto.Param);
                if (invitationLink != null && invitationLink.IsLinkUsed)
                    return (false, ResponseMessages.Errors.LinkAlreadyUsed);
            }

            // Step 3: Check if link exists and is not used
            var resetLinkRecord = await _repository.GetResetPasswordLink(user.UserId, dto.Param);
            if (resetLinkRecord == null || resetLinkRecord.IsLinkUsed)
                return (false, ResponseMessages.Errors.LinkAlreadyUsed);

            // Step 4: Check if link expired (24 hours)
            var isExpired = decrypted.RequestTime.AddHours(24) < DateTime.UtcNow;
            if (isExpired)
                return (false, ResponseMessages.General.ResetExpired);



            // Step 6: Mark the link as used and Update Password 
            resetLinkRecord.IsLinkUsed = true;
            await _repository.UpdateResetPasswordLink(resetLinkRecord, dto.NewPassword);

            // Invalidate all existing tokens for this user to force re-authentication
            // This ensures that all active sessions (other browsers/devices) are logged out
            await _userRepository.InvalidateUserTokensAsync(user.UserId);

            return (true, ResponseMessages.Success.PasswordReset);
        }
        public async Task<List<CompanyDto>> GetCompanyPatnerListAsync(int userId)
        {
            return await _repository.GetCompanyPatnerListAsync(userId);
        }

        public async Task<List<CompanyDto>> GetAllPartnerCompaniesAsync()
        {
            return await _repository.GetAllPartnerCompaniesAsync();
        }

        public async Task<(bool Success, string Message)> AssignPartnerAsync(int profileId, int partnerId)
        {
            return await _repository.AssignPartnerAsync(profileId, partnerId);
        }

        public async Task<(bool Success, string Message)> UnassignPartnerAsync(int profileId, int partnerId)
        {
            return await _repository.UnassignPartnerAsync(profileId, partnerId);
        }

        public async Task IncrementFailedLoginAttemptsAsync(int userId)
        {
            await _repository.IncrementFailedLoginAttemptsAsync(userId);
        }

        public async Task ResetFailedLoginAttemptsAsync(int userId)
        {
            await _repository.ResetFailedLoginAttemptsAsync(userId);
        }
    }
}
