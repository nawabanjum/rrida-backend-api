using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Implementation;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Implementation;
using CloverBackoffice.Services.Interfaces;
using Moq;
using Xunit;
using static CloverBackoffice.Tests.TeamHealth.MilestoneTestData;
using ProfileEntity = CloverBackoffice.Infrastructure.Models.Profile;

namespace CloverBackoffice.Tests.Subscription
{
    /// <summary>
    /// RRIDA Admin (Super Admin) team limit extension, POST
    /// /api/license-assignments/extend-team-limit: profile level raises every licence
    /// of that profile, licence level raises only that manager's licence; the team
    /// creation / assignment checks use the new limit and every call is audited.
    /// </summary>
    public class TeamLimitExtensionTests
    {
        private const uint ExtraTeam1 = 701;
        private const uint ExtraTeam2 = 702;
        private const uint ExtraTeam3 = 703;

        private readonly string _db = Guid.NewGuid().ToString();
        private readonly int _licenceOfTeamManager;   // held by ManagerOfTeam (300), on team 294
        private readonly int _licenceOfParentManager; // held by ManagerOfParent (301)

        public TeamLimitExtensionTests()
        {
            using var db = CreateContext(_db);
            Seed(db);
            foreach (var id in new[] { ProfileA, ProfileB })
                db.Profiles.Add(new ProfileEntity
                {
                    Id = id,
                    ProfileShortCode = $"P{id}",
                    DomainName = $"p{id}.test",
                    ProfileName = $"Profile {id}",
                    RegistrationDate = DateTime.UtcNow
                });
            foreach (var id in new[] { ExtraTeam1, ExtraTeam2, ExtraTeam3 })
                db.BusinessUnits.Add(new BusinessUnit
                {
                    Id = id,
                    BusinessUnitCode = $"BU{id}",
                    BusinessUnitShortCode = $"S{id}",
                    Name = $"Team {id}",
                    NameInvariant = $"team {id}",
                    RegistrationDate = DateTime.UtcNow,
                    ProfileId = ProfileA,
                    ActiveDailyQuestionSetGroupId = 1,
                    ActiveDailyQuestionSetGroup = null!
                });
            db.SaveChanges();

            _licenceOfTeamManager = AddLicence(db, ProfileA, ManagerOfTeam);
            _licenceOfParentManager = AddLicence(db, ProfileA, ManagerOfParent);
            AddLicence(db, ProfileB, null);

            db.AssignedCompanyLicenses.Add(new AssignedCompanyLicense { CompanyLicenseId = _licenceOfTeamManager, BusinessUnitId = Team });
            db.SaveChanges();
        }

        private static int AddLicence(ApplicationDbContext db, uint profileId, int? holder)
        {
            var licence = new CompanyLicense
            {
                ProfileId = profileId,
                LicenseKey = Guid.NewGuid().ToString(),
                LicenseType = "Trial",
                ExpiryDate = DateTime.UtcNow.AddDays(30),
                AssignedUserId = holder
            };
            db.CompanyLicenses.Add(licence);
            db.SaveChanges();
            return licence.Id;
        }

        private sealed class NoLockLicenseService : CompanyLicenseService
        {
            public NoLockLicenseService(ApplicationDbContext db)
                : base(new CompanyLicenseRepository(db), db, new Mock<ILicensedPeriodService>().Object) { }

            protected override Task LockProfileLicensesAsync(uint profileId) => Task.CompletedTask;
        }

        private async Task<TeamLimitExtensionResultDto> Extend(ExtendTeamLimitRequestDto dto, int actor = RridaAdmin)
        {
            using var db = CreateContext(_db);
            return await new NoLockLicenseService(db).ExtendTeamLimitAsync(dto, actor);
        }

        private async Task<string> ExtendFails(ExtendTeamLimitRequestDto dto)
            => (await Assert.ThrowsAsync<InvalidOperationException>(() => Extend(dto))).Message;

        private async Task<T> Read<T>(Func<ApplicationDbContext, Task<T>> read)
        {
            using var db = CreateContext(_db);
            return await read(db);
        }

        private Task<int> ManagerLimit(int userId) => Read(db => db.ManagerTeamLimitAsync(userId, limits: null));
        private Task<string?> ManagerLimitError(int userId) => Read(db => db.ManagerTeamLimitErrorAsync(userId));
        private Task<LicenseUsage> Usage(uint profileId) => Read(db => new CompanyLicenseRepository(db).GetLicenseUsageAsync(profileId));
        private Task<int> Extensions() => Read(db => Task.FromResult(db.TeamLimitExtensions.Count()));

