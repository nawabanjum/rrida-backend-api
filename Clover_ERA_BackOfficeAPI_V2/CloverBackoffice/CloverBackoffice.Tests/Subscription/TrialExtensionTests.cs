using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Implementation;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Implementation;
using CloverBackoffice.Services.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using static CloverBackoffice.Tests.TeamHealth.MilestoneTestData;
using ProfileEntity = CloverBackoffice.Infrastructure.Models.Profile;

namespace CloverBackoffice.Tests.Subscription
{
    /// <summary>
    /// RRIDA Admin (Super Admin) trial extension, POST /api/subscription/apply with
    /// extendByDays: only the requested profile's trial and licences move, the result
    /// carries the new dates, and every extension is audited with the admin's id.
    /// </summary>
    public class TrialExtensionTests
    {
        private const int Admin = RridaAdmin;
        private const uint PaidProfile = 777;

        private readonly string _db = Guid.NewGuid().ToString();
        private readonly DateTime _trialEndA = DateTime.UtcNow.AddDays(5);
        private readonly DateTime _trialEndB = DateTime.UtcNow.AddDays(9);

        public TrialExtensionTests()
        {
            using var db = CreateContext(_db);
            Seed(db);
            foreach (var id in new[] { ProfileA, ProfileB, PaidProfile })
                db.Profiles.Add(new ProfileEntity
                {
                    Id = id,
                    ProfileShortCode = $"P{id}",
                    DomainName = $"p{id}.test",
                    ProfileName = $"Profile {id}",
                    RegistrationDate = DateTime.UtcNow
                });

            AddTrial(db, ProfileA, _trialEndA, licences: 2);
            AddTrial(db, ProfileB, _trialEndB, licences: 1);

            // A fully paid profile: paid subscription, paid licence, nothing on trial.
            var paid = AddTrial(db, PaidProfile, DateTime.UtcNow.AddDays(20), licences: 1);
            paid.Status = SubscriptionStatus.Active;
            paid.StripeSubscriptionId = "sub_paid";
            foreach (var lic in db.CompanyLicenses.Where(l => l.ProfileId == PaidProfile))
                lic.LicenseType = "Paid";
            db.SaveChanges();
        }

        private static CompanySubscription AddTrial(ApplicationDbContext db, uint profileId, DateTime end, int licences)
        {
            var sub = new CompanySubscription
            {
                ProfileId = profileId,
                Status = SubscriptionStatus.Trial,
                TrialStartDate = end.AddDays(-30),
                TrialEndDate = end,
                CurrentPeriodStart = end.AddDays(-30),
                CurrentPeriodEnd = end,
                IsTrialUsed = true
            };
            db.CompanySubscriptions.Add(sub);
            db.SaveChanges();
            for (var i = 0; i < licences; i++)
                db.CompanyLicenses.Add(new CompanyLicense
                {
                    ProfileId = profileId,
                    LicenseKey = Guid.NewGuid().ToString(),
                    LicenseType = "Trial",
                    CompanySubscriptionId = sub.Id,
                    ExpiryDate = end
                });
            db.SaveChanges();
            return sub;
        }

        private sealed class NoLockSubscriptionService : SubscriptionService
        {
            public NoLockSubscriptionService(ApplicationDbContext db)
                : base(
                    new CompanySubscriptionRepository(db, new PaymentAuditLogRepository(db)),
                    new Mock<IPlanRepository>().Object,
                    new Mock<IStripeService>().Object,
                    new Mock<IPromoCodeService>().Object,
                    new Mock<IPromoCodeRepository>().Object,
                    new Mock<IPaymentRepository>().Object,
                    new CompanyLicenseRepository(db),
                    db,
                    Options.Create(new StripeSettings()),
                    NullLogger<SubscriptionService>.Instance,
                    new Mock<ILegacyTrialBackfillService>().Object) { }

            protected override Task LockProfileLicensesAsync(uint profileId) => Task.CompletedTask;
        }

        private async Task<CompanySubscriptionDetailDto> Apply(ApplySubscriptionRequestDto dto)
        {
            using var db = CreateContext(_db);
            var service = new NoLockSubscriptionService(db);
            return await service.ApplySubscriptionAsync(dto, Admin);
        }

        private T Read<T>(Func<ApplicationDbContext, T> read)
        {
            using var db = CreateContext(_db);
            return read(db);
        }

