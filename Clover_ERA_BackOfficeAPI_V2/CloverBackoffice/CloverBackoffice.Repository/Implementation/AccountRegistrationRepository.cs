using CloverBackoffice.Components;
using CloverBackoffice.Components.Enums;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.BusinessModel;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static CloverBackoffice.Components.BussinessManager;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;

namespace CloverBackoffice.Repository.Implementation
{
    public class AccountRegistrationRepository : IAccountRegistrationRepository
    {
        private readonly ApplicationDbContext _context;
        private readonly JwtTokenGenerator _tokenGenerator;
        private readonly IConfiguration _config;
        private const int TOKEN_EXPIRY = 4320;
        private readonly SendGridSettings _sendGridSettings;
        private readonly ICompanySubscriptionRepository _subscriptionRepo;
        private readonly IPaymentRepository _paymentRepo;
        public AccountRegistrationRepository(IConfiguration config, ApplicationDbContext context, JwtTokenGenerator tokenGenerator, SendGridSettings sendGridSettings, ICompanySubscriptionRepository subscriptionRepo, IPaymentRepository paymentRepo)
        {
            _context = context;
            _tokenGenerator = tokenGenerator;
            _config = config;
            _sendGridSettings = sendGridSettings;
            _subscriptionRepo = subscriptionRepo;
            _paymentRepo = paymentRepo;
        }
        /// <summary>
        /// POST api/profile/Create. Creates a Profile and its Profile Admin User, and
        /// nothing else, inside one transaction.
        ///
        /// Deliberately does NOT create a default BusinessUnit, a UserToken or a
        /// CompanyLink. Those belonged to the Company/BusinessUnit architecture this
        /// flow replaces. Verification still works without a UserToken row: the
        /// emailed link carries an encrypted {Email, UserId} payload and
        /// VerifyUserEmailAsync looks the user up from that, never from UserTokens.
        ///
        /// Uniqueness is on the COMPLETE email address only. There is deliberately no
        /// domain rule: two Profile Admins on the same domain
        /// (manager1@abc.com, manager2@abc.com) are both valid registrations, and the
        /// domain is never used to find or associate an existing Profile.
        ///
        /// The email check is repeated HERE, inside the transaction, even though the
        /// controller already ran it. The controller's check gives the caller a
        /// friendly message; this one closes the check-then-insert race between two
        /// concurrent registrations of the same address, which the controller's
        /// cannot.
        /// </summary>
        public async Task<ProfileCreateResponseDto> AddProfileWithUserAsync(ProfileCreateDto dto)
        {
            var email = (dto.EmailAddress ?? string.Empty).Trim();
            var normalizedEmail = email.ToLowerInvariant();

            var atIndex = normalizedEmail.LastIndexOf('@');
            if (atIndex <= 0 || atIndex == normalizedEmail.Length - 1)
                throw new InvalidOperationException(ResponseMessages.Errors.InvalidEmail);

            var profileName = (dto.ProfileName ?? string.Empty).Trim();
            var firstName = (dto.FirstName ?? string.Empty).Trim();
            var lastName = (dto.LastName ?? string.Empty).Trim();

            // Stored as an attribute of the profile because the column is NOT NULL.
            // It is never read back to decide whether a profile already exists, and
            // nothing is matched or associated on it. Multi-level domains are stored
            // whole: abc.co.uk stays abc.co.uk, with no attempt to reduce it to "abc".
            var domainName = normalizedEmail[(atIndex + 1)..];

            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // The only uniqueness rule: the complete address, case-insensitively.
                var emailTaken = await _context.Users
                    .AnyAsync(u => u.Username.ToLower() == normalizedEmail && !u.IsDel);
                if (emailTaken)
                    throw new InvalidOperationException(ResponseMessages.Errors.EmailAlreadyExist);

                // The trial plan, looked up before anything is written (same rule
                // as create-profile).
                var plan = await FindRegistrationTrialPlanAsync();

                var profileCodes = BussinessManager.GenerateCodes();
                var profile = new Profile
                {
                    ProfileName = profileName,
                    ProfileShortCode = profileCodes.ShortCode,
                    DomainName = domainName,
                    RegistrationDate = DateTime.UtcNow,
                    ContactFirstName = firstName,
                    ContactLastName = lastName,
                    ContactEmailAddress = normalizedEmail
                };
                _context.Profiles.Add(profile);
                await _context.SaveChangesAsync();

                // The Profile Admin. Created unverified (Pending); the caller sends the
                // verification link and the account activates when it is followed.
                var user = new User
                {
                    Username = email,
                    Name = string.IsNullOrWhiteSpace(firstName) && string.IsNullOrWhiteSpace(lastName)
                        ? normalizedEmail[..atIndex]
                        : $"{firstName} {lastName}".Trim(),
                    Password = PasswordUtility.Encrypt(dto.Password),
                    CreatedDate = DateTime.UtcNow.ToString("o"),
                    ModifiedDate = DateTime.UtcNow.ToString("o"),
                    IsActive = true,
                    IsDel = false,
                    UserGroupId = (int)CloverBackoffice.Components.Enums.UserGroups.ProfileAdmin,
                    LastLoginDate = null,
                    IsVerified = false,
                    Status = ((int)UserStatus.Pending).ToString(),
                    CreatedBy = CreatedByType.System.ToString(),
                    TenantId = "",

                    // Persisted as literal text in Users.Type and read back by name
                    // elsewhere — checkCompany finds a profile's admin by this value.
                    Type = UserType.ProfileAdmin.ToString(),

                    // The whole of the user-to-profile association.
                    ProfileId = (int)profile.Id
                };
                AddRegistrationRoles(user);
                _context.Users.Add(user);
                await _context.SaveChangesAsync();

                // The new Profile Admin's 30-day free trial starts now, at
                // registration, and is stored the same way create-profile stores it.
                await ProvisionRegistrationTrialAsync(profile.Id, user.UserId, plan, DateTime.UtcNow);

                await transaction.CommitAsync();

                return new ProfileCreateResponseDto
                {
                    ProfileId = (int)profile.Id,
                    ProfileName = profile.ProfileName,
                    DomainName = profile.DomainName,
                    UserId = user.UserId,
                    Email = user.Username
                };
            }
            catch (InvalidOperationException)
            {
                // A rejected registration, not a fault. Roll back and let the message
                // reach the caller unchanged.
                await transaction.RollbackAsync();
                throw;
            }
            catch (DbUpdateException ex) when (IsUsernameUniqueViolation(ex))
            {
                // Two registrations of the same address raced past the check above and
                // the unique index UX_Users_Username_Live caught the loser. That is the
                // duplicate-email rule firing, not a fault, so report it as one instead
                // of a 500 - the caller sees the same message either way.
                await transaction.RollbackAsync();
                throw new InvalidOperationException(ResponseMessages.Errors.EmailAlreadyExist);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                throw new Exception(ResponseMessages.Errors.CompanyCreationFailedMessage, ex);
            }
        }

        /// <summary>
        /// Whether this write failed on the live-username unique index. Matched on
        /// PostgreSQL's unique_violation SQLSTATE plus the index name, so an unrelated
        /// unique violation is not silently reported as a duplicate email.
        /// </summary>
        private static bool IsUsernameUniqueViolation(DbUpdateException ex) =>
            ex.GetBaseException() is Npgsql.PostgresException { SqlState: "23505" } pg
            && (pg.ConstraintName?.Contains("Username", StringComparison.OrdinalIgnoreCase) ?? false);

        public async Task<CompanyDto> CompanyNameExistsAsync(string CompanyName)
        {
            var normalizedDomain = CompanyName.Trim().ToLower();

            var company = await _context.Profiles
                .Where(c => c.ProfileName.ToLower() == normalizedDomain)
                .Select(c => new CompanyDto
                {
                    Id = (int?)c.Id,
                    DomainName = c.DomainName,
                    Name = c.ProfileName,
                    FirstName = c.ContactFirstName,
                    LastName = c.ContactLastName,
                    EmailAddress = c.ContactEmailAddress
                })
                .FirstOrDefaultAsync();

            return company;
        }

        public async Task<BusinessUnitDto> BussinessNameExistsAsync(string BussinessName, int ProfileId)
        {
            var normalizedDomain = BussinessName.Trim().ToLower();

            var bussinessunit = await _context.BusinessUnits
                .Where(b => b.Name.ToLower() == normalizedDomain && b.Profile.Id == ProfileId)
                .Select(b => new BusinessUnitDto
                {
                    Id = b.Id,
                    Name = b.Name
                })
                .FirstOrDefaultAsync();

            return bussinessunit;
        }