        // ManagerOfTeam (300) already has 294; give them more teams.
        private void GiveManagerOfTeam(params uint[] units)
        {
            using var db = CreateContext(_db);
            var nextId = db.UserTeams.Max(ut => ut.Id) + 1;
            foreach (var unit in units)
                db.UserTeams.Add(new UserTeam { Id = nextId++, UserId = ManagerOfTeam, UnitId = unit, CreatedDate = DateTime.UtcNow });
            db.SaveChanges();
        }

        // ---------- who may extend ----------

        [Fact]
        public async Task Only_a_rrida_admin_can_extend()
        {
            var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                Extend(new ExtendTeamLimitRequestDto { ProfileId = ProfileA, AdditionalTeams = 2 }, actor: ProfileAdminA));

            Assert.Equal("Only a RRIDA Admin can extend team limits.", ex.Message);
            Assert.Equal(0, await Extensions());
            Assert.Equal(3, await ManagerLimit(ManagerOfTeam));
        }

        // ---------- profile level ----------

        [Fact]
        public async Task Profile_extension_raises_every_licence_of_that_profile_only()
        {
            var result = await Extend(new ExtendTeamLimitRequestDto { ProfileId = ProfileA, AdditionalTeams = 2, Notes = " Pilot " });

            Assert.Equal("PROFILE", result.Scope);
            Assert.Equal(3, result.PreviousLimit);
            Assert.Equal(5, result.NewLimit);
            Assert.Equal(5, result.TeamsPerLicense);
            Assert.Equal(10, result.MaxTeams);             // 2 live licences x 5
            Assert.Equal("Pilot", result.Notes);
            Assert.Null(result.CompanyLicenseId);
            Assert.All(result.Licenses.Where(r => r.CompanyLicenseId.HasValue), r => Assert.Equal(5, r.TeamsLimit));

            Assert.Equal(5, await ManagerLimit(ManagerOfTeam));
            Assert.Equal(5, await ManagerLimit(ManagerOfParent));

            // Other profile untouched.
            var other = await Usage(ProfileB);
            Assert.Equal(3, other.TeamsPerLicense);
            Assert.Equal(3, other.MaxTeams);
        }

        [Fact]
        public async Task Profile_extension_lets_a_full_manager_take_more_teams()
        {
            GiveManagerOfTeam(ExtraTeam1, ExtraTeam2);    // 3 of 3
            Assert.NotNull(await ManagerLimitError(ManagerOfTeam));

            await Extend(new ExtendTeamLimitRequestDto { ProfileId = ProfileA, AdditionalTeams = 1 });

            Assert.Null(await ManagerLimitError(ManagerOfTeam));
            GiveManagerOfTeam(ExtraTeam3);                // 4 of 4
            var error = await ManagerLimitError(ManagerOfTeam);
            Assert.NotNull(error);
            Assert.Contains("4 of 4", error);
        }

        // ---------- licence level ----------

        [Fact]
        public async Task Manager_extension_raises_only_that_managers_licence()
        {
            var result = await Extend(new ExtendTeamLimitRequestDto { ProfileId = ProfileA, ManagerId = ManagerOfTeam, AdditionalTeams = 1 });

            Assert.Equal("LICENSE", result.Scope);
            Assert.Equal(_licenceOfTeamManager, result.CompanyLicenseId);
            Assert.Equal(ManagerOfTeam, result.ManagerId);
            Assert.Equal(3, result.PreviousLimit);
            Assert.Equal(4, result.NewLimit);
            Assert.Equal(3, result.TeamsPerLicense);       // the profile default is unchanged
            Assert.Equal(7, result.MaxTeams);              // 4 + 3
            var row = Assert.Single(result.Licenses);
            Assert.Equal(ManagerOfTeam, row.ManagerId);
            Assert.Equal(4, row.TeamsLimit);

            Assert.Equal(4, await ManagerLimit(ManagerOfTeam));
            Assert.Equal(3, await ManagerLimit(ManagerOfParent));
        }

        [Fact]
        public async Task Licence_id_alone_finds_its_manager()
        {
            var result = await Extend(new ExtendTeamLimitRequestDto { ProfileId = ProfileA, CompanyLicenseId = _licenceOfParentManager, AdditionalTeams = 2 });

            Assert.Equal(ManagerOfParent, result.ManagerId);
            Assert.Equal(5, result.NewLimit);
            Assert.Equal(5, await ManagerLimit(ManagerOfParent));
            Assert.Equal(3, await ManagerLimit(ManagerOfTeam));
        }