        private CompanySubscription Sub(uint profileId) =>
            Read(db => db.CompanySubscriptions.Single(s => s.ProfileId == profileId));

        private List<DateTime?> LicenceExpiries(uint profileId) =>
            Read(db => db.CompanyLicenses.Where(l => l.ProfileId == profileId).Select(l => l.ExpiryDate).ToList());

        private int AuditRows() => Read(db => db.TrialExtensionAuditLogs.Count());

        [Fact]
        public async Task Extend_by_days_moves_the_trial_end_and_licences_of_that_profile()
        {
            var result = await Apply(new ApplySubscriptionRequestDto { ProfileId = ProfileA, ExtendByDays = 14, Notes = "Sales asked" });

            var expected = _trialEndA.AddDays(14);
            var sub = Sub(ProfileA);
            Assert.Equal(expected, sub.TrialEndDate);
            Assert.Equal(expected, sub.CurrentPeriodEnd);
            Assert.Equal(SubscriptionStatus.Trial, sub.Status);
            Assert.All(LicenceExpiries(ProfileA), e => Assert.Equal(expected, e));

            var ext = result.TrialExtension!;
            Assert.Equal("PROFILE", ext.Scope);
            Assert.Equal(14, ext.DaysAdded);
            Assert.Equal(_trialEndA, ext.PreviousTrialEndDate);
            Assert.Equal(expected, ext.NewTrialEndDate);
            Assert.Equal(expected, ext.EffectiveExpiryDate);
            Assert.Equal(2, ext.LicensesUpdated);
            Assert.Equal(Admin, ext.ExtendedByUserId);
            Assert.Equal(ProfileA, result.ProfileId);
        }

        [Fact]
        public async Task Other_profiles_are_not_touched()
        {
            await Apply(new ApplySubscriptionRequestDto { ProfileId = ProfileA, ExtendByDays = 14 });

            var other = Sub(ProfileB);
            Assert.Equal(_trialEndB, other.TrialEndDate);
            Assert.Equal(_trialEndB, other.CurrentPeriodEnd);
            Assert.All(LicenceExpiries(ProfileB), e => Assert.Equal(_trialEndB, e));
        }

        [Fact]
        public async Task Extension_is_audited_with_the_admin_and_details()
        {
            var result = await Apply(new ApplySubscriptionRequestDto { ProfileId = ProfileA, ExtendByDays = 7, Notes = "  Pilot  " });

            var audit = Read(db => db.TrialExtensionAuditLogs.Single());
            Assert.Equal(result.TrialExtension!.AuditId, audit.AuditId);
            Assert.Equal(ProfileA, audit.ProfileId);
            Assert.Equal(Sub(ProfileA).Id, audit.SubscriptionId);
            Assert.Equal("PROFILE", audit.Scope);
            Assert.Equal(7, audit.DaysAdded);
            Assert.Equal(_trialEndA, audit.PreviousTrialEndDate);
            Assert.Equal(_trialEndA.AddDays(7), audit.NewTrialEndDate);
            Assert.Equal("Pilot", audit.Notes);
            Assert.Equal(Admin, audit.ExtendedByUserId);

            // The row snapshot log still gets its UPDATE row too.
            Assert.True(Read(db => db.CompanySubscriptionAuditLogs.Any(a => a.ProfileId == ProfileA && a.OperationType == "UPDATE")));
        }

        [Fact]
        public async Task Ended_trial_is_extended_from_today()
        {
            using (var db = CreateContext(_db))
            {
                var sub = db.CompanySubscriptions.Single(s => s.ProfileId == ProfileA);
                sub.Status = SubscriptionStatus.Expired;
                sub.TrialEndDate = sub.CurrentPeriodEnd = DateTime.UtcNow.AddDays(-10);
                db.SaveChanges();
            }

            var before = DateTime.UtcNow;
            var result = await Apply(new ApplySubscriptionRequestDto { ProfileId = ProfileA, ExtendByDays = 10 });

            var sub2 = Sub(ProfileA);
            Assert.Equal(SubscriptionStatus.Trial, sub2.Status);
            Assert.InRange(sub2.TrialEndDate!.Value, before.AddDays(10), DateTime.UtcNow.AddDays(10));
            Assert.Equal(sub2.TrialEndDate, result.TrialExtension!.NewTrialEndDate);
        }

