using CloverBackoffice.Components.Enums;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Components;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static CloverBackoffice.Components.BussinessManager;
using CloverBackoffice.Repository.Provider;
using Microsoft.Extensions.Configuration;
using CloverBackoffice.Repository.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Repository.Implementation
{
    public class CloveraCompanyAdminRepository : ICloveraCompanyAdminRepository
    {
        private readonly ApplicationDbContext _context;
        private readonly JwtTokenGenerator _tokenGenerator;
        public CloveraCompanyAdminRepository(ApplicationDbContext context, JwtTokenGenerator tokenGenerator)
        {
            _context = context;
            _tokenGenerator = tokenGenerator;

        }

        public async Task<Profile?> GetByIdAsync(int profileId)
        {
            return await _context.Profiles.FindAsync((uint)profileId);
        }

        public async Task UpdateAsync(Profile company)
        {
            try
            {
                var existingCompany = await _context.Profiles
                                        .FirstOrDefaultAsync(c => c.Id == company.Id);
                if (existingCompany != null)
                {
                    existingCompany.IsPartner = company.IsPartner;

                    // Only mark the IsPartner property as modified, not the entire entity
                    // This prevents EF Core from trying to update DateTime properties with Unspecified Kind
                    _context.Entry(existingCompany).Property(c => c.IsPartner).IsModified = true;
                    await _context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {

                throw;
            }

            
        }
        public async Task ClearPartnerReferencesAsync(int partnerId)
        {
            var companies = await _context.Profiles
                .Where(c => c.PartnerId == (uint)partnerId || c.ReferredBy == partnerId)
                .ToListAsync();

            foreach (var company in companies)
            {
                company.PartnerId = null;
                company.ReferredBy = null;
                _context.Entry(company).Property(c => c.PartnerId).IsModified = true;
                _context.Entry(company).Property(c => c.ReferredBy).IsModified = true;
            }

            await _context.SaveChangesAsync();
        }

        public async Task<UserCompanyResponseDto> AddCompanyWithUserAsync(CloveraCompanyCreateDto dto)
        {
            string[] parts = Array.Empty<string>();
            if (!string.IsNullOrEmpty(dto.EmailAddress))
            {
                parts = dto.EmailAddress.Split('@');
            }
            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                //  Step 0: DISABLED — domain-based company reuse.
                //
                // This used to look up an existing Company by EMAIL DOMAIN
                // (parts[1]) and, on a match, skip company creation entirely: it
                // attached the new user + a new BusinessUnit to that OTHER company,
                // overwrote its IsPartner/ReferredBy with the request's values, and
                // returned CompanyExists ("This company has already been
                // registered.") together with the foreign ProfileId.
                //
                // Every consumer-domain signup collided. 94 companies in Dev share
                // DomainName='yopmail.com', so a request for a brand-new company was
                // silently absorbed into whichever one FirstOrDefault happened to
                // return (no OrderBy → not even deterministic). No new company was
                // ever created, and an unrelated company was mutated.
                //
                // /api/company/Create and /CreateTrial already dropped this same
                // reuse logic — see the equivalent commented block in
                // AccountRegistrationRepository.AddCompanyWithUserAsync. This
                // endpoint now matches them: it ALWAYS provisions a fresh Company.
                //
                // Placeholder so the downstream `Company = ... : existingCompany`
                // ternary still compiles.
                Profile? existingCompany = null;

                if (existingCompany != null)
                {
                    // Update partner fields on the existing company
                    existingCompany.IsPartner = dto.IsPartner;
                    existingCompany.ReferredBy = dto.IsPartner ? null : dto.ReferredBy;
                    _context.Entry(existingCompany).Property(c => c.IsPartner).IsModified = true;
                    _context.Entry(existingCompany).Property(c => c.ReferredBy).IsModified = true;
                    await _context.SaveChangesAsync();

                    var existingUser = await _context.Users
                        .FirstOrDefaultAsync(u => u.Username == dto.EmailAddress);

                    if (existingUser == null)
                    {
                        var dailyQuestionSetGroups = await _context.DailyQuestionSetGroups.FirstOrDefaultAsync(g => g.Id == 1);
                        var businessUnitCodesd = BussinessManager.GenerateCodes();
                        var businessUnitd = new BusinessUnit
                        {
                            BusinessUnitCode = businessUnitCodesd.Code,
                            BusinessUnitShortCode = businessUnitCodesd.ShortCode,
                            Name = dto.CompanyName,
                            NameInvariant = dto.CompanyName.ToUpperInvariant() + businessUnitCodesd.Code,
                            RegistrationDate = DateTime.UtcNow,
                            Profile = existingCompany != null && existingCompany.Id > 0 ? existingCompany : existingCompany,
                            ContactFirstName = dto.FirstName,
                            ContactLastName = dto.LastName,
                            ContactEmailAddress = dto.EmailAddress,
                            ActiveDailyQuestionSetGroupId = dailyQuestionSetGroups.Id,
                            ActiveDailyQuestionSetGroup = dailyQuestionSetGroups

                        };

                        _context.BusinessUnits.Add(businessUnitd);
                        await _context.SaveChangesAsync();
                        existingUser = new User
                        {
                            Username = dto.EmailAddress,
                            Name = !string.IsNullOrWhiteSpace(dto.FirstName) || !string.IsNullOrWhiteSpace(dto.LastName) ? $"{dto.FirstName} {dto.LastName}".Trim() : parts[0],
                            Password = PasswordUtility.Encrypt("12345678"),
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
                            Type = UserType.Unit.ToString(),
                            ProfileId = (int)existingCompany.Id
                        };

                        _context.Users.Add(existingUser);
                        await _context.SaveChangesAsync();

                        var companyLinks = new CompanyLink
                        {
                            ProfileId = (int)existingCompany.Id,
                            UserId = existingUser.UserId,
                            ParentId = 0,
                            UnitId = 0,
                            ExpiryDate = string.Empty,
                            BussinessUnitId = (int)businessUnitd.Id
                        };
                        _context.CompanyLinks.Add(companyLinks);
                        await _context.SaveChangesAsync();
                        //Token saved in AuthToken Table
                        var token = Guid.NewGuid().ToString();
                        var expiryDate = DateTime.UtcNow.AddDays(3);

                        var authToken = new UserToken
                        {
                            UserId = existingUser.UserId,
                            Token = token,
                            ExpiryDate = expiryDate
                        };
                        _context.UserTokens.Add(authToken);
                        await _context.SaveChangesAsync();
                    }
                    await transaction.CommitAsync();
                    return new UserCompanyResponseDto
                    {
                        UserId = existingUser?.UserId ?? 0,
                        Email = existingUser?.Username ?? dto.EmailAddress,
                        ProfileId = (int)existingCompany.Id,
                        Message = ResponseMessages.Errors.CompanyExists
                    };
                }



                // Step 1: Create Profile, contact included as inline columns.
                var companyUnitCodes = BussinessManager.GenerateCodes();
                var company = new Profile
                {
                    DomainName = parts[1],
                    ProfileName = dto.CompanyName,
                    ProfileShortCode = companyUnitCodes.ShortCode,
                    RegistrationDate = DateTime.UtcNow,
                    ContactFirstName = dto.FirstName,
                    ContactLastName = dto.LastName,
                    ContactEmailAddress = dto.EmailAddress,
                    IsPartner = dto.IsPartner,
                    ReferredBy = dto.IsPartner ? null : dto.ReferredBy

                };
                _context.Profiles.Add(company);
                await _context.SaveChangesAsync();

                // No default BusinessUnit. This used to create one named after the
                // company, so the Profile Admin this endpoint provisions logged in
                // already owning a team they never asked for — the same thing trial
                // registration used to do. Their first team is made when they go
                // through Team Setup or the Teams screen themselves.

                // Step 4: Create User (if not exists)
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == dto.EmailAddress);
                if (user == null)
                {
                    user = new User
                    {
                        Username = dto.EmailAddress,
                        Name = !string.IsNullOrWhiteSpace(dto.FirstName) || !string.IsNullOrWhiteSpace(dto.LastName) ? $"{dto.FirstName} {dto.LastName}".Trim() : parts[0],
                        Password = PasswordUtility.Encrypt("12345678"),
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
                        Type = UserType.Unit.ToString(),
                        ProfileId = (int)company.Id
                    };
                    _context.Users.Add(user);
                    await _context.SaveChangesAsync();


                    //Token saved in AuthToken Table
                    var token = Guid.NewGuid().ToString();
                    var expiryDate = DateTime.UtcNow.AddDays(3);

                    var authToken = new UserToken
                    {
                        UserId = user.UserId,
                        Token = token,
                        ExpiryDate = expiryDate
                    };
                    _context.UserTokens.Add(authToken);
                    await _context.SaveChangesAsync();
                }
                //here
                // Step 5: Create CompanyLink
                var companyLink = new CompanyLink
                {
                    ProfileId = (int)company.Id,
                    UserId = user.UserId,
                    ParentId = 0,
                    UnitId = 0,
                    ExpiryDate = string.Empty,

                    // No team yet, so the link points at none — login then reports
                    // an empty CompanyUnit rather than an auto-created team.
                    BussinessUnitId = null
                };
                _context.CompanyLinks.Add(companyLink);
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();

                return new UserCompanyResponseDto
                {
                    UserId = user.UserId,
                    Email = user.Username,
                    ProfileId = (int)company.Id,
                    Message = ResponseMessages.Success.RegistrationSuccess

                };
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                throw new Exception(ResponseMessages.Errors.CompanyCreationFailedMessage, ex);
            }
        }
    }
}
