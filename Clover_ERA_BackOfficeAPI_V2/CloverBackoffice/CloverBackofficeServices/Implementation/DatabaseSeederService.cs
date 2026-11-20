using CloverBackoffice.Components;
using CloverBackoffice.Components.Enums;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Provider;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using static CloverBackoffice.Components.BussinessManager;

namespace CloverBackoffice.Services.Implementation
{
    /// <summary>
    /// Creates the RRIDA Admin (super admin) accounts on startup.
    ///
    /// This is the ONLY thing in the system that mints a
    /// <see cref="UserGroups.RridaAdmin"/>. The role is refused by
    /// <c>api/profile/upsert-user</c> and hidden from the role picker, so a Profile
    /// Admin cannot promote anyone — including themselves — to platform admin.
    ///
    /// Runs on every start and is idempotent: an account that already exists is
    /// left alone apart from its role, which is re-asserted so an account that was
    /// demoted (by hand, or by an earlier version of this seeder) comes back as a
    /// RRIDA Admin. Passwords are set ONLY on creation — re-running never resets a
    /// password somebody has since changed.
    /// </summary>
    public class DatabaseSeeder
    {
        private readonly ApplicationDbContext _context;

        // The RRIDA Admins own no tenant, but a User row still needs a ProfileId and
        // the login response still returns a company, so they are hung off one
        // profile of their own. It is not a customer profile and nothing is expected
        // to be created under it.
        private const string PlatformDomain = "rrida.com";
        private const string PlatformProfileName = "RRIDA";

        // Initial password for a newly seeded account only. Changing it here does
        // NOT change the password of an account that already exists.
        private const string InitialPassword = "Welcome@1";

        private static readonly (string Email, string FirstName, string LastName)[] SuperAdmins =
        {
            ("nawab.anjum@rrida.com",   "Nawab", "Anjum"),
            ("clive.hays@rrida.com",    "Clive", "Hays"),
            ("neil.hays@rrida.com",     "Neil",  "Hays"),
            ("chad.williams@rrida.com", "Chad",  "Williams")
        };

        public DatabaseSeeder(ApplicationDbContext context)
        {
            _context = context;
        }

        // ---------------- SUPERADMIN SEEDING ---------------- //
        public async Task SeedSuperAdminsAsync()
        {
            var profile = await EnsurePlatformProfileAsync();

            foreach (var (email, firstName, lastName) in SuperAdmins)
            {
                var existing = await _context.Users
                    .FirstOrDefaultAsync(u => u.Username.ToLower() == email.ToLower());

                if (existing != null)
                {
                    // Re-assert the role only. Everything else — password, name,
                    // status — belongs to whoever has been using the account.
                    if (existing.UserGroupId != (int)UserGroups.RridaAdmin)
                    {
                        existing.UserGroupId = (int)UserGroups.RridaAdmin;
                        existing.ModifiedDate = DateTime.UtcNow.ToString("o");
                        await _context.SaveChangesAsync();
                    }
                    continue;
                }

                var user = new User
                {
                    Username = email,
                    Name = $"{firstName} {lastName}",
                    Password = PasswordUtility.Encrypt(InitialPassword),
                    CreatedDate = DateTime.UtcNow.ToString("o"),
                    ModifiedDate = DateTime.UtcNow.ToString("o"),
                    IsActive = true,
                    IsDel = false,
                    UserGroupId = (int)UserGroups.RridaAdmin,
                    LastLoginDate = null,

                    // Seeded accounts skip email verification: there is no inbox to
                    // click a link in, and they must work on a fresh database.
                    IsVerified = true,
                    Status = ((int)UserStatus.Active).ToString(),
                    CreatedBy = CreatedByType.System.ToString(),
                    TenantId = "",
                    Type = UserType.ProfileAdmin.ToString(),
                    ProfileId = (int)profile.Id
                };
                _context.Users.Add(user);
                await _context.SaveChangesAsync();

                var businessUnitId = await _context.BusinessUnits
                    .Where(b => b.Profile.Id == profile.Id)
                    .Select(b => b.Id)
                    .FirstOrDefaultAsync();

                _context.CompanyLinks.Add(new CompanyLink
                {
                    ProfileId = (int)profile.Id,
                    UserId = user.UserId,
                    ParentId = 0,
                    UnitId = 0,
                    ExpiryDate = string.Empty,
                    BussinessUnitId = (int)businessUnitId,
                    CreatedBy = "System"
                });
                await _context.SaveChangesAsync();
            }
        }

        private async Task<Profile> EnsurePlatformProfileAsync()
        {
            var profile = await _context.Profiles
                .FirstOrDefaultAsync(c => c.DomainName.ToLower() == PlatformDomain);

            if (profile != null)
                return profile;

            var profileCodes = BussinessManager.GenerateCodes();

            profile = new Profile
            {
                ProfileShortCode = profileCodes.ShortCode,
                DomainName = PlatformDomain,
                ProfileName = PlatformProfileName,
                RegistrationDate = DateTime.UtcNow,
                ContactFirstName = "Clive",
                ContactLastName = "Hays",
                ContactEmailAddress = "clive.hays@rrida.com"
            };
            _context.Profiles.Add(profile);
            await _context.SaveChangesAsync();

            // A profile with no business unit breaks several screens that assume at
            // least one, so the platform profile gets a placeholder like any other.
            var buCodes = BussinessManager.GenerateCodes();
            var dailyQuestionSetGroup = await _context.DailyQuestionSetGroups
                .FirstOrDefaultAsync(g => g.Id == 1);

            _context.BusinessUnits.Add(new BusinessUnit
            {
                BusinessUnitCode = buCodes.Code,
                BusinessUnitShortCode = buCodes.ShortCode,
                Name = "RRIDA",
                NameInvariant = "RRIDA_" + buCodes.Code,
                RegistrationDate = DateTime.UtcNow,
                Profile = profile,
                ContactFirstName = "Clive",
                ContactLastName = "Hays",
                ContactEmailAddress = "clive.hays@rrida.com",
                ActiveDailyQuestionSetGroup = dailyQuestionSetGroup,
                ActiveDailyQuestionSetGroupId = dailyQuestionSetGroup?.Id ?? 1,
                ParentId = null,
                IsDel = false
            });
            await _context.SaveChangesAsync();

            return profile;
        }
    }
}