        [Fact]
        public async Task Ended_trial_gets_its_licence_and_team_coverage_back()
        {
            // What the expiry job leaves behind: licence soft-deleted, team assignment closed.
            int licenceId;
            using (var db = CreateContext(_db))
            {
                var sub = db.CompanySubscriptions.Single(s => s.ProfileId == ProfileA);
                sub.Status = SubscriptionStatus.Expired;
                sub.TrialEndDate = sub.CurrentPeriodEnd = DateTime.UtcNow.AddDays(-3);
                var lic = db.CompanyLicenses.First(l => l.ProfileId == ProfileA);
                lic.ExpiryDate = sub.TrialEndDate;
                lic.IsDel = true;
                licenceId = lic.Id;
                db.AssignedCompanyLicenses.Add(new AssignedCompanyLicense
                {
                    CompanyLicenseId = lic.Id,
                    BusinessUnitId = Team,
                    AssignedAt = DateTime.UtcNow.AddDays(-30),
                    IsActive = false,
                    UnassignedAt = sub.TrialEndDate
                });
                db.SaveChanges();
            }

            await Apply(new ApplySubscriptionRequestDto { ProfileId = ProfileA, ExtendByDays = 14 });

            var licence = Read(db => db.CompanyLicenses.Single(l => l.Id == licenceId));
            Assert.False(licence.IsDel);
            Assert.True(licence.ExpiryDate > DateTime.UtcNow.AddDays(13));

            var assignment = Read(db => db.AssignedCompanyLicenses.Single(a => a.CompanyLicenseId == licenceId));
            Assert.True(assignment.IsActive);
            Assert.Null(assignment.UnassignedAt);
        }

        [Fact]
        public async Task Trial_ended_by_a_move_to_a_purchased_licence_is_not_revived()
        {
            // What the trial -> paid conversion leaves behind: licence expired now,
            // terminated, team assignment closed.
            int terminatedId, otherId;
            using (var db = CreateContext(_db))
            {
                var licences = db.CompanyLicenses.Where(l => l.ProfileId == ProfileA).OrderBy(l => l.Id).ToList();
                var lic = licences[0];
                lic.ExpiryDate = DateTime.UtcNow.AddMinutes(-1);
                lic.IsDel = true;
                lic.TerminatedAt = lic.ExpiryDate;
                lic.ReplacedByLicenseId = 999;
                terminatedId = lic.Id;
                otherId = licences[1].Id;
                db.AssignedCompanyLicenses.Add(new AssignedCompanyLicense
                {
                    CompanyLicenseId = lic.Id,
                    BusinessUnitId = Team,
                    AssignedAt = DateTime.UtcNow.AddDays(-10),
                    IsActive = false,
                    UnassignedAt = lic.ExpiryDate
                });
                db.SaveChanges();
            }

            await Apply(new ApplySubscriptionRequestDto { ProfileId = ProfileA, ExtendByDays = 14 });

            var terminated = Read(db => db.CompanyLicenses.Single(l => l.Id == terminatedId));
            Assert.True(terminated.IsDel);
            Assert.True(terminated.ExpiryDate < DateTime.UtcNow);
            Assert.False(Read(db => db.AssignedCompanyLicenses.Single(a => a.CompanyLicenseId == terminatedId).IsActive));

            // The profile's other trial licence is still extended.
            Assert.True(Read(db => db.CompanyLicenses.Single(l => l.Id == otherId).ExpiryDate) > DateTime.UtcNow.AddDays(13));
        }

        [Fact]
        public async Task Profile_with_nothing_on_trial_is_refused_and_nothing_changes()
        {
            var paidExpiry = LicenceExpiries(PaidProfile).Single();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Apply(new ApplySubscriptionRequestDto { ProfileId = PaidProfile, ExtendByDays = 14 }));

            Assert.Equal("This profile has no trial license to extend.", ex.Message);
            Assert.Equal(SubscriptionStatus.Active, Sub(PaidProfile).Status);
            Assert.Equal(paidExpiry, LicenceExpiries(PaidProfile).Single());
            Assert.Equal(0, AuditRows());
        }

        // ---------- profile with Team Managers on trial AND on paid licences ----------

