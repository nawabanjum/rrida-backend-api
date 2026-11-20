using CloverBackoffice.Components.Helpers;
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

namespace CloverBackoffice.Tests.Subscription
{
    /// <summary>
    /// POST api/subscription/cancel and api/subscription/reactivate work per Team
    /// Manager licence: two managers share one Stripe subscription (quantity 2),
    /// cancelling or reactivating one never changes the other's billing.
    /// </summary>
    public class LicenceCancelReactivateTests
    {
        private const string StripeSubId = "sub_shared";

        private readonly string _db = Guid.NewGuid().ToString();
        private readonly Mock<IStripeService> _stripe = new();
        private long _stripeQuantity = 2;
        private bool _stripeCancelAtPeriodEnd;
        private readonly int _subscriptionId;
        private readonly int _licenceA;
        private readonly int _licenceB;

        public LicenceCancelReactivateTests()
        {
            using (var db = CreateContext(_db))
            {
                Seed(db);
                var sub = new CompanySubscription
                {
                    ProfileId = ProfileA,
                    Status = SubscriptionStatus.Active,
                    StripeSubscriptionId = StripeSubId,
                    BillingCycle = BillingCycle.Monthly,
                    CurrentPeriodStart = DateTime.UtcNow.AddDays(-10),
                    CurrentPeriodEnd = DateTime.UtcNow.AddDays(20),
                    TeamCount = 2,
                    MaxTeamsAllowed = 2,
                    IsTrialUsed = true
                };
                db.CompanySubscriptions.Add(sub);
                db.SaveChanges();
                _subscriptionId = sub.Id;
                _licenceA = AddLicence(db, sub.Id, ManagerOfTeam, "Paid", sub.CurrentPeriodEnd);
                _licenceB = AddLicence(db, sub.Id, ManagerOfParent, "Paid", sub.CurrentPeriodEnd);
            }

            _stripe.Setup(s => s.GetSubscriptionQuantityAsync(StripeSubId)).ReturnsAsync(() => _stripeQuantity);
            _stripe.Setup(s => s.SetSubscriptionQuantityAsync(StripeSubId, It.IsAny<long>(), It.IsAny<bool>()))
                .Callback<string, long, bool>((_, q, _) => _stripeQuantity = q)
                .Returns(Task.CompletedTask);
            _stripe.Setup(s => s.CancelSubscriptionAsync(StripeSubId, true))
                .Callback(() => _stripeCancelAtPeriodEnd = true)
                .Returns(Task.CompletedTask);
            _stripe.Setup(s => s.ReactivateSubscriptionAsync(StripeSubId, It.IsAny<long?>()))
                .Callback<string, long?>((_, q) =>
                {
                    _stripeCancelAtPeriodEnd = false;
                    if (q.HasValue) _stripeQuantity = q.Value;
                })
                .Returns(Task.CompletedTask);
        }

        private static int AddLicence(ApplicationDbContext db, int subId, int holder, string type, DateTime expiry)
        {
            var licence = new CompanyLicense
            {
                ProfileId = ProfileA,
                LicenseKey = Guid.NewGuid().ToString(),
                LicenseType = type,
                CompanySubscriptionId = subId,
                AssignedUserId = holder,
                ExpiryDate = expiry
            };
            db.CompanyLicenses.Add(licence);
            db.SaveChanges();
            return licence.Id;
        }

        private SubscriptionService Service(ApplicationDbContext db) => new(
            new CompanySubscriptionRepository(db, new PaymentAuditLogRepository(db)),
            new Mock<IPlanRepository>().Object,
            _stripe.Object,
            new Mock<IPromoCodeService>().Object,
            new Mock<IPromoCodeRepository>().Object,
            new Mock<IPaymentRepository>().Object,
            new CompanyLicenseRepository(db),
            db,
            Options.Create(new StripeSettings()),
            NullLogger<SubscriptionService>.Instance,
            new Mock<ILegacyTrialBackfillService>().Object);

        private async Task<Infrastructure.DTO.CancelLicenseResultDto> Cancel(int userId, int actorId = ProfileAdminA, int? profileId = null)
        {
            using var db = CreateContext(_db);
            var actor = db.Users.Single(u => u.UserId == actorId);
            return await Service(db).CancelLicenseAsync(userId, actor, profileId);
        }

        private async Task<Infrastructure.DTO.CancelLicenseResultDto> Reactivate(int userId, int actorId = ProfileAdminA, int? profileId = null)
        {
            using var db = CreateContext(_db);
            var actor = db.Users.Single(u => u.UserId == actorId);
            return await Service(db).ReactivateLicenseAsync(userId, actor, profileId);
        }

        private T Read<T>(Func<ApplicationDbContext, T> read)
        {
            using var db = CreateContext(_db);
            return read(db);
        }

        private CompanySubscription Sub() => Read(db => db.CompanySubscriptions.Single(s => s.Id == _subscriptionId));
        private CompanyLicense Licence(int id) => Read(db => db.CompanyLicenses.Single(l => l.Id == id));