        // The organisation contact now lives inline on Profiles and BusinessUnits,
        // so a hit can come from either table. Profiles is checked first, which is
        // what the old Contacts lookup effectively did: the company row was created
        // before any of its units.
        public async Task<ContactInfoDto?> ContactEmailExistsAsync(string EmailAddress)
        {
            var normalized = EmailAddress.Trim().ToLower();

            var fromProfile = await _context.Profiles
                .Where(c => c.ContactEmailAddress != null
                            && c.ContactEmailAddress.ToLower() == normalized)
                .Select(c => new ContactInfoDto
                {
                    EmailAddress = c.ContactEmailAddress!,
                    FirstName = c.ContactFirstName,
                    LastName = c.ContactLastName
                })
                .FirstOrDefaultAsync();

            if (fromProfile != null)
            {
                return fromProfile;
            }

            return await _context.BusinessUnits
                .Where(b => b.ContactEmailAddress != null
                            && b.ContactEmailAddress.ToLower() == normalized)
                .Select(b => new ContactInfoDto
                {
                    EmailAddress = b.ContactEmailAddress!,
                    FirstName = b.ContactFirstName,
                    LastName = b.ContactLastName
                })
                .FirstOrDefaultAsync();
        }

        // Duplicate check against the identity store. Contacts is the organisation
        // contact table (FK'd from Companies/BusinessUnits) and is NOT written by
        // the add-user endpoint, so ContactEmailExistsAsync alone cannot see an
        // account created that way — that blind spot is how the same email ended
        // up on two Users rows via /CreateTrial.
        //
        // Soft-deleted rows are excluded: DeleteUserAsync only flips IsDel, and a
        // released email must be reusable.
        public async Task<User?> UserEmailExistsAsync(string email)
        {
            var normalized = (email ?? string.Empty).Trim().ToLower();
            if (normalized.Length == 0)
                return null;

            return await _context.Users
                .FirstOrDefaultAsync(u => u.Username.ToLower() == normalized && !u.IsDel);
        }

        // Companies and BusinessUnits both FK into Contacts for their organisation
        // contact, and Users holds the logins — an email already present in any of
        // the three must not be reused. Soft-deleted rows are skipped so a released
        // email becomes available again.
        //
        // Users is probed first because it is the only store carrying IsVerified;
        // an unverified account is reported as taken-but-unverified rather than
        // taken. Contacts has no verification flag, so a company/unit contact with
        // no matching login counts as verified (i.e. simply taken).
        public async Task<EmailUsageResult> GetEmailUsageAsync(string email)
        {
            var normalized = (email ?? string.Empty).Trim().ToLower();
            if (normalized.Length == 0)
                return new EmailUsageResult();

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Username.ToLower() == normalized && !u.IsDel);
            if (user != null)
                return new EmailUsageResult { Exists = true, IsVerified = user.IsVerified };

            var companyMatch = await _context.Profiles
                .AnyAsync(c => !c.IsDel && c.ContactEmailAddress.ToLower() == normalized);
            if (companyMatch)
                return new EmailUsageResult { Exists = true, IsVerified = true };

            var unitMatch = await _context.BusinessUnits
                .AnyAsync(b => b.IsDel != true && b.ContactEmailAddress.ToLower() == normalized);
            if (unitMatch)
                return new EmailUsageResult { Exists = true, IsVerified = true };