        // What a purchase during the trial leaves behind: the trial subscription row
        // turned Active (Stripe id set); trial licence X still on it, paid licence Y next to it.
        private (int TrialId, int PaidId, DateTime TrialEnd, DateTime PaidEnd) AddTrialLicenceToPaidProfile(int? trialHolder = null)
        {
            using var db = CreateContext(_db);
            var sub = db.CompanySubscriptions.Single(s => s.ProfileId == PaidProfile);
            var paid = db.CompanyLicenses.Single(l => l.ProfileId == PaidProfile);
            var trialEnd = DateTime.UtcNow.AddDays(3);
            var trial = new CompanyLicense
            {
                ProfileId = PaidProfile,
                LicenseKey = Guid.NewGuid().ToString(),
                LicenseType = "Trial",
                CompanySubscriptionId = sub.Id,
                ExpiryDate = trialEnd,
                AssignedUserId = trialHolder
            };
            db.CompanyLicenses.Add(trial);
            db.SaveChanges();
            return (trial.Id, paid.Id, trialEnd, paid.ExpiryDate!.Value);
        }

        [Fact]
        public async Task Trial_licence_next_to_paid_licences_is_extended_and_paid_side_is_untouched()
        {
            var (trialId, paidId, trialEnd, paidEnd) = AddTrialLicenceToPaidProfile();
            var subBefore = Sub(PaidProfile);

            var result = await Apply(new ApplySubscriptionRequestDto { ProfileId = PaidProfile, ExtendByDays = 14 });

            Assert.Equal(trialEnd.AddDays(14), Read(db => db.CompanyLicenses.Single(l => l.Id == trialId).ExpiryDate));
            Assert.Equal(paidEnd, Read(db => db.CompanyLicenses.Single(l => l.Id == paidId).ExpiryDate));

            // Paid subscription keeps its status, period and Stripe link.
            var subAfter = Sub(PaidProfile);
            Assert.Equal(SubscriptionStatus.Active, subAfter.Status);
            Assert.Equal("sub_paid", subAfter.StripeSubscriptionId);
            Assert.Equal(subBefore.CurrentPeriodEnd, subAfter.CurrentPeriodEnd);
            Assert.Equal(subBefore.TrialEndDate, subAfter.TrialEndDate);
            Assert.Equal(subBefore.GracePeriod, subAfter.GracePeriod);

            var ext = result.TrialExtension!;
            Assert.Equal(trialEnd, ext.PreviousTrialEndDate);
            Assert.Equal(trialEnd.AddDays(14), ext.NewTrialEndDate);
            Assert.Equal(1, ext.LicensesUpdated);
            Assert.Equal(1, AuditRows());
        }

        [Fact]
        public async Task Expired_trial_licence_next_to_paid_licences_is_extended_from_today()
        {
            var (trialId, paidId, _, paidEnd) = AddTrialLicenceToPaidProfile();
            using (var db = CreateContext(_db))
            {
                var lic = db.CompanyLicenses.Single(l => l.Id == trialId);
                lic.ExpiryDate = DateTime.UtcNow.AddDays(-2);
                lic.IsDel = true;
                db.SaveChanges();
            }

            await Apply(new ApplySubscriptionRequestDto { ProfileId = PaidProfile, ExtendByDays = 10 });

            var trial = Read(db => db.CompanyLicenses.Single(l => l.Id == trialId));
            Assert.False(trial.IsDel);
            Assert.True(trial.ExpiryDate > DateTime.UtcNow.AddDays(9));
            Assert.Equal(paidEnd, Read(db => db.CompanyLicenses.Single(l => l.Id == paidId).ExpiryDate));
        }

