using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.DTO.PlanManagement;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Implementation;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Implementation;
using CloverBackoffice.Services.Interfaces;
using CloverBackoffice.Tests.TeamHealth;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using static CloverBackoffice.Tests.PlanManagement.PlanManagementTestData;

namespace CloverBackoffice.Tests.PlanManagement
{
    /// <summary>
    /// POST api/subscription/checkout with a plan: the quantity's tier price goes to Stripe as
    /// an inline price and the plan id rides on the session metadata. No plan = legacy.
    /// </summary>
    public class PlanCheckoutTests
    {
        private const uint Profile = 124;
        private readonly string _db = Guid.NewGuid().ToString();
        private readonly int _proId;

        private StripePlanPrice? _sentPrice;
        private IDictionary<string, string>? _sentMetadata;
        private int _sentQuantity;
        private bool _called;

        public PlanCheckoutTests()
        {
            using var db = CreateContext(_db);
            Seed(db);
            var admin = new PlanManagementService(db, Roles(), new FixedClock(Now));
            admin.CreatePlanAsync(Light(), RridaAdmin).Wait();
            _proId = admin.CreatePlanAsync(Pro(), RridaAdmin).Result.Data!.Id;
        }

        private SubscriptionService Service(ApplicationDbContext db)
        {
            var stripe = new Mock<IStripeService>();
            stripe.Setup(s => s.GetOrCreateCustomerAsync(It.IsAny<uint>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync("cus_test");
            stripe.Setup(s => s.CreateDynamicCheckoutSessionAsync(
                    It.IsAny<string>(), It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<BillingCycle>(), It.IsAny<int>(),
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<uint>(), It.IsAny<decimal?>(), It.IsAny<bool>(),
                    It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<IDictionary<string, string>?>(), It.IsAny<StripePlanPrice?>()))
                .Callback((string _, int qty, decimal _, BillingCycle _, int _, string _, string _, uint _, decimal? _, bool _,
                           string? _, int? _, IDictionary<string, string>? md, StripePlanPrice? price) =>
                {
                    _called = true;
                    _sentQuantity = qty;
                    _sentMetadata = md;
                    _sentPrice = price;
                })
                .ReturnsAsync("https://checkout.stripe.test/session");

            return new SubscriptionService(
                new CompanySubscriptionRepository(db, new PaymentAuditLogRepository(db)),
                new Mock<IPlanRepository>().Object,
                stripe.Object,
                new Mock<IPromoCodeService>().Object,
                new Mock<IPromoCodeRepository>().Object,
                new Mock<IPaymentRepository>().Object,
                new CompanyLicenseRepository(db),
                db,
                Options.Create(new StripeSettings { PricePerLicenseMonthly = 695 }),
                NullLogger<SubscriptionService>.Instance,
                new Mock<ILegacyTrialBackfillService>().Object);
        }

        private async Task<CheckoutResponseDto> Checkout(int qty, BillingCycle cycle, int? planId = null, string? planCode = null)
        {
            using var db = CreateContext(_db);
            return await Service(db).CreateCheckoutAsync(Profile, qty, cycle, "pa@test.com", "PA", null, planId, planCode);
        }

        [Fact]
        public async Task Plan_checkout_sends_the_tier_price_and_plan_metadata()
        {
            var r = await Checkout(6, BillingCycle.Annual, planCode: "pro");

            Assert.Equal("https://checkout.stripe.test/session", r.SessionUrl);
            Assert.Equal(6, _sentQuantity);
            Assert.NotNull(_sentPrice);
            Assert.Equal(5950m, _sentPrice!.UnitAmount);           // annual price as is, no multiplier
            Assert.Equal("USD", _sentPrice.Currency);
            Assert.Equal("RRIDA Pro - per manager", _sentPrice.ProductName);
            Assert.Equal(_proId.ToString(), _sentMetadata![SubscriptionService.ServicePlanIdMetadataKey]);
            Assert.Equal("pro", _sentMetadata[SubscriptionService.ServicePlanCodeMetadataKey]);
            Assert.Equal(_proId, SubscriptionService.ParseServicePlanIdMetadata(_sentMetadata));
        }

        [Fact]
        public async Task Single_pro_licence_monthly_is_695()
        {
            await Checkout(1, BillingCycle.Monthly, planId: _proId);
            Assert.Equal(695m, _sentPrice!.UnitAmount);
        }

        [Fact]
        public async Task No_plan_is_priced_as_pro_at_its_current_version()
        {
            await Checkout(6, BillingCycle.Monthly);

            Assert.True(_called);
            Assert.Equal(595m, _sentPrice!.UnitAmount);                       // Pro 5-9 tier
            Assert.Equal(_proId.ToString(), _sentMetadata![SubscriptionService.ServicePlanIdMetadataKey]);
            Assert.True(_sentMetadata.ContainsKey(SubscriptionService.ServicePlanVersionIdMetadataKey));
        }

        [Theory]
        [InlineData("gold")]     // unknown
        public async Task Unpriceable_plan_fails_before_stripe(string code)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => Checkout(1, BillingCycle.Monthly, planCode: code));
            Assert.False(_called);
        }

        [Fact]
        public void Plan_id_metadata_parsing()
        {
            Assert.Equal(7, SubscriptionService.ParseServicePlanIdMetadata(new Dictionary<string, string> { ["servicePlanId"] = "7" }));
            Assert.Null(SubscriptionService.ParseServicePlanIdMetadata(new Dictionary<string, string> { ["servicePlanId"] = "x" }));
            Assert.Null(SubscriptionService.ParseServicePlanIdMetadata(new Dictionary<string, string>()));
            Assert.Null(SubscriptionService.ParseServicePlanIdMetadata(null));
        }
    }
}