        [Fact]
        public async Task Extended_licence_takes_more_complimentary_teams()
        {
            GiveManagerOfTeam(ExtraTeam1, ExtraTeam2, ExtraTeam3);
            using (var db = CreateContext(_db))
            {
                foreach (var unit in new[] { ExtraTeam1, ExtraTeam2 })
                    db.ComplimentaryLicenseGrants.Add(new ComplimentaryLicenseGrant
                    {
                        CompanyLicenseId = _licenceOfTeamManager,
                        PrimaryBusinessUnitId = Team,
                        ComplimentaryBusinessUnitId = unit,
                        ManagerUserId = ManagerOfTeam,
                        GrantedAt = DateTime.UtcNow,
                        IsActive = true
                    });
                db.SaveChanges();
            }

            // Licence covers 3 of 3: no free slot for the 4th team.
            Assert.Null(await Read(db => new CompanyLicenseRepository(db).FindPurchasedComplimentarySourceForUnitAsync(ExtraTeam3)));

            await Extend(new ExtendTeamLimitRequestDto { ProfileId = ProfileA, ManagerId = ManagerOfTeam, AdditionalTeams = 1 });

            var source = await Read(db => new CompanyLicenseRepository(db).FindPurchasedComplimentarySourceForUnitAsync(ExtraTeam3));
            Assert.NotNull(source);
            Assert.Equal(_licenceOfTeamManager, source!.CompanyLicenseId);
        }

        [Fact]
        public async Task Licence_details_show_the_new_limit()
        {
            await Extend(new ExtendTeamLimitRequestDto { ProfileId = ProfileA, ManagerId = ManagerOfTeam, AdditionalTeams = 2 });

            using var db = CreateContext(_db);
            var service = new NoLockLicenseService(db);
            var details = await service.GetLicenseDetailsAsync(ProfileA, RridaAdmin);
            Assert.Equal(5, details.Single(r => r.ManagerId == ManagerOfTeam).TeamsLimit);
            Assert.Equal(3, details.Single(r => r.ManagerId == ManagerOfParent).TeamsLimit);

            var usage = await service.GetManagerTeamUsageAsync(ProfileA, ManagerOfTeam, RridaAdmin);
            Assert.Equal(5, usage.Single().MaxTeams);
            Assert.Equal("1 of 5", usage.Single().TeamUsage);

            var licences = await service.GetByCompanyAsync(ProfileA);
            Assert.Equal(5, licences.Single(l => l.Id == _licenceOfTeamManager).MaxTeams);
            Assert.Equal(3, licences.Single(l => l.Id == _licenceOfParentManager).MaxTeams);
        }

        // ---------- audit + stacking ----------

        [Fact]
        public async Task Extensions_stack_and_are_audited()
        {
            await Extend(new ExtendTeamLimitRequestDto { ProfileId = ProfileA, AdditionalTeams = 1 });
            var second = await Extend(new ExtendTeamLimitRequestDto { ProfileId = ProfileA, ManagerId = ManagerOfTeam, AdditionalTeams = 2, Notes = "Big team" });

            Assert.Equal(4, second.PreviousLimit);         // 3 + profile 1
            Assert.Equal(6, second.NewLimit);
            Assert.Equal(6, await ManagerLimit(ManagerOfTeam));
            Assert.Equal(4, await ManagerLimit(ManagerOfParent));

            var audit = await Read(db => Task.FromResult(db.TeamLimitExtensions.OrderBy(e => e.Id).ToList()));
            Assert.Equal(2, audit.Count);
            Assert.Equal("PROFILE", audit[0].Scope);
            Assert.Null(audit[0].CompanyLicenseId);
            var row = audit[1];
            Assert.Equal(second.ExtensionId, row.Id);
            Assert.Equal(ProfileA, row.ProfileId);
            Assert.Equal("LICENSE", row.Scope);
            Assert.Equal(_licenceOfTeamManager, row.CompanyLicenseId);
            Assert.Equal(ManagerOfTeam, row.ManagerUserId);
            Assert.Equal(2, row.AdditionalTeams);
            Assert.Equal(4, row.PreviousLimit);
            Assert.Equal(6, row.NewLimit);
            Assert.Equal("Big team", row.Notes);
            Assert.Equal(RridaAdmin, row.ExtendedByUserId);
        }