        [Fact]
        public async Task Separate_trial_and_paid_subscriptions_only_the_trial_side_moves()
        {
            // ProfileA keeps its trial subscription (2 trial licences) and also buys
            // a paid subscription with a paid licence.
            int paidSubId, paidLicId;
            DateTime paidEnd = DateTime.UtcNow.AddDays(25);
            using (var db = CreateContext(_db))
            {
                var paidSub = new CompanySubscription
                {
                    ProfileId = ProfileA,
                    Status = SubscriptionStatus.Active,
                    StripeSubscriptionId = "sub_a_paid",
                    CurrentPeriodStart = DateTime.UtcNow.AddDays(-5),
                    CurrentPeriodEnd = paidEnd,
                    IsTrialUsed = true
                };
                db.CompanySubscriptions.Add(paidSub);
                db.SaveChanges();
                var paidLic = new CompanyLicense
                {
                    ProfileId = ProfileA,
                    LicenseKey = Guid.NewGuid().ToString(),
                    LicenseType = "Paid",
                    CompanySubscriptionId = paidSub.Id,
                    ExpiryDate = paidEnd
                };
                db.CompanyLicenses.Add(paidLic);
                db.SaveChanges();
                (paidSubId, paidLicId) = (paidSub.Id, paidLic.Id);
            }

            var result = await Apply(new ApplySubscriptionRequestDto { ProfileId = ProfileA, ExtendByDays = 14 });

            var expected = _trialEndA.AddDays(14);
            var trialSub = Read(db => db.CompanySubscriptions.Single(s => s.ProfileId == ProfileA && s.Id != paidSubId));
            Assert.Equal(expected, trialSub.TrialEndDate);
            Assert.Equal(SubscriptionStatus.Trial, trialSub.Status);
            Assert.All(Read(db => db.CompanyLicenses.Where(l => l.ProfileId == ProfileA && l.LicenseType == "Trial").Select(l => l.ExpiryDate).ToList()),
                e => Assert.Equal(expected, e));

            var paidAfter = Read(db => db.CompanySubscriptions.Single(s => s.Id == paidSubId));
            Assert.Equal(SubscriptionStatus.Active, paidAfter.Status);
            Assert.Equal(paidEnd, paidAfter.CurrentPeriodEnd);
            Assert.Equal(paidEnd, Read(db => db.CompanyLicenses.Single(l => l.Id == paidLicId).ExpiryDate));
            Assert.Equal(2, result.TrialExtension!.LicensesUpdated);
        }

        [Fact]
        public async Task Licence_details_show_the_new_date_for_the_trial_manager_only()
        {
            // ProfileA: manager 300 on the trial licence, manager 301 on a paid one.
            int paidLicId;
            DateTime paidEnd = DateTime.UtcNow.AddDays(25);
            using (var db = CreateContext(_db))
            {
                db.CompanyLicenses.Where(l => l.ProfileId == ProfileA).OrderBy(l => l.Id).First().AssignedUserId = ManagerOfTeam;
                var paidSub = new CompanySubscription
                {
                    ProfileId = ProfileA,
                    Status = SubscriptionStatus.Active,
                    StripeSubscriptionId = "sub_a_paid",
                    CurrentPeriodEnd = paidEnd,
                    IsTrialUsed = true
                };
                db.CompanySubscriptions.Add(paidSub);
                db.SaveChanges();
                var paidLic = new CompanyLicense
                {
                    ProfileId = ProfileA,
                    LicenseKey = Guid.NewGuid().ToString(),
                    LicenseType = "Paid",
                    CompanySubscriptionId = paidSub.Id,
                    ExpiryDate = paidEnd,
                    AssignedUserId = ManagerOfParent
                };
                db.CompanyLicenses.Add(paidLic);
                db.SaveChanges();
                paidLicId = paidLic.Id;
            }

            await Apply(new ApplySubscriptionRequestDto { ProfileId = ProfileA, ExtendByDays = 14 });

            using var read = CreateContext(_db);
            var rows = await new CompanyLicenseService(new CompanyLicenseRepository(read), read, new Mock<ILicensedPeriodService>().Object)
                .GetLicenseDetailsAsync(ProfileA, Admin);
            var trialRow = Assert.Single(rows, r => r.ManagerId == ManagerOfTeam);
            Assert.Equal("Trial", trialRow.Plan);
            Assert.Equal(_trialEndA.AddDays(14).ToString("yyyy-MM-dd"), trialRow.RenewalDate);
            var paidRow = Assert.Single(rows, r => r.ManagerId == ManagerOfParent);
            Assert.Equal(paidLicId, paidRow.CompanyLicenseId);
            Assert.Equal(paidEnd.ToString("yyyy-MM-dd"), paidRow.RenewalDate);
        }