            return new EmailUsageResult();
        }

        public async Task<CompanyDto> CompanyExistsAsync(string domainName)
        {
            var normalizedDomain = domainName.Trim().ToLower();

            var company = await _context.Profiles
                .Where(c => c.DomainName.ToLower() == normalizedDomain)
                .Select(c => new CompanyDto
                {
                    Id = (int?)c.Id,
                    DomainName = c.DomainName,
                    Name = c.ProfileName,
                    FirstName = c.ContactFirstName,
                    LastName = c.ContactLastName,
                    EmailAddress = c.ContactEmailAddress
                })
                .FirstOrDefaultAsync();

            return company;
        }

        public async Task<UserCompanyResponseDto> CreateBusinessWithUserAsync(UnitCreateDto dto)
        {
            int CompId = 0;
            string[] parts = Array.Empty<string>();
            if (!string.IsNullOrEmpty(dto.EmailAddress))
            {
                parts = dto.EmailAddress.Split('@');
            }
            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                //  Step 0: Check if company already exists by domain name
                var normalizedDomain = dto.EmailAddress.Trim().ToLower();
                var existingCompany = await _context.Profiles
                    .FirstOrDefaultAsync(c => c.Id == dto.ProfileId);
                var existingUnit = await _context.BusinessUnits
                  .FirstOrDefaultAsync(c => c.Name.ToLower() == normalizedDomain);

                if (existingCompany == null)
                {
                    return new UserCompanyResponseDto
                    {
                        UserId = 0,
                        Email = null,
                        ProfileId = 0,
                        Message = ResponseMessages.Errors.InvalidCompany
                    };
                }

                if (existingCompany != null && existingUnit != null)
                {
                    var existingUser = await _context.Users
                        .FirstOrDefaultAsync(u => u.Username == dto.EmailAddress);
                    return new UserCompanyResponseDto
                    {
                        UserId = 0,
                        Email = null,
                        ProfileId = 0,
                        Message = ResponseMessages.Warnings.UnitExists
                    };
                }
                // Step 1: Create BusinessUnit. Its contact is stored inline on the unit.

                var dailyQuestionSetGroupb = await _context.DailyQuestionSetGroups.FirstOrDefaultAsync(g => g.Id == 1);
                var businessUnitCodes = BussinessManager.GenerateCodes();
                var businessUnit = new BusinessUnit
                {

                    BusinessUnitCode = businessUnitCodes.Code,
                    BusinessUnitShortCode = businessUnitCodes.ShortCode,
                    Name = dto.UnitName,
                    NameInvariant = dto.UnitName.ToUpperInvariant() + businessUnitCodes,
                    RegistrationDate = DateTime.UtcNow,
                    Profile = existingCompany,
                    ContactFirstName = dto.FirstName ?? parts[0],
                    ContactLastName = dto.LastName ?? parts[0],
                    ContactEmailAddress = dto.EmailAddress,
                    ActiveDailyQuestionSetGroupId = dailyQuestionSetGroupb.Id,
                    ActiveDailyQuestionSetGroup = dailyQuestionSetGroupb
                };
                _context.BusinessUnits.Add(businessUnit);
                await _context.SaveChangesAsync();

                // Step 4: Create User (if not exists)
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == dto.EmailAddress);
                if (user == null)
                {
                    user = new User
                    {
                        Username = dto.EmailAddress,
                        Name = !string.IsNullOrWhiteSpace(dto.FirstName) || !string.IsNullOrWhiteSpace(dto.LastName) ? $"{dto.FirstName} {dto.LastName}".Trim() : parts[0],
                        Password = PasswordUtility.Encrypt(!string.IsNullOrWhiteSpace(dto.Password) ? dto.Password : "12345678"),
                        CreatedDate = DateTime.UtcNow.ToString("o"),
                        ModifiedDate = DateTime.UtcNow.ToString("o"),
                        IsActive = true,
                        IsDel = false,
                        UserGroupId = (int)CloverBackoffice.Components.Enums.UserGroups.TeamManager,
                        LastLoginDate = null,
                        IsVerified = false,
                        Status = ((int)UserStatus.Active).ToString(),
                        CreatedBy = CreatedByType.System.ToString(),
                        TenantId = "",
                        Type = UserType.Unit.ToString()
                    };
                    _context.Users.Add(user);
                    await _context.SaveChangesAsync();
                }

                // Create notification
                var message = string.Format(ResponseMessages.Success.UnitLinkedToCompany, businessUnit.Name);
                var notification = new Notification
                {
                    UnitId = 0,
                    BussinessUnitId = (int)businessUnit.Id,
                    ProfileId = dto.ProfileId,
                    UserId = user.UserId,
                    CreatedAt = DateTime.UtcNow,
                    Message = message
                };
                _context.Notifications.Add(notification);
                await _context.SaveChangesAsync();


                // Step 5: Create CompanyLink
                var companyLink = new CompanyLink
                {
                    ProfileId = (int)existingCompany.Id,
                    UserId = user.UserId,
                    ParentId = 0,
                    UnitId = 0,
                    ExpiryDate = string.Empty,
                    BussinessUnitId = (int)businessUnit.Id
                };
                _context.CompanyLinks.Add(companyLink);
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();

                if (existingCompany != null)
                {
                    var companyMessage = string.Format(ResponseMessages.Success.BusinessUnitCreated, businessUnit.Name, existingCompany.ProfileName);
                    // Send email to company admin
                    await BussinessManager.SendCompanyNotificationEmailAsync(_sendGridSettings, existingCompany.DomainName, existingCompany.ProfileName, businessUnit.Name);
                }

                return new UserCompanyResponseDto
                {
                    UserId = user.UserId,
                    Email = user.Username,
                    ProfileId = (int)businessUnit.Id,
                    Message = ResponseMessages.Success.UnitCreationSuccess
                };
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                throw new Exception(ResponseMessages.Errors.UnitCreationFailedMessage, ex);
            }
        }

        public async Task<List<CompanyDto>> GetCompanyListAsync(int userId)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);
            if (user == null)
                return new List<CompanyDto>();

            IQueryable<Profile> companiesQuery;

            if (user.UserGroupId == (int)CloverBackoffice.Components.Enums.UserGroups.TeamManager)
            {
                var unitIds = await _context.UserTeams
                    .Where(ut => ut.UserId == userId && !ut.IsUnAssigned == false)
                    .Select(ut => ut.UnitId)
                    .ToListAsync();

                if (!unitIds.Any())
                    return new List<CompanyDto>();

                var profileIds = await _context.BusinessUnits
                    .Where(bu => unitIds.Contains(bu.Id))
                     .OrderBy(bu => bu.Name)
                    .Select(bu => bu.Profile.Id)
                    .Distinct()
                    .ToListAsync();

                if (!profileIds.Any())
                    return new List<CompanyDto>();

                companiesQuery = _context.Profiles.Where(c => profileIds.Contains(c.Id));
            }
            else
            {
                companiesQuery = _context.Profiles;
            }

            // Step 1: Get raw company and user data
            var rawList = await companiesQuery
                 .OrderBy(bu => bu.ProfileName)
                .Select(c => new
                {
                    c.Id,
                    Name = c.ProfileName,
                    c.DomainName,
                    c.IsPartner,
                    c.ReferredBy,

                    // The profile's own contact, merged in from the old Contacts row.
                    // Used when no CompanyLink user is on file, which is how a company
                    // created by any registration path but never linked to a user used
                    // to come back with three nulls.
                    c.ContactFirstName,
                    c.ContactLastName,
                    c.ContactEmailAddress,
                    //Get company name 
                    ReferredCompanyName = _context.Profiles
                         .Where(rc => rc.Id == c.ReferredBy)
                         .Select(rc => rc.ProfileName)
                         .FirstOrDefault(),
                    LinkedUser = _context.CompanyLinks
                        .Where(cl => cl.ProfileId == c.Id)
                        .OrderBy(cl => cl.UserId)
                        .Select(cl => new
                        {
                            FullName = cl.User.Name,
                            Email = cl.User.Username
                        })
                        .FirstOrDefault()
                })
                .ToListAsync();

            // Step 2: Map to CompanyDto and split name
            var result = rawList.Select(c => new CompanyDto
            {
                Id = (int)c.Id,
                Name = c.Name,
                DomainName = c.DomainName,
                FirstName = c.LinkedUser?.FullName?.Split(' ', 2)[0] ?? c.ContactFirstName,
                LastName = c.LinkedUser?.FullName?.Split(' ', 2).Length > 1
                    ? c.LinkedUser.FullName.Split(' ', 2)[1]
                    : (c.LinkedUser != null ? "" : c.ContactLastName),
                EmailAddress = c.LinkedUser?.Email ?? c.ContactEmailAddress,
                IsPartner = c.IsPartner,
                ReferredById = c.ReferredBy,
                ReferredName = c.ReferredCompanyName
            }).ToList();

            return result;
        }

        public async Task<CompanyDto?> GetCompanyByIdAsync(int id)
        {
            return await _context.Profiles
                .Where(c => c.Id == id)
.Select(c => new CompanyDto
                {
                    Id = (int)c.Id,
                    DomainName = c.DomainName,
                    Name = c.ProfileName,

                    // Now that the contact is three columns on the profile, this can
                    // return it. It used to need a join to Contacts and did not do one.
                    FirstName = c.ContactFirstName,
                    LastName = c.ContactLastName,
                    EmailAddress = c.ContactEmailAddress
                })
                .FirstOrDefaultAsync();
        }
        public async Task<bool> UserExistsAsync(string email, string password)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == email.ToLower() && (u.IsDel == false || u.IsDel == null));

            if (user == null)
                return false;

            string storedPassword = user.Password;

            bool isHashed = storedPassword.StartsWith("$2a$") || storedPassword.StartsWith("$2b$") || storedPassword.StartsWith("$2y$");

            if (isHashed)
            {
                // User has hashed password — normal case
                return PasswordUtility.Verify(password, storedPassword);
            }
            else
            {
                // User has plain text password — legacy case
                if (password == storedPassword)
                {
                    // Rehash password and update user record
                    string hashed = PasswordUtility.Encrypt(password);
                    user.Password = hashed;

                    // Save updated password back to DB
                    //_context.Users.Update(user);
                    _context.Entry(user).Property(u => u.Password).IsModified = true;
                    await _context.SaveChangesAsync();

                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        public async Task<Profile> GetCompanyByUserEmailAsync(string email)
        {
            return await _context.Profiles
                .FirstOrDefaultAsync(c => c.DomainName == email);
        }

        public async Task<UserDto> GetByUsername(string username)
        {
            try
            {
                var createdUserDto = await _context.Users
                        .Where(u => u.Username == username)
                        .Select(u => new UserDto
                        {
                            UserId = u.UserId,
                            TenantId = u.TenantId,
                            Username = u.Username,
                            Name = u.Name,
                            IsActive = u.IsActive,
                            Group = new UserGroupDto
                            {
                                Id = u.UserGroup.Id,
                                Name = u.UserGroup.Name,
                                IsAdmin = u.UserGroup.IsAdmin,
                                UnitId = u.UserGroup.UnitId,
                                AssignUserRights = u.UserGroup.AssignUserRights.Select(right => new AssignUserRightDto
                                {
                                    AssignRightId = right.AssignRightId,
                                    ModuleID = right.SystemModule.ModuleID,
                                    ModuleName = right.SystemModule.ModuleName, // <-- Uses FK to join
                                    PromptTypeId = right.PromptType.PromptTypeId,
                                    PromptTypeTitle = right.PromptType.PromptTypeTitle  // <-- Uses FK to join
                                }).ToList()
                            }
                        })
                        .FirstOrDefaultAsync();

                return createdUserDto;

            }
            catch (Exception ex)
            {

                throw;
            }
        }
        public async Task<ExistingCompany?> GetCompanyByDomainAsync(string domain)
        {
            return await _context.Profiles.Where(c => c.DomainName.ToLower() == domain.ToLower())
                 .Select(c => new ExistingCompany()
                 {
                     DomainName = c.DomainName,
                     emailExist = true,
                     Id = c.Id,
                     Name = domain,
                     domainExist = true,
                 })
                 .FirstOrDefaultAsync();
        }

        public async Task<ExistingCompany?> GetCompanyByDomainOnlyAsync(string domain)
        {
            return await _context.Profiles.Where(c => c.DomainName.ToLower().EndsWith("@" + domain))
                 .Select(c => new ExistingCompany()
                 {
                     DomainName = c.DomainName,
                     emailExist = true,
                     Id = c.Id,
                     Name = domain,
                     domainExist = true,
                 })
                 .FirstOrDefaultAsync();
        }

        public async Task<string> CreateBusinessUnitAsync(int ProfileId, string BussinessUnitName)
        {
            return "Success";
        }

        public async Task<User?> GetUserAsync(int userId, string username)
        {
            return await _context.Users
                .FirstOrDefaultAsync(u => u.UserId == userId && u.Username == username);
        }

        public async Task<LoginDto> UserInfoAsync(string email)
        {
            // Get user with user group info (tracked so we can update LastLoginDate)
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Username.ToLower() == email.ToLower());

            if (user == null)
                return null;

            // Detect first-time login before updating
            var isFirstLogin = user.LastLoginDate == null;


            var userGroup = await _context.UserGroups
                .AsNoTracking()
                .FirstOrDefaultAsync(g => g.Id == user.UserGroupId);

            var companyLink = await _context.CompanyLinks
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.UserId == user.UserId);

            Profile? company = null;
            if (companyLink != null)
            {
                // Only one query needed to get Company info from the ProfileId in link
                company = await _context.Profiles
                   .AsNoTracking()
                   .FirstOrDefaultAsync(c => c.Id == companyLink.ProfileId);
            }
            else
            {
                company = await _context.Profiles
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == user.ProfileId);
            }

            BusinessUnit? bussinessunit = null;
            if (user.UserGroupId == (int)CloverBackoffice.Components.Enums.UserGroups.TeamManager)
            {
                // Fetch from UserTeam based on UserId
                var userTeam = await _context.UserTeams
                    .AsNoTracking()
                    .FirstOrDefaultAsync(ut => ut.UserId == user.UserId && ut.IsUnAssigned == false);

                if (userTeam != null)
                {
                    bussinessunit = await _context.BusinessUnits
                        .AsNoTracking()
                        .FirstOrDefaultAsync(bu => bu.Id == userTeam.UnitId);
                }
            }
                else
            {
                if (companyLink != null)
                {
                    bussinessunit = await _context.BusinessUnits
                        .AsNoTracking()
                        .FirstOrDefaultAsync(u => u.Id == companyLink.BussinessUnitId);
                }
                else if (company != null)
                {
                    bussinessunit = await _context.BusinessUnits
                        .AsNoTracking()
                        .FirstOrDefaultAsync(u => u.Profile.Id == company.Id);
                }
            }


            // Get permissions-related data
            var userGroups = await _context.UserGroups.AsNoTracking().ToListAsync();
            var promptTypes = await _context.PromptTypes.AsNoTracking().ToListAsync();
            var systemModules = await _context.SystemModules.AsNoTracking().ToListAsync();
            var assignRights = await _context.AssignUserRights.AsNoTracking().ToListAsync();

            // Every role the user holds (UserRoles), highest first. Falls back to the
            // derived column for a user not yet backfilled.
            var roleIds = await _context.UserRoles
                .AsNoTracking()
                .Where(r => r.UserId == user.UserId)
                .Select(r => r.UserGroupId)
                .ToListAsync();
            if (roleIds.Count == 0 && user.UserGroupId != RoleHierarchy.NoRoleGroupId)
                roleIds.Add(user.UserGroupId);

            var heldGroups = userGroups
                .Where(g => roleIds.Contains(g.Id))
                .OrderBy(g => RoleHierarchy.RankOf(g.Id))
                .ToList();

            var modules = new List<LoginDto.ModulePermission>();

            foreach (var module in systemModules)
            {
                // One Prompts entry per role held. A single-role user gets exactly
                // the one entry they always did.
                var prompts = new Dictionary<string, List<PermissionMatrix.PromptTypesitem>>();

                foreach (var group in heldGroups)
                {
                    var matchedPromptTypeIds = assignRights
                        .Where(r => r.SystemModuleId == module.ModuleID && r.UserGroupId == group.Id)
                        .Select(r => r.PromptTypeId)
                        .Distinct()
                        .ToList();

                    prompts[group.Name] = promptTypes
                        .Where(p => matchedPromptTypeIds.Contains(p.PromptTypeId))
                        .Select(p => new PermissionMatrix.PromptTypesitem
                        {
                            PromptTypeTitle = p.PromptTypeTitle,
                            PromptTypeId = p.PromptTypeId
                        })
                        .ToList();
                }

                if (prompts.Count == 0)
                    prompts["Unknown"] = new List<PermissionMatrix.PromptTypesitem>();

                modules.Add(new LoginDto.ModulePermission
                {
                    ModuleId = module.ModuleID,
                    Name = module.ModuleName,
                    Prompts = prompts
                });
            }

            // A first login is the onboarding of every team the manager already has,
            // so none of them is a "new assignment". Only teams added after this
            // point stay unseen and raise the new-team popup on a later login.
            if (isFirstLogin)
            {
                var seenAt = DateTime.UtcNow;
                await _context.UserTeams
                    .Where(ut => ut.UserId == user.UserId && ut.AssignmentSeenAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(ut => ut.AssignmentSeenAt, seenAt));
            }

            // Relationships, so the client knows what each role covers.
            // Self-assigned rows (a PA+TM creating their own team) are never "new".
            var managedTeams = await (from ut in _context.UserTeams.AsNoTracking()
                                      join bu in _context.BusinessUnits.AsNoTracking() on ut.UnitId equals bu.Id
                                      where ut.UserId == user.UserId && !ut.IsUnAssigned && bu.IsDel != true
                                      orderby ut.CreatedDate descending
                                      select new ManagedTeamDto
                                      {
                                          UnitId = bu.Id,
                                          TeamName = bu.Name,
                                          AssignedDate = ut.CreatedDate,
                                          IsNewAssignment = ut.AssignmentSeenAt == null && ut.CreatedBy != ut.UserId
                                      })
                                     .ToListAsync();

            var managersInPortfolio = await (from a in _context.PortfolioManagerAssignments.AsNoTracking()
                                             join u in _context.Users.AsNoTracking() on a.ManagerUserId equals u.UserId
                                             where a.PortfolioManagerUserId == user.UserId && !a.IsUnAssigned && !u.IsDel
                                             orderby u.Name
                                             select new RelatedUserDto { UserId = u.UserId, Name = u.Name, Email = u.Username })
                                            .ToListAsync();

            var portfolioManager = await (from a in _context.PortfolioManagerAssignments.AsNoTracking()
                                          join u in _context.Users.AsNoTracking() on a.PortfolioManagerUserId equals u.UserId
                                          where a.ManagerUserId == user.UserId && !a.IsUnAssigned && !u.IsDel
                                          select new RelatedUserDto { UserId = u.UserId, Name = u.Name, Email = u.Username })
                                         .FirstOrDefaultAsync();
            // Generate token stored in AuthToken Table
            var token = _tokenGenerator.GenerateToken(user, email);

            // Save token in AuthTokens table
            var authToken = new UserToken
            {
                UserId = user.UserId,
                Token = token,
                ExpiryDate = DateTime.UtcNow.AddMinutes(TOKEN_EXPIRY)
            };
            var refreshtoken = _tokenGenerator.GenerateToken(user, email);
            // refresh token for 6 days 
            var authToken6Days = new UserToken
            {
                UserId = user.UserId,
                Token = refreshtoken,
                ExpiryDate = DateTime.UtcNow.AddMinutes(TOKEN_EXPIRY * 2)
            };
            _context.UserTokens.AddRange(authToken, authToken6Days);

            // Update LastLoginDate on every login
            user.LastLoginDate = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            // Return the full LoginDto
            return new LoginDto
            {
                User = new LoginDto.UserDto
                {
                    UserId = user.UserId,
                    Name = user.Username
                },
                UserGroup = userGroup != null
                  ? new LoginDto.UserGroupDto
                  {
                      Id = userGroup.Id,
                      Name = userGroup.Name,
                      IsAdmin = userGroup.IsAdmin
                  }
                  : null,
                Company = company != null
                  ? new LoginDto.CompanyDto
                  {
                      Id = (int)company.Id,
                      Name = company.ProfileName,
                      IsPartner = company.IsPartner
                  }
                  : null,
                CompanyUnit = bussinessunit != null
                  ? new List<LoginDto.BussinessUnitDto>
                  {
                      new LoginDto.BussinessUnitDto
                      {
                          UnitId = (int)bussinessunit.Id,
                          UnitName = bussinessunit.Name
                      }
                  }
                  : new List<LoginDto.BussinessUnitDto>(),
                Roles = heldGroups
                    .Select(g => new LoginDto.UserGroupDto { Id = g.Id, Name = g.Name, IsAdmin = g.IsAdmin })
                    .ToList(),
                ManagedTeams = managedTeams,
                HasNewTeamAssignment = managedTeams.Any(t => t.IsNewAssignment),
                NewTeamAssignments = managedTeams.Where(t => t.IsNewAssignment).ToList(),
                ManagersInPortfolio = managersInPortfolio,
                PortfolioManager = portfolioManager,
                Modules = modules,
                IsVerified = user.IsVerified,
                IsFirstLogin = isFirstLogin,
                accessToken = token,
                refreshToken = refreshtoken,
                expiresIn = TOKEN_EXPIRY
            };
        }

        public async Task<bool> UpdateUserVerificationAsync(User user, bool isVerified, string status)
        {
            user.IsVerified = isVerified;
            user.Status = status;
            user.ModifiedDate = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            if (user.LastLoginDate.HasValue)
                user.LastLoginDate = DateTime.SpecifyKind(user.LastLoginDate.Value, DateTimeKind.Utc);
            _context.Users.Update(user);
            return await _context.SaveChangesAsync() > 0;
        }

        public async Task<User> GetUserByEmailAsync(string email)
        {

            return await _context.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == email.ToLower() && u.IsVerified == true);
        }

        // Login lookup — does NOT filter on IsVerified so the login flow can tell
        // "no such user" apart from "registered but email not verified" and return
        // the correct message. Verified == true filtering stays in
        // GetUserByEmailAsync (used by password-reset etc.).
        public async Task<User> GetUserByEmailIncludingUnverifiedAsync(string email)
        {
            return await _context.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == email.ToLower());
        }

        public async Task<User> GetUserByEmailForgot(string email)
        {
            return await _context.Users.FirstOrDefaultAsync(u => u.Username == email);
        }
        // Two things here were wrong for as long as this method has existed, and
        // both only showed up on the invitation flow because that is the one that
        // reaches it with an unverified user:
        //
        //   * it looked the user up with GetUserByEmailAsync, which filters
        //     IsVerified == true. An invited manager is unverified by definition —
        //     the invitation is what proves the mailbox — so the lookup returned
        //     null and the caller answered "Failed to update password." Whether an
        //     unverified account may reset at all is decided by ResetPasswordAsync
        //     before it gets here, so this lookup must not second-guess it.
        //
        //   * it stored the password AS TYPED. Passwords are BCrypt hashes
        //     (PasswordUtility.Encrypt) and login verifies against one, so every
        //     password set through here was unusable — the user reset it and then
        //     could not log in with what they had just chosen. This affected
        //     forgot-password too, not only invitations.
        public async Task<bool> UpdateUserPasswordAsync(string email, string newPassword)
        {
            var user = await GetUserByEmailIncludingUnverifiedAsync(email);
            if (user == null) return false;

            user.Password = PasswordUtility.Encrypt(newPassword);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<List<BusinessUnitDto>> GetUnitsByProfileIdAsyncold(long profileId, int userId)
        {
            var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId);

            if (user == null)
                return new List<BusinessUnitDto>();

            if (user.UserGroupId == (int)CloverBackoffice.Components.Enums.UserGroups.TeamManager)
            {
                // Fetch units from UserTeam for a Manager
                var userTeams = await _context.UserTeams
                .Where(ut => ut.UserId == userId && ut.IsUnAssigned == false)
                .ToListAsync();

                var unitIds = userTeams.Select(ut => ut.UnitId).Distinct().ToList();

                return await _context.BusinessUnits
                    .Where(bu => unitIds.Contains(bu.Id))
                    .Select(bu => new BusinessUnitDto
                    {
                        Id = bu.Id,
                        Name = bu.Name
                    })
                    .ToListAsync();
            }
            else
            {
                // Default logic: fetch units by ProfileId
                return await _context.BusinessUnits
                    .Where(bu => bu.Profile.Id == profileId && bu.IsDel == false)
                    .Select(bu => new BusinessUnitDto
                    {
                        Id = bu.Id,
                        Name = bu.Name
                    })
                    .ToListAsync();
            }
        }

        //Parent and Child 
        public async Task<List<BusinessUnitDto>> GetUnitsByProfileIdAsyncolooo(long profileId, int userId)
        {
            var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId);
            if (user == null)
                return new List<BusinessUnitDto>();

            List<long> rootUnitIds;

            if (user.UserGroupId == (int)UserGroups.TeamManager)
            {
                rootUnitIds = await _context.UserTeams
                    .Where(ut => ut.UserId == userId && ut.IsUnAssigned == false)
                    .Select(ut => (long)ut.UnitId)
                    .Distinct()
                    .ToListAsync();
            }
            else // Profile Admin
            {
                rootUnitIds = await _context.BusinessUnits
                    .Where(bu => bu.Profile.Id == profileId && bu.ParentId == null && bu.IsDel != true)
                    .Select(bu => (long)bu.Id)
                    .ToListAsync();
            }

            if (!rootUnitIds.Any())
                return new List<BusinessUnitDto>();

            // Fetch all units under the company to build hierarchy
            var allUnits = await _context.BusinessUnits
                .Where(bu => bu.Profile.Id == profileId && bu.IsDel != true)
                .ToListAsync();

            var unitLookup = allUnits.ToLookup(bu => bu.ParentId);

            List<BusinessUnitDto> BuildHierarchy(long? parentId)
            {
                return unitLookup[(int?)parentId]
                    .Select(unit => new BusinessUnitDto
                    {
                        Id = unit.Id,
                        Name = unit.Name,
                        Children = BuildHierarchy(unit.Id)
                    })
                    .ToList();
            }

            var result = new List<BusinessUnitDto>();

            foreach (var rootId in rootUnitIds)
            {
                var rootUnit = allUnits.FirstOrDefault(u => u.Id == rootId);
                if (rootUnit != null)
                {
                    var dto = new BusinessUnitDto
                    {
                        Id = rootUnit.Id,
                        Name = rootUnit.Name,
                        Children = BuildHierarchy(rootUnit.Id)
                    };
                    result.Add(dto);
                }
            }

            return result;
        }

        //Parent and Child (child is Parent )
        public async Task<List<BusinessUnitDto>> GetUnitsByProfileIdAsync(long profileId, int userId)
        {


            var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId);
            if (user == null)
                return new List<BusinessUnitDto>();
            switch (user.UserGroupId)
            {
                case (int)UserGroups.TeamManager:
                    return TeamManagerAdmin(profileId, userId);
                case (int)UserGroups.RridaAdmin:
                case (int)UserGroups.ProfileAdmin:
                    return CloveraAdmin(profileId);
            }
            return new List<BusinessUnitDto>();
        }
        private List<BusinessUnitDto> CloveraAdmin(long profileId)
        {
            try
            {
                var allUnits = _context.BusinessUnits
                    .Where(bu => bu.Profile.Id == profileId && bu.IsDel != true)
                    .OrderBy(bu => bu.Name)
                    .ToList();

                if (!allUnits.Any())
                    return new List<BusinessUnitDto>();

                // Create a lookup for fast parent-child access
                var unitLookup = allUnits.ToLookup(bu => bu.ParentId);

                // Recursive method to build the hierarchy
                List<BusinessUnitDto> BuildHierarchy(int? parentId)
                {
                    var children = unitLookup[parentId].ToList();
                    return children.Select(unit => new BusinessUnitDto
                    {
                        Id = unit.Id,
                        Name = unit.Name,
                        Children = BuildHierarchy((int)unit.Id)
                    }).ToList();
                }

                // Start building from root units (ParentId == null)
                var result = BuildHierarchy(null);

                return result;
            }
            catch (Exception ex)
            {
                return new List<BusinessUnitDto>();
            }
        }
        private List<BusinessUnitDto> TeamManagerAdmin(long profileId, int userId)
        {
            try
            {
                // 1. Assigned units
                var rootUnitIds = _context.UserTeams
                    .Where(ut => ut.UserId == userId && ut.IsUnAssigned == false)
                    .Select(ut => (long)ut.UnitId)
                    .Distinct()
                    .ToList();

                if (!rootUnitIds.Any())
                    return new List<BusinessUnitDto>();

                // 2. Get ALL units of company
                var allUnits = _context.BusinessUnits
                    .Where(bu => bu.Profile.Id == profileId && bu.IsDel != true)
                    .OrderBy(bu => bu.Name)
                    .ToList();

                if (!allUnits.Any())
                    return new List<BusinessUnitDto>();

                var unitLookup = allUnits.ToLookup(bu => bu.ParentId);
                var unitDict = allUnits.ToDictionary(bu => (long)bu.Id);

                // 3. Check if unit has an assigned ancestor
                bool HasAncestorInAssignedUnits(long unitId)
                {
                    if (!unitDict.TryGetValue(unitId, out var unit) || unit.ParentId == null)
                        return false;

                    var parentId = (long)unit.ParentId.Value;
                    if (rootUnitIds.Contains(parentId))
                        return true;

                    return HasAncestorInAssignedUnits(parentId);
                }

                // 4. Build hierarchy recursively
                List<BusinessUnitDto> BuildHierarchy(int? parentId)
                {
                    return unitLookup[parentId]
                        .Select(unit => new BusinessUnitDto
                        {
                            Id = unit.Id,
                            Name = unit.Name,
                            Children = BuildHierarchy((int)unit.Id)
                        })
                        .ToList();
                }

                // 5. Only TRUE root units (avoid duplicates)
                var rootUnits = allUnits
                    .Where(bu => rootUnitIds.Contains(bu.Id) && !HasAncestorInAssignedUnits(bu.Id))
                    .ToList();

                // 6. Final result
                return rootUnits.Select(unit => new BusinessUnitDto
                {
                    Id = unit.Id,
                    Name = unit.Name,
                    Children = BuildHierarchy((int)unit.Id)
                }).ToList();
            }
            catch
            {
                return new List<BusinessUnitDto>();
            }
        }

        private List<BusinessUnitDto> TeamUnit(long profileId, int userId)
        {
            try
            {
                int unit = 0;
                unit = _context.CompanyLinks
                    .Where(ut => ut.UserId == userId && ut.ProfileId == profileId)
                    .Select(ut => (ut.UnitId).Value).FirstOrDefault();


                var allUnits = _context.BusinessUnits
                    .Where(bu => bu.Profile.Id == profileId && (bu.Id == unit || bu.ParentId == unit) && bu.IsDel != true)
                    .OrderBy(bu => bu.Name)
                    .ToList();

                if (!allUnits.Any())
                    return new List<BusinessUnitDto>();

                // Create a lookup for fast parent-child access
                var unitLookup = allUnits.ToLookup(bu => bu.ParentId);

                // Recursive method to build the hierarchy
                List<BusinessUnitDto> BuildHierarchy(int? parentId)
                {
                    var children = unitLookup[parentId].ToList();
                    return children.Select(unit => new BusinessUnitDto
                    {
                        Id = unit.Id,
                        Name = unit.Name,
                        Children = BuildHierarchy((int)unit.Id)
                    }).ToList();
                }

                // Start building from root units (ParentId == null)
                var result = BuildHierarchy(null);

                return result;
            }
            catch (Exception ex)
            {
                return new List<BusinessUnitDto>();
            }
        }

        public async Task<User?> GetUpsertUserByEmailAsync(string email)
        {
            return await _context.Users
                .FirstOrDefaultAsync(u => u.Username.ToLower() == email.ToLower());
        }

        // Total non-deleted users of a company, INCLUDING the Company Admin created
        // automatically at registration.
        //
        // This is what firstTimeUserAdded is derived from — no stored flag, no
        // hardcoded id:
        //   1 → registration only, admin alone
        //   2 → admin + the FIRST user the admin added  → firstTimeUserAdded = true
        //  >2 → later users                             → false
        //
        // Soft-deleted rows are excluded so the number matches the list get-users
        // returns for the same company.
        public async Task<int> CountCompanyUsersAsync(int profileId)
        {
            return await _context.Users
                .CountAsync(u => u.ProfileId == profileId && !u.IsDel);
        }

        public async Task AddUserAsync(User user)
        {
            await _context.Users.AddAsync(user);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateUserAsync(User user)
        {
            if (user.LastLoginDate.HasValue && user.LastLoginDate.Value.Kind != DateTimeKind.Utc)
                user.LastLoginDate = DateTime.SpecifyKind(user.LastLoginDate.Value, DateTimeKind.Utc);
            if (user.LastFailedLoginAt.HasValue && user.LastFailedLoginAt.Value.Kind != DateTimeKind.Utc)
                user.LastFailedLoginAt = DateTime.SpecifyKind(user.LastFailedLoginAt.Value, DateTimeKind.Utc);

            _context.Users.Update(user);
            await _context.SaveChangesAsync();
        }
        public async Task<User> GetUserByIdAsync(int id)
        {
            return await _context.Users.FirstOrDefaultAsync(u => u.UserId == id && !u.IsDel);
        }
        // Deleting a user must also drop the team assignments they hold: the team
        // hierarchy resolves a unit's manager purely from its active UserTeams row
        // (see CompanyTeamService.loadManagerNameWithIsReadOnly), so a row left
        // behind keeps showing the deleted user as that team's manager. The teams
        // are unassigned exactly the way the manual unassign path does it —
        // IsUnAssigned = true, preserving the historical row — and both writes go
        // through one SaveChanges so a user can never end up deleted while still
        // holding a live assignment.
        //
        // Returns the units that lost their manager.
        public async Task<List<uint>> DeleteUserAsync(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null)
                return new List<uint>();

            if (user.LastLoginDate.HasValue)
                user.LastLoginDate = DateTime.SpecifyKind(user.LastLoginDate.Value, DateTimeKind.Utc);
            user.ModifiedDate = DateTime.UtcNow.ToString("o");
            user.IsDel = true;
            _context.Users.Update(user);

            // A user can manage several units, so every active row is released.
            var assignments = await _context.UserTeams
                .Where(ut => ut.UserId == id && !ut.IsUnAssigned)
                .ToListAsync();

            foreach (var assignment in assignments)
                assignment.IsUnAssigned = true;

            await _context.SaveChangesAsync();

            return assignments.Select(a => a.UnitId).Distinct().ToList();
        }
        public async Task<List<UserGroupsDto>> GetAllUserGroupsAsync()
        {
            return await _context.UserGroups
                .Select(ug => new UserGroupsDto
                {
                    Id = ug.Id,
                    Name = ug.Name
                })
                // RRIDA Admin is never offered: it is platform staff, minted only by
                // DatabaseSeeder, and upsert-user refuses it outright.
                .Where(a => a.Id != (int)UserGroups.RridaAdmin)
                // Nor is "No Role": it marks a user with no role, it is not one.
                .Where(a => a.Id != RoleHierarchy.NoRoleGroupId)
                .ToListAsync();
        }

        /// <summary>
        /// Every role row, RRIDA Admin included, with its description.
        ///
        /// Separate from GetAllUserGroupsAsync rather than a flag on it: that
        /// method is the ASSIGNABLE-role picker and its omission of RRIDA Admin is
        /// a rule, not a default anyone should be able to switch off. This one
        /// answers "what roles exist", which the hierarchy endpoint needs because a
        /// RRIDA Admin must see their own role in it.
        ///
        /// Returned unordered and unfiltered — the hierarchy is a code-level
        /// concept (the ids are not in rank order) and belongs to the service.
        /// </summary>
        public async Task<List<RoleHierarchyDto>> GetAllRolesAsync()
        {
            return await _context.UserGroups
                .AsNoTracking()
                .Select(ug => new RoleHierarchyDto
                {
                    Id = ug.Id,
                    Name = ug.Name,
                    Description = ug.Description
                })
                .ToListAsync();
        }
        public async Task<Profile?> GetByIdAsync(uint profileId)
        {
            return await _context.Profiles.FindAsync(profileId);
        }
        public async Task<bool> EmailExistsAsync(string email)
        {
            // Normalise here rather than trusting every caller to pre-lower the
            // argument — the Users comparison silently missed matches whenever a
            // caller passed the raw address.
            var normalized = (email ?? string.Empty).Trim().ToLower();

            bool userExists = await _context.Users
                .AnyAsync(u => u.Username.ToLower() == normalized && !u.IsDel);

            bool contactExists =
                await _context.Profiles.AnyAsync(c => c.ContactEmailAddress != null
                    && c.ContactEmailAddress.ToLower() == normalized)
                || await _context.BusinessUnits.AnyAsync(b => b.ContactEmailAddress != null
                    && b.ContactEmailAddress.ToLower() == normalized);

            return userExists || contactExists;
        }
        public async Task<UserToken?> GetTokenByUserIdAsync(int userId, string token)
        {
            return await _context.UserTokens
                .FirstOrDefaultAsync(t => t.UserId == userId && t.Token.ToLower() == token.ToLower() && t.ExpiryDate > DateTime.UtcNow);
        }

        public async Task UpdateTokenAsync(UserToken tokenEntity)
        {
            _context.UserTokens.Update(tokenEntity);
            await _context.SaveChangesAsync();
        }


        public async Task<UserToken> GetTokenInfoAsync(int userId, string token)
        {
            return await _context.UserTokens
                .FirstOrDefaultAsync(t => t.UserId == userId && t.Token.ToLower() == token.ToLower());
        }

        public async Task SaveResetPasswordLink(ResetPasswordLinks model)
        {
            try
            {
                await _context.ResetPasswordLinks.AddAsync(model);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                // Re-throw or handle as needed
                throw new Exception($"Failed to save reset password link: {ex.Message}", ex);
            }
        }
        public async Task<ResetPasswordLinks> GetResetPasswordLink(int userId, string encryptedLink)
        {
            var list = await _context.ResetPasswordLinks
                .Where(x => x.UserId == userId && !x.IsDel)
                .ToListAsync();

            foreach (var item in list)
            {
                string storedValue = item.ResetLink;

                // If full URL is stored, extract the encrypted part
                if (storedValue.Contains("data="))
                {
                    storedValue = storedValue.Split("data=")[1];
                }

                if (storedValue == encryptedLink)
                {
                    return item; // FOUND MATCH
                }
            }

            return null; // No match found
        }
        // Marks one link consumed and nothing else — see the interface for why
        // this is separate from UpdateResetPasswordLink.
        public async Task MarkResetPasswordLinkUsedAsync(int id)
        {
            var existing = await _context.ResetPasswordLinks
                .FirstOrDefaultAsync(a => a.Id == id);

            if (existing == null || existing.IsLinkUsed)
                return;

            existing.IsLinkUsed = true;
            _context.Entry(existing).Property(c => c.IsLinkUsed).IsModified = true;
            await _context.SaveChangesAsync();
        }

        public async Task UpdateResetPasswordLink(ResetPasswordLinks model, string Password)
        {
            var existing = await _context.ResetPasswordLinks
                .FirstOrDefaultAsync(a => a.Id == model.Id);

            if (existing == null)
                return;

            // Update Link as Used
            existing.IsLinkUsed = true;

            _context.Entry(existing).Property(c => c.IsLinkUsed).IsModified = true;

            // Update User IsVerified
            if (existing.UserId > 0)
            {
                var userInfo = await _context.Users
                    .FirstOrDefaultAsync(a => a.UserId == existing.UserId);

                if (userInfo != null)
                {
                    userInfo.IsVerified = true;
                    userInfo.Password = PasswordUtility.Encrypt(!string.IsNullOrWhiteSpace(Password) ? Password : "12345678");
                    _context.Entry(userInfo).Property(c => c.IsVerified).IsModified = true;
                    _context.Entry(userInfo).Property(c => c.Password).IsModified = true;

                }
            }

            await _context.SaveChangesAsync();
        }
        public async Task<User> GetUserByEmailAndIdAsync(string email, int UserId)
        {
            return await _context.Users.FirstOrDefaultAsync(u => u.Username == email && u.UserId == UserId);
        }

        public async Task<List<CompanyDto>> GetCompanyPatnerListAsync(int userId)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);
            if (user == null)
                return new List<CompanyDto>();

            var userProfileId = user.ProfileId;
            if (userProfileId == 0)
                return new List<CompanyDto>();

            var userCompany = await _context.Profiles.FirstOrDefaultAsync(c => c.Id == userProfileId);
            if (userCompany == null || !userCompany.IsPartner)
                return new List<CompanyDto>();

            var rawList = await _context.Profiles
                .Where(c => !c.IsDel && (c.Id == userCompany.Id || c.PartnerId == userCompany.Id))
                .OrderBy(c => c.ProfileName)
                .Select(c => new
                {
                    c.Id,
                    Name = c.ProfileName,
                    c.DomainName,
                    c.IsPartner,
                    c.ReferredBy,
                    ReferredCompanyName = _context.Profiles
                         .Where(rc => rc.Id == c.ReferredBy)
                         .Select(rc => rc.ProfileName)
                         .FirstOrDefault(),
                    LinkedUser = _context.CompanyLinks
                        .Where(cl => cl.ProfileId == c.Id)
                        .OrderBy(cl => cl.UserId)
                        .Select(cl => new
                        {
                            FullName = cl.User.Name,
                            Email = cl.User.Username
                        })
                        .FirstOrDefault()
                })
                .ToListAsync();

            return rawList.Select(c => new CompanyDto
            {
                Id = (int)c.Id,
                Name = c.Name,
                DomainName = c.DomainName,
                FirstName = c.LinkedUser?.FullName?.Split(' ', 2)[0],
                LastName = c.LinkedUser?.FullName?.Split(' ', 2).Length > 1 ? c.LinkedUser.FullName.Split(' ', 2)[1] : "",
                EmailAddress = c.LinkedUser?.Email,
                IsPartner = c.IsPartner,
                ReferredById = c.ReferredBy,
                ReferredName = c.ReferredCompanyName
            }).ToList();
        }

        public async Task<List<CompanyDto>> GetAllPartnerCompaniesAsync()
        {
            var rawList = await _context.Profiles
                .Where(c => c.IsPartner)
                .OrderBy(c => c.ProfileName)
                .Select(c => new
                {
                    c.Id,
                    Name = c.ProfileName,
                    c.DomainName,
                    c.IsPartner,
                    c.ReferredBy,
                    ReferredCompanyName = _context.Profiles
                         .Where(rc => rc.Id == c.ReferredBy)
                         .Select(rc => rc.ProfileName)
                         .FirstOrDefault(),
                    LinkedUser = _context.CompanyLinks
                        .Where(cl => cl.ProfileId == c.Id)
                        .OrderBy(cl => cl.UserId)
                        .Select(cl => new
                        {
                            FullName = cl.User.Name,
                            Email = cl.User.Username
                        })
                        .FirstOrDefault()
                })
                .ToListAsync();

            return rawList.Select(c => new CompanyDto
            {
                Id = (int)c.Id,
                Name = c.Name,
                DomainName = c.DomainName,
                FirstName = c.LinkedUser?.FullName?.Split(' ', 2)[0],
                LastName = c.LinkedUser?.FullName?.Split(' ', 2).Length > 1 ? c.LinkedUser.FullName.Split(' ', 2)[1] : "",
                EmailAddress = c.LinkedUser?.Email,
                IsPartner = c.IsPartner,
                ReferredById = c.ReferredBy,
                ReferredName = c.ReferredCompanyName
            }).ToList();
        }

        public async Task<(bool Success, string Message)> AssignPartnerAsync(int profileId, int partnerId)
        {
            var partner = await _context.Profiles.FirstOrDefaultAsync(c => c.Id == partnerId);
            if (partner == null)
                return (false, ResponseMessages.Errors.ReferredCompanyNotFound);

            if (!partner.IsPartner)
                return (false, "The selected company is not a partner.");

            var company = await _context.Profiles
                .Include(c => c.Partner)
                .FirstOrDefaultAsync(c => c.Id == profileId);
            if (company == null)
                return (false, ResponseMessages.Errors.ReferredCompanyNotFound);

            // Block if company already has a different partner assigned
            //if (company.PartnerId.HasValue && company.PartnerId.Value != (uint)partnerId)
            //{
            //    var existingPartnerName = company.Partner?.Name ?? "another partner";
            //    return (false, string.Format(ResponseMessages.Errors.CompanyAlreadyHasPartner, existingPartnerName));
            //}

            // Link the company to the partner — do NOT touch IsPartner on either side
            company.ReferredBy = partnerId;
            company.PartnerId = (uint)partnerId;

            await _context.SaveChangesAsync();
            return (true, string.Empty);
        }

        public async Task<(bool Success, string Message)> UnassignPartnerAsync(int profileId, int partnerId)
        {
            var company = await _context.Profiles.FirstOrDefaultAsync(c => c.Id == profileId);
            if (company == null)
                return (false, ResponseMessages.Errors.CompanyNotFound);

            if (!company.PartnerId.HasValue)
                return (false, "This company does not have an assigned partner.");

            if (company.PartnerId.Value != (uint)partnerId)
                return (false, "The provided partner does not match the company's assigned partner.");

            company.PartnerId = null;
            company.ReferredBy = null;

            await _context.SaveChangesAsync();
            return (true, string.Empty);
        }

        public async Task IncrementFailedLoginAttemptsAsync(int userId)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);
            if (user == null) return;

            user.FailedLoginAttempts += 1;
            user.LastFailedLoginAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        public async Task ResetFailedLoginAttemptsAsync(int userId)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);
            if (user == null) return;

            user.FailedLoginAttempts = 0;
            user.LastFailedLoginAt = null;
            await _context.SaveChangesAsync();
        }

        // Trial-aware company create.
        //
        // Differs from AddCompanyWithUserAsync:
        //   - User is created already verified + Active (no email-verify step).
        //   - A CompanySubscription row is provisioned as a 30-day Trial with the
        //     matched Plan's prices + trial window.
        //   - Exactly 1 CompanyLicense row is minted with a key of the form
        //     CLD-TRI001-{ProfileId}, unassigned (no default team).
        //   - The user holds Profile Admin + Team Manager (AddRegistrationRoles).
        //
        // All side-effects share one transaction; rollback on any failure.
        public async Task<TrialProfileResponseDto> AddTrialProfileWithUserAsync(TrialProfileCreateDto dto)
        {
            string[] parts = Array.Empty<string>();
            if (!string.IsNullOrEmpty(dto.EmailAddress))
                parts = dto.EmailAddress.Split('@');

            // Trial flags are no longer client-controlled: every call is a 30-day
            // trial with exactly 1 licence (ProvisionRegistrationTrialAsync).

            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Identity guard — runs BEFORE any row is written.
                //
                // Unlike AddCompanyWithUserAsync (which looks the user up and reuses
                // it) this method used to Add() a User unconditionally, so a second
                // Users row was minted whenever the controller's duplicate check
                // missed. It did miss for accounts that live in Users but not in
                // Contacts — the add-user / partner-user path — which is exactly how
                // one email ended up on two Users rows.
                //
                // The controller now checks both tables; this stays as the last line
                // of defence and, because it runs inside the same transaction as the
                // insert, it also closes the check-then-insert race.
                var normalizedEmail = (dto.EmailAddress ?? string.Empty).Trim().ToLower();
                var duplicateUser = await _context.Users
                    .AnyAsync(u => u.Username.ToLower() == normalizedEmail && !u.IsDel);
                if (duplicateUser)
                    throw new InvalidOperationException(ResponseMessages.Errors.EmailAlreadyExist);

                var duplicateContact =
                    await _context.Profiles.AnyAsync(c => c.ContactEmailAddress != null
                        && c.ContactEmailAddress.ToLower() == normalizedEmail)
                    || await _context.BusinessUnits.AnyAsync(b => b.ContactEmailAddress != null
                        && b.ContactEmailAddress.ToLower() == normalizedEmail);
                if (duplicateContact)
                    throw new InvalidOperationException(ResponseMessages.Errors.EmailAlreadyExist);

                // Trial registration always provisions a fresh Company + default
                // BusinessUnit, even when the email's domain already maps to one
                // or more existing companies. Domain uniqueness guard intentionally
                // disabled — keep commented for reference / quick re-enable.
                //
                // var domain = parts.Length > 1 ? parts[1] : dto.EmailAddress;
                // var domainTaken = await _context.Companies
                //     .AnyAsync(c => c.DomainName.ToLower() == domain.ToLower());
                // if (domainTaken)
                //     throw new InvalidOperationException(
                //         $"A company is already registered for domain '{domain}'. Use a different email domain.");

                // Plan lookup — the single-unit plan (MaxTeams == 1). Billing cycle
                // is not client-controlled: the trial is always Monthly.
                var plan = await FindRegistrationTrialPlanAsync();

                // Step 1: Profile, with its contact stored inline.
                var companyCodes = BussinessManager.GenerateCodes();
                var company = new Profile
                {
                    DomainName = parts.Length > 1 ? parts[1] : dto.EmailAddress,
                    ProfileName = dto.ProfileName,
                    ProfileShortCode = companyCodes.ShortCode,
                    RegistrationDate = DateTime.UtcNow,
                    ContactFirstName = dto.FirstName,
                    ContactLastName = dto.LastName,
                    ContactEmailAddress = dto.EmailAddress
                };
                _context.Profiles.Add(company);
                await _context.SaveChangesAsync();

                // Step 3: NO default BusinessUnit.
                //
                // Registration used to create one here named after the profile, so
                // the Profile Admin logged in already owning a team nobody asked
                // for. A team is now made only when the admin goes through Team
                // Setup (or the Teams screen) themselves, and they are free to skip
                // that and come back to it later. The paid /Create path never
                // created one either — the two flows now agree.

                // Step 4: User — same email-verification flow as the paid /Create
                // path: created unverified (Pending), activated only after the
                // emailed verification link is clicked.
                var user = new User
                {
                    Username = dto.EmailAddress,
                    Name = !string.IsNullOrWhiteSpace(dto.FirstName) || !string.IsNullOrWhiteSpace(dto.LastName)
                        ? $"{dto.FirstName} {dto.LastName}".Trim()
                        : (parts.Length > 0 ? parts[0] : dto.EmailAddress),
                    Password = PasswordUtility.Encrypt(!string.IsNullOrWhiteSpace(dto.Password) ? dto.Password : "12345678"),
                    CreatedDate = DateTime.UtcNow.ToString("o"),
                    ModifiedDate = DateTime.UtcNow.ToString("o"),
                    IsActive = true,
                    IsDel = false,
                    UserGroupId = (int)CloverBackoffice.Components.Enums.UserGroups.ProfileAdmin,
                    LastLoginDate = null,
                    IsVerified = false,                                      // ← verify via email link (matches /Create)
                    Status = ((int)UserStatus.Pending).ToString(),           // ← verify via email link (matches /Create)
                    CreatedBy = CreatedByType.System.ToString(),
                    TenantId = "",
                    Type = UserType.ProfileAdmin.ToString(),
                    ProfileId = (int)company.Id
                };
                AddRegistrationRoles(user);
                _context.Users.Add(user);
                await _context.SaveChangesAsync();

                // Auth token (kept for parity with paid flow — login still works without it)
                _context.UserTokens.Add(new UserToken
                {
                    UserId = user.UserId,
                    Token = Guid.NewGuid().ToString(),
                    ExpiryDate = DateTime.UtcNow.AddDays(3)
                });
                await _context.SaveChangesAsync();

                // Step 5: CompanyLink
                _context.CompanyLinks.Add(new CompanyLink
                {
                    ProfileId = (int)company.Id,
                    UserId = user.UserId,
                    ParentId = 0,
                    UnitId = 0,
                    ExpiryDate = string.Empty,

                    // No team yet, so the link points at none. Login reads this to
                    // fill CompanyUnit and now returns an empty list, which is what
                    // "the admin has no team" looks like on the wire.
                    BussinessUnitId = null
                });
                await _context.SaveChangesAsync();

                // Steps 6-7: 30-day trial subscription, $0 trial payment and the
                // 1 trial licence. Shared with /Create (ProvisionRegistrationTrialAsync).
                await ProvisionRegistrationTrialAsync(company.Id, user.UserId, plan, DateTime.UtcNow);

                // Step 8: NO license is assigned yet. The trial licence stays pooled until
                // the admin creates their first team, and team coverage takes it
                // at that point. Pinning license #1 to a default team was the
                // other half of the auto-created team: it made the team look real in
                // the licence screens as well.

                await transaction.CommitAsync();

                return new TrialProfileResponseDto
                {
                    UserId = user.UserId,
                    Email = user.Username,
                    ProfileId = (int)company.Id,

                    // Nothing to report: registration creates no team. Left at their
                    // defaults rather than removed so existing clients still parse
                    // the response.
                    BusinessUnitId = 0,
                    BusinessUnitShortCode = string.Empty,
                    IsNew = true,
                    // Same message as the paid /Create flow — user must verify
                    // their email before they can log in.
                    Message = ResponseMessages.Success.RegistrationSuccess
                };
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        // ---------- Profile Admin registration: roles + 30-day trial ----------

        private const int RegistrationTrialDays = 30;

        // Exactly one trial licence per newly registered profile. The Profile
        // Admin holds the Team Manager role, so this one licence is theirs and
        // covers up to 3 teams; more Team Managers need a purchased licence.
        private const int RegistrationTrialLicenseCount = 1;
        private const int RegistrationTrialTeams = 1;

        // A newly registered Profile Admin holds Profile Admin AND Team Manager.
        // Users.UserGroupId (the derived highest role) stays Profile Admin. As a
        // Team Manager they count against the trial licences like any other; the
        // registration itself is never licence-checked.
        private static void AddRegistrationRoles(User user)
        {
            var now = DateTime.UtcNow;
            user.Roles.Add(new UserRole { UserGroupId = (int)UserGroups.ProfileAdmin, CreatedDate = now });
            user.Roles.Add(new UserRole { UserGroupId = (int)UserGroups.TeamManager, CreatedDate = now });
        }

        // The single-unit plan (MaxTeams == 1) the trial is recorded against.
        private async Task<Plan> FindRegistrationTrialPlanAsync()
        {
            return await _context.Plans
                       .Where(p => p.IsActive && p.MaxTeams == RegistrationTrialTeams)
                       .OrderBy(p => p.SortOrder)
                       .FirstOrDefaultAsync()
                   ?? throw new InvalidOperationException(
                       $"No active Plan found for MaxTeams={RegistrationTrialTeams}. Seed a Plan row first.");
        }

        // The Profile Admin's free trial, starting now: a Trial CompanySubscription
        // (TrialStartDate / TrialEndDate / CurrentPeriodEnd = now + 30 days), a $0
        // trial payment, and 1 Trial licence expiring with it. Runs inside the
        // caller's registration transaction.
        //
        // Idempotent per profile: if the profile already has a subscription the
        // trial was provisioned before, so it is returned as-is and nothing more
        // is minted. Keeps a retried/re-run provisioning from adding licences.
        private async Task<CompanySubscription> ProvisionRegistrationTrialAsync(uint profileId, int userId, Plan plan, DateTime now)
        {
            var existingSubscription = await _context.CompanySubscriptions
                .Where(s => s.ProfileId == profileId && !s.IsDel)
                .OrderBy(s => s.Id)
                .FirstOrDefaultAsync();
            if (existingSubscription != null)
                return existingSubscription;

            var trialEnd = now.AddDays(RegistrationTrialDays);
            var subscription = new CompanySubscription
            {
                ProfileId = profileId,
                PlanId = plan.Id,
                BillingCycle = BillingCycle.Monthly,
                Status = SubscriptionStatus.Trial,
                TeamCount = RegistrationTrialTeams,
                MaxTeamsAllowed = RegistrationTrialLicenseCount,
                MaxPeopleAllowed = RegistrationTrialLicenseCount * plan.MaxPeoplePerTeam,
                TrialStartDate = now,
                TrialEndDate = trialEnd,
                CurrentPeriodStart = now,
                CurrentPeriodEnd = trialEnd,
                IsTrialUsed = true,
                CreatedAt = now,
                UpdatedAt = now
            };
            // Through the repo so the CompanySubscriptionAuditLog INSERT fires.
            await _subscriptionRepo.CreateAsync(subscription);

            // Trial activation payment: Amount 0, method Trial, so subscription ->
            // payments joins behave the same for trial and paid.
            await _paymentRepo.CreateAsync(new Payment
            {
                ProfileId = profileId,
                CompanySubscriptionId = subscription.Id,
                Amount = 0m,
                Currency = "usd",
                PaymentMethod = PaymentMethodType.Trial,
                Status = PaymentStatus.Succeeded,
                Description = $"Trial activation - {RegistrationTrialLicenseCount} license(s) - {RegistrationTrialDays} days"
            });

            // Each licence lives by its own ExpiryDate from here on.
            var licenses = new List<CompanyLicense>(RegistrationTrialLicenseCount);
            for (int i = 1; i <= RegistrationTrialLicenseCount; i++)
            {
                licenses.Add(new CompanyLicense
                {
                    ProfileId = profileId,
                    LicenseKey = $"CLD-TRI{i:000}-{profileId}",
                    LicenseType = "Trial",
                    CompanySubscriptionId = subscription.Id,
                    PlanId = plan.Id,
                    ExpiryDate = subscription.CurrentPeriodEnd,
                    // The registering Profile Admin is also a Team Manager and
                    // holds the trial licence: 1 total, 1 consumed, 0 available.
                    AssignedUserId = i == 1 ? userId : null,
                    AssignedUserAt = i == 1 ? now : null,
                    CreatedBy = userId,
                    UpdatedBy = userId,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
            _context.CompanyLicenses.AddRange(licenses);
            await _context.SaveChangesAsync();

            // One INSERT audit row per minted licence, VersionNumber 1.
            _context.CompanyLicenseAuditLogs.AddRange(licenses.Select(l => new CompanyLicenseAuditLog
            {
                Id = l.Id,
                ProfileId = l.ProfileId,
                LicenseKey = l.LicenseKey,
                LicenseType = l.LicenseType,
                CompanySubscriptionId = l.CompanySubscriptionId,
                PlanId = l.PlanId,
                ExpiryDate = l.ExpiryDate,
                CreatedBy = l.CreatedBy,
                UpdatedBy = l.UpdatedBy,
                CreatedAt = l.CreatedAt,
                UpdatedAt = l.UpdatedAt,
                IsDel = l.IsDel,
                LoggedAt = now,
                VersionNumber = 1,
                OperationType = "INSERT"
            }));
            await _context.SaveChangesAsync();

            return subscription;
        }
    }
}