        [Fact]
        public async Task Cancel_one_manager_drops_one_seat_and_keeps_the_other_renewing()
        {
            var result = await Cancel(ManagerOfTeam);

            Assert.True(result.CancelAtPeriodEnd);
            Assert.True(result.CanReactivate);
            Assert.Equal(_licenceA, result.CompanyLicenseId);
            Assert.True(Licence(_licenceA).CancelAtPeriodEnd);
            Assert.False(Licence(_licenceB).CancelAtPeriodEnd);
            Assert.False(Sub().CancelAtPeriodEnd);
            Assert.Equal(1, Sub().TeamCount);
            Assert.Equal(1, _stripeQuantity);
            Assert.False(_stripeCancelAtPeriodEnd);
        }

        [Fact]
        public async Task Reactivate_after_licence_cancel_puts_the_seat_back()
        {
            await Cancel(ManagerOfTeam);

            var result = await Reactivate(ManagerOfTeam);

            Assert.False(result.CancelAtPeriodEnd);
            Assert.True(result.CanCancel);
            Assert.Equal("ACTIVE", result.Status);
            Assert.StartsWith("Renews", result.RenewsLabel);
            Assert.False(Licence(_licenceA).CancelAtPeriodEnd);
            Assert.Null(Licence(_licenceA).CanceledAt);
            Assert.Equal(2, Sub().TeamCount);
            Assert.Equal(2, Sub().MaxTeamsAllowed);
            Assert.Equal(2, _stripeQuantity);
            _stripe.Verify(s => s.SetSubscriptionQuantityAsync(StripeSubId, 2, false), Times.Once);
        }

        [Fact]
        public async Task Reactivate_when_the_whole_subscription_is_cancelling_renews_only_that_licence()
        {
            await Cancel(ManagerOfTeam);    // licence flag, quantity 1
            await Cancel(ManagerOfParent);  // last renewing seat: whole subscription
            Assert.True(Sub().CancelAtPeriodEnd);
            Assert.True(_stripeCancelAtPeriodEnd);

            var result = await Reactivate(ManagerOfTeam);

            Assert.False(result.CancelAtPeriodEnd);
            Assert.False(Sub().CancelAtPeriodEnd);
            Assert.Null(Sub().CanceledAt);
            Assert.Equal(1, Sub().TeamCount);
            Assert.False(Licence(_licenceA).CancelAtPeriodEnd);
            // The other manager stays cancelled, now on their own licence.
            Assert.True(Licence(_licenceB).CancelAtPeriodEnd);
            Assert.False(_stripeCancelAtPeriodEnd);
            Assert.Equal(1, _stripeQuantity);
            _stripe.Verify(s => s.ReactivateSubscriptionAsync(StripeSubId, 1), Times.Once);

            // ...and can be reactivated on their own afterwards.
            await Reactivate(ManagerOfParent);
            Assert.False(Licence(_licenceB).CancelAtPeriodEnd);
            Assert.Equal(2, Sub().TeamCount);
            Assert.Equal(2, _stripeQuantity);
        }

        [Fact]
        public async Task Reactivate_last_seat_clears_the_subscription_cancel()
        {
            using (var db = CreateContext(_db))
            {
                db.CompanyLicenses.Single(l => l.Id == _licenceB).IsDel = true;
                var sub = db.CompanySubscriptions.Single(s => s.Id == _subscriptionId);
                sub.TeamCount = 1;
                db.SaveChanges();
            }
            _stripeQuantity = 1;
            await Cancel(ManagerOfTeam);
            Assert.True(Sub().CancelAtPeriodEnd);
            Assert.False(Licence(_licenceA).CancelAtPeriodEnd);

            var result = await Reactivate(ManagerOfTeam);

            Assert.False(result.CancelAtPeriodEnd);
            Assert.False(Sub().CancelAtPeriodEnd);
            Assert.Equal(1, Sub().TeamCount);
            Assert.False(_stripeCancelAtPeriodEnd);
            Assert.Equal(1, _stripeQuantity);
        }

        [Fact]
        public async Task Reactivate_a_renewing_licence_is_a_no_op()
        {
            var result = await Reactivate(ManagerOfTeam);

            Assert.False(result.CancelAtPeriodEnd);
            Assert.True(result.CanCancel);
            _stripe.Verify(s => s.SetSubscriptionQuantityAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<bool>()), Times.Never);
            _stripe.Verify(s => s.ReactivateSubscriptionAsync(It.IsAny<string>(), It.IsAny<long?>()), Times.Never);
        }