        [Fact]
        public async Task Days_and_grace_on_a_trial_licence_of_a_paid_subscription_go_onto_that_licence_only()
        {
            // Profile 164 on Dev: extendByDays 2 + gracePeriod 1.
            var (trialId, paidId, trialEnd, paidEnd) = AddTrialLicenceToPaidProfile();

            var result = await Apply(new ApplySubscriptionRequestDto { ProfileId = PaidProfile, ExtendByDays = 2, GracePeriod = 1 });

            Assert.Equal(trialEnd.AddDays(3), Read(db => db.CompanyLicenses.Single(l => l.Id == trialId).ExpiryDate));
            Assert.Equal(paidEnd, Read(db => db.CompanyLicenses.Single(l => l.Id == paidId).ExpiryDate));
            Assert.Null(Sub(PaidProfile).GracePeriod); // grace on the paid subscription would stretch the paid licences

            var ext = result.TrialExtension!;
            Assert.Equal(trialEnd.AddDays(2), ext.NewTrialEndDate);
            Assert.Equal(1, ext.GracePeriod);
            Assert.Equal(trialEnd.AddDays(3), ext.EffectiveExpiryDate);
            Assert.Equal(1, ext.LicensesUpdated);
        }

        [Fact]
        public async Task Grace_only_on_a_trial_licence_of_a_paid_subscription_adds_to_its_own_end()
        {
            var (trialId, paidId, trialEnd, paidEnd) = AddTrialLicenceToPaidProfile();

            await Apply(new ApplySubscriptionRequestDto { ProfileId = PaidProfile, GracePeriod = 5 });

            Assert.Equal(trialEnd.AddDays(5), Read(db => db.CompanyLicenses.Single(l => l.Id == trialId).ExpiryDate));
            Assert.Equal(paidEnd, Read(db => db.CompanyLicenses.Single(l => l.Id == paidId).ExpiryDate));
            Assert.Null(Sub(PaidProfile).GracePeriod);
            Assert.Equal(1, AuditRows());
        }

        [Fact]
        public async Task Unit_on_a_trial_licence_of_a_paid_subscription_is_extended()
        {
            var (trialId, paidId, _, paidEnd) = AddTrialLicenceToPaidProfile();
            var unit = await AddUnitWithLicence(PaidProfile, trialId);
            var newEnd = DateTime.UtcNow.AddDays(40);

            await Apply(new ApplySubscriptionRequestDto { ProfileId = PaidProfile, UnitId = unit, EndDate = newEnd });

            Assert.Equal(newEnd, Read(db => db.CompanyLicenses.Single(l => l.Id == trialId).ExpiryDate));
            Assert.Equal(paidEnd, Read(db => db.CompanyLicenses.Single(l => l.Id == paidId).ExpiryDate));
            Assert.Equal(SubscriptionStatus.Active, Sub(PaidProfile).Status);
        }

        [Fact]
        public async Task Unit_on_a_paid_licence_is_refused()
        {
            var (_, paidId, _, paidEnd) = AddTrialLicenceToPaidProfile();
            var unit = await AddUnitWithLicence(PaidProfile, paidId);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Apply(new ApplySubscriptionRequestDto { ProfileId = PaidProfile, UnitId = unit, EndDate = DateTime.UtcNow.AddDays(40) }));