        [Fact]
        public async Task Existing_team_assignments_are_not_changed()
        {
            var before = await Read(db => Task.FromResult((
                db.UserTeams.Count(), db.AssignedCompanyLicenses.Count(), db.ComplimentaryLicenseGrants.Count(),
                db.CompanyLicenses.Select(l => l.AssignedUserId).ToList())));

            await Extend(new ExtendTeamLimitRequestDto { ProfileId = ProfileA, AdditionalTeams = 2 });
            await Extend(new ExtendTeamLimitRequestDto { ProfileId = ProfileA, ManagerId = ManagerOfTeam, AdditionalTeams = 1 });

            var after = await Read(db => Task.FromResult((
                db.UserTeams.Count(), db.AssignedCompanyLicenses.Count(), db.ComplimentaryLicenseGrants.Count(),
                db.CompanyLicenses.Select(l => l.AssignedUserId).ToList())));
            Assert.Equal(before.Item1, after.Item1);
            Assert.Equal(before.Item2, after.Item2);
            Assert.Equal(before.Item3, after.Item3);
            Assert.Equal(before.Item4, after.Item4);
        }

        // ---------- validation ----------

        [Theory]
        [InlineData(null, "AdditionalTeams is required.")]
        [InlineData(0, "AdditionalTeams must be between 1 and 100.")]
        [InlineData(-2, "AdditionalTeams must be between 1 and 100.")]
        [InlineData(101, "AdditionalTeams must be between 1 and 100.")]
        public async Task Additional_teams_out_of_range_is_refused(int? teams, string message)
        {
            Assert.Equal(message, await ExtendFails(new ExtendTeamLimitRequestDto { ProfileId = ProfileA, AdditionalTeams = teams }));
            Assert.Equal(0, await Extensions());
        }

        [Fact]
        public async Task Invalid_targets_are_refused()
        {
            Assert.Equal("ProfileId is required.",
                await ExtendFails(new ExtendTeamLimitRequestDto { AdditionalTeams = 1 }));
            Assert.Equal("Invalid ProfileId. Profile does not exist.",
                await ExtendFails(new ExtendTeamLimitRequestDto { ProfileId = 999999, AdditionalTeams = 1 }));
            Assert.Equal("Manager 99999 not found.",
                await ExtendFails(new ExtendTeamLimitRequestDto { ProfileId = ProfileA, ManagerId = 99999, AdditionalTeams = 1 }));
            Assert.Equal($"Manager {ProfileAdminB} does not belong to profile {ProfileA}.",
                await ExtendFails(new ExtendTeamLimitRequestDto { ProfileId = ProfileA, ManagerId = ProfileAdminB, AdditionalTeams = 1 }));
            Assert.Equal($"User {PortfolioManagerNoReports} is not a Team Manager.",
                await ExtendFails(new ExtendTeamLimitRequestDto { ProfileId = ProfileA, ManagerId = PortfolioManagerNoReports, AdditionalTeams = 1 }));
            Assert.Equal($"Manager {UnassignedManager} holds no live license. Assign a license to them first.",
                await ExtendFails(new ExtendTeamLimitRequestDto { ProfileId = ProfileA, ManagerId = UnassignedManager, AdditionalTeams = 1 }));
            Assert.Equal("License 99999 not found.",
                await ExtendFails(new ExtendTeamLimitRequestDto { ProfileId = ProfileA, CompanyLicenseId = 99999, AdditionalTeams = 1 }));
            Assert.Equal($"License {_licenceOfTeamManager} does not belong to profile {ProfileB}.",
                await ExtendFails(new ExtendTeamLimitRequestDto { ProfileId = ProfileB, CompanyLicenseId = _licenceOfTeamManager, AdditionalTeams = 1 }));
            Assert.Equal($"License {_licenceOfParentManager} is not held by manager {ManagerOfTeam}.",
                await ExtendFails(new ExtendTeamLimitRequestDto { ProfileId = ProfileA, ManagerId = ManagerOfTeam, CompanyLicenseId = _licenceOfParentManager, AdditionalTeams = 1 }));

            Assert.Equal(0, await Extensions());
        }

        [Fact]
        public async Task Expired_licence_is_refused()
        {
            using (var db = CreateContext(_db))
            {
                db.CompanyLicenses.Single(l => l.Id == _licenceOfParentManager).ExpiryDate = DateTime.UtcNow.AddDays(-1);
                db.SaveChanges();
            }

            Assert.Equal($"License {_licenceOfParentManager} has expired.",
                await ExtendFails(new ExtendTeamLimitRequestDto { ProfileId = ProfileA, CompanyLicenseId = _licenceOfParentManager, AdditionalTeams = 1 }));
        }
    }
}