        [Fact]
        public async Task Reactivate_an_ended_licence_is_refused()
        {
            await Cancel(ManagerOfTeam);
            using (var db = CreateContext(_db))
            {
                db.CompanyLicenses.Single(l => l.Id == _licenceA).ExpiryDate = DateTime.UtcNow.AddMinutes(-1);
                db.SaveChanges();
            }

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Reactivate(ManagerOfTeam));
            Assert.Equal(ResponseMessages.Errors.LicenseAlreadyEnded, ex.Message);
        }

        [Fact]
        public async Task Reactivate_after_the_stripe_subscription_ended_is_refused()
        {
            await Cancel(ManagerOfTeam);
            using (var db = CreateContext(_db))
            {
                db.CompanySubscriptions.Single(s => s.Id == _subscriptionId).Status = SubscriptionStatus.Expired;
                db.SaveChanges();
            }

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Reactivate(ManagerOfTeam));
            Assert.Equal(ResponseMessages.Errors.LicenseAlreadyEnded, ex.Message);
        }

        [Fact]
        public async Task Trial_licence_has_nothing_to_cancel_or_reactivate()
        {
            using (var db = CreateContext(_db))
            {
                db.CompanyLicenses.Single(l => l.Id == _licenceA).LicenseType = "Trial";
                db.SaveChanges();
            }

            var cancel = await Assert.ThrowsAsync<InvalidOperationException>(() => Cancel(ManagerOfTeam));
            Assert.Equal(ResponseMessages.Errors.LicenseHasNoRecurringBilling, cancel.Message);
            var reactivate = await Assert.ThrowsAsync<InvalidOperationException>(() => Reactivate(ManagerOfTeam));
            Assert.Equal(ResponseMessages.Errors.LicenseHasNoRecurringBillingToReactivate, reactivate.Message);
        }

        [Theory]
        [InlineData(ManagerOfTeam)]          // a Team Manager, even on their own licence
        [InlineData(PortfolioManagerOfTeam)] // same profile, not an admin
        [InlineData(ProfileAdminB)]          // Profile Admin of another profile
        public async Task Only_profile_admin_of_the_profile_or_rrida_admin_may_cancel_or_reactivate(int actorId)
        {
            var cancel = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Cancel(ManagerOfTeam, actorId));
            Assert.Equal(ResponseMessages.Errors.LicenseBillingNotAllowed, cancel.Message);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Reactivate(ManagerOfTeam, actorId));
            Assert.Equal(2, _stripeQuantity);
        }

        [Fact]
        public async Task Rrida_admin_may_cancel_and_reactivate_any_profile()
        {
            await Cancel(ManagerOfTeam, RridaAdmin, (int)ProfileA);
            Assert.True(Licence(_licenceA).CancelAtPeriodEnd);

            await Reactivate(ManagerOfTeam, RridaAdmin, (int)ProfileA);
            Assert.False(Licence(_licenceA).CancelAtPeriodEnd);
        }

        [Fact]
        public async Task Plan_change_permission_is_unchanged_a_team_manager_still_passes_it()
        {
            using var db = CreateContext(_db);
            var actor = db.Users.Single(u => u.UserId == ManagerOfTeam);

            // Gets past authorization to the next rule (already monthly), so the
            // admin-only rule of cancel / reactivate does not apply to plan change.
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => Service(db).StartLicensePlanChangeAsync(ManagerOfTeam, BillingCycle.Monthly, actor));
            Assert.Equal(string.Format(ResponseMessages.Errors.LicenseAlreadyOnBillingCycle, "monthly"), ex.Message);
        }

        [Fact]
        public async Task Cancel_and_reactivate_never_touch_the_subscription_period_or_create_a_subscription()
        {
            var before = Sub();

            await Cancel(ManagerOfTeam);
            await Reactivate(ManagerOfTeam);
            await Cancel(ManagerOfTeam);
            await Cancel(ManagerOfParent);
            await Reactivate(ManagerOfParent);

            var after = Sub();
            Assert.Equal(1, Read(db => db.CompanySubscriptions.Count()));
            Assert.Equal(before.CurrentPeriodStart, after.CurrentPeriodStart);
            Assert.Equal(before.CurrentPeriodEnd, after.CurrentPeriodEnd);
            Assert.Equal(before.BillingCycle, after.BillingCycle);
            Assert.Equal(before.Status, after.Status);
            Assert.Equal(before.StripeSubscriptionId, after.StripeSubscriptionId);
            Assert.Equal(2, Read(db => db.CompanyLicenses.Count()));
            Assert.Equal(before.CurrentPeriodEnd, Licence(_licenceA).ExpiryDate);
            Assert.Equal(before.CurrentPeriodEnd, Licence(_licenceB).ExpiryDate);
            Assert.Equal(ManagerOfTeam, Licence(_licenceA).AssignedUserId);
            Assert.Equal(ManagerOfParent, Licence(_licenceB).AssignedUserId);
        }

        [Fact]
        public async Task Profile_id_must_match_the_user()
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => Reactivate(ManagerOfTeam, RridaAdmin, (int)ProfileB));
            Assert.Equal(ResponseMessages.Errors.LicenseUserNotInProfile, ex.Message);
        }
    }
}