            Assert.Equal("Trial extension is only allowed for a team on a trial license.", ex.Message);
            Assert.Equal(paidEnd, Read(db => db.CompanyLicenses.Single(l => l.Id == paidId).ExpiryDate));
            Assert.Equal(0, AuditRows());
        }

        private Task<uint> AddUnitWithLicence(uint profileId, int licenseId)
        {
            using var db = CreateContext(_db);
            const uint unitId = 9001;
            db.BusinessUnits.Add(new BusinessUnit
            {
                Id = unitId,
                BusinessUnitCode = $"BU{unitId}",
                BusinessUnitShortCode = $"S{unitId}",
                Name = "Paid profile team",
                NameInvariant = "paid profile team",
                RegistrationDate = DateTime.UtcNow,
                ProfileId = profileId,
                ActiveDailyQuestionSetGroupId = 1,
                ActiveDailyQuestionSetGroup = null!
            });
            db.AssignedCompanyLicenses.Add(new AssignedCompanyLicense
            {
                CompanyLicenseId = licenseId,
                BusinessUnitId = unitId,
                AssignedAt = DateTime.UtcNow.AddDays(-5),
                IsActive = true
            });
            db.SaveChanges();
            return Task.FromResult(unitId);
        }

        [Fact]
        public async Task Unknown_profile_is_refused()
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Apply(new ApplySubscriptionRequestDto { ProfileId = 999999, ExtendByDays = 14 }));

            Assert.Equal("Invalid ProfileId. Profile does not exist.", ex.Message);
            Assert.Equal(0, AuditRows());
        }

        [Fact]
        public async Task Profile_without_a_subscription_is_refused()
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Apply(new ApplySubscriptionRequestDto { ProfileId = ProfileA + 1000, ExtendByDays = 14 }));
            Assert.Equal("Invalid ProfileId. Profile does not exist.", ex.Message);

            using (var db = CreateContext(_db))
            {
                db.Profiles.Add(new ProfileEntity { Id = 888, ProfileShortCode = "P888", DomainName = "p888.test", ProfileName = "No sub", RegistrationDate = DateTime.UtcNow });
                db.SaveChanges();
            }
            ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Apply(new ApplySubscriptionRequestDto { ProfileId = 888, ExtendByDays = 14 }));
            Assert.Equal("Company has no subscription to extend.", ex.Message);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-3)]
        [InlineData(3651)]
        public async Task Days_out_of_range_are_refused(int days)
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Apply(new ApplySubscriptionRequestDto { ProfileId = ProfileA, ExtendByDays = days }));

            Assert.Equal("ExtendByDays must be between 1 and 3650.", ex.Message);
            Assert.Equal(_trialEndA, Sub(ProfileA).TrialEndDate);
        }

        [Fact]
        public async Task Days_with_end_date_or_unit_are_refused()
        {
            var both = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Apply(new ApplySubscriptionRequestDto { ProfileId = ProfileA, ExtendByDays = 5, EndDate = DateTime.UtcNow.AddDays(40) }));
            Assert.Equal("Provide ExtendByDays or EndDate, not both.", both.Message);

            var unit = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Apply(new ApplySubscriptionRequestDto { ProfileId = ProfileA, UnitId = Team, ExtendByDays = 5 }));
            Assert.Equal("ExtendByDays extends the whole profile's trial. Leave UnitId out.", unit.Message);
        }

        [Fact]
        public async Task Profile_with_no_teams_is_extended_without_an_error()
        {
            const uint noTeams = 555;
            var end = DateTime.UtcNow.AddDays(30);
            using (var db = CreateContext(_db))
            {
                db.Profiles.Add(new ProfileEntity { Id = noTeams, ProfileShortCode = "P555", DomainName = "p555.test", ProfileName = "No teams", RegistrationDate = DateTime.UtcNow });
                db.SaveChanges();
                AddTrial(db, noTeams, end, licences: 1);
            }

            var result = await Apply(new ApplySubscriptionRequestDto { ProfileId = noTeams, ExtendByDays = 1, GracePeriod = 2, Notes = "No teams yet" });

            var sub = Sub(noTeams);
            Assert.Equal(end.AddDays(1), sub.TrialEndDate);
            Assert.Equal(2, sub.GracePeriod);
            Assert.All(LicenceExpiries(noTeams), e => Assert.Equal(end.AddDays(1), e));

            Assert.Equal(noTeams, result.ProfileId);
            Assert.Empty(result.Units);
            Assert.Equal(end.AddDays(1), result.TrialExtension!.NewTrialEndDate);
            Assert.Equal(end.AddDays(3), result.TrialExtension.EffectiveExpiryDate);
            Assert.Equal(1, result.TrialExtension.LicensesUpdated);
            Assert.Equal(1, AuditRows());

            // The subscription detail API itself is unchanged for a profile with no teams.
            using var readDb = CreateContext(_db);
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new NoLockSubscriptionService(readDb).GetSubscriptionDetailsForAllUnitsAsync(noTeams));
            Assert.Equal("No business units found for this company.", ex.Message);
        }

        [Fact]
        public async Task End_date_mode_still_works_and_is_audited()
        {
            var end = DateTime.UtcNow.AddDays(60);

            var result = await Apply(new ApplySubscriptionRequestDto { ProfileId = ProfileA, EndDate = end, GracePeriod = 3 });

            var sub = Sub(ProfileA);
            Assert.Equal(end, sub.TrialEndDate);
            Assert.Equal(3, sub.GracePeriod);
            Assert.Null(result.TrialExtension!.DaysAdded);
            Assert.Equal(end.AddDays(3), result.TrialExtension.EffectiveExpiryDate);
            Assert.Equal(1, AuditRows());
        }
    }
}
