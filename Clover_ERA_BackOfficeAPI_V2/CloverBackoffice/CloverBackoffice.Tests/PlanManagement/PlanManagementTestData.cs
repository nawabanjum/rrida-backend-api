using CloverBackoffice.Infrastructure.DTO.PlanManagement;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Infrastructure.Models.PlanManagement;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;

namespace CloverBackoffice.Tests.PlanManagement
{
    /// <summary>
    /// Shared data: RRIDA Admin 87 (Dev's seeded admin) and Profile Admin 272. The feature
    /// catalogue is seeded like the migration (system features, client's pricing table rows).
    /// </summary>
    internal static class PlanManagementTestData
    {
        public const int RridaAdmin = 87;
        public const int ProfileAdmin = 272;
        public static readonly DateTimeOffset Now = new(2026, 11, 11, 10, 0, 0, TimeSpan.Zero);

        public static ApplicationDbContext CreateContext(string dbName) =>
            new TestApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(dbName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);

        public static IUserRoleAssignmentService Roles()
        {
            var mock = new Mock<IUserRoleAssignmentService>();
            mock.Setup(r => r.GetRoleIdsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((int userId, CancellationToken _) => userId switch
                {
                    RridaAdmin => new List<int> { 3 },
                    ProfileAdmin => new List<int> { 1, 2 },
                    _ => new List<int>()
                });
            return mock.Object;
        }

        public static void Seed(ApplicationDbContext db)
        {
            db.Users.Add(new User
            {
                UserId = RridaAdmin,
                TenantId = "t",
                Username = "admin@rrida.com",
                Name = "RRIDA Admin",
                UserGroupId = 3,
                Status = "1",
                Type = "User",
                UserGroup = null!
            });
            var sort = 0;
            void F(string key, string title, string type, string? unit = null, string? period = null, string? footnote = null) =>
                db.PlanFeatures.Add(new PlanFeature
                {
                    Key = key, Title = title, ValueType = type, LimitUnit = unit, LimitPeriod = period,
                    Footnote = footnote, IsSystem = true, IsActive = true, SortOrder = ++sort,
                    CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime
                });

            F("MANAGERS_AND_TEAMS", "Managers and teams", PlanFeatureValueTypes.Limit, "teams", PlanFeatureLimitPeriods.None);
            F("DAILY_CHECKINS", "Daily anonymous team check-ins", PlanFeatureValueTypes.Included);
            F("WEEKLY_SUGGESTED_ACTION", "Weekly suggested action", PlanFeatureValueTypes.Text,
                footnote: "* Clover Framework includes over 2000 micro-actions.");
            F("PORTFOLIO_VIEW", "Team-of-teams Portfolio View", PlanFeatureValueTypes.Included);
            F("ERAN_COACH", "Eran coach", PlanFeatureValueTypes.Limit, "chats", PlanFeatureLimitPeriods.Month);
            db.SaveChanges();
        }

        public static List<PriceTierInputDto> ProTiers() => new()
        {
            new() { MinManagers = 1, MaxManagers = 4, MonthlyPrice = 695, AnnualPrice = 6950 },
            new() { MinManagers = 5, MaxManagers = 9, MonthlyPrice = 595, AnnualPrice = 5950 },
            new() { MinManagers = 10, MaxManagers = null, MonthlyPrice = 495, AnnualPrice = 4950 }
        };

        public static List<PriceTierInputDto> LightTiers() => new()
        {
            new() { MinManagers = 1, MonthlyPrice = 97, AnnualPrice = 970 }
        };

        public static CreateServicePlanDto Light() => new()
        {
            Code = "light",
            Name = "Light",
            InternalNotes = "Client pricing table 2026-11",
            IsActive = true,
            PriceTiers = LightTiers(),
            Features = new()
            {
                new() { FeatureKey = "MANAGERS_AND_TEAMS", IsIncluded = true, LimitValue = 1, DisplayText = "1" },
                new() { FeatureKey = "DAILY_CHECKINS", IsIncluded = true },
                new() { FeatureKey = "WEEKLY_SUGGESTED_ACTION", IsIncluded = true, DisplayText = "Clover actions" },
                new() { FeatureKey = "PORTFOLIO_VIEW", IsIncluded = false },
                new() { FeatureKey = "ERAN_COACH", IsIncluded = true, LimitValue = 10, DisplayText = "10 chats", SubText = "per month" }
            }
        };

        public static CreateServicePlanDto Pro() => new()
        {
            Code = "pro",
            Name = "Pro",
            IsHighlighted = true,
            AllowsTeamLimitExtension = true,
            IsActive = true,
            PriceTiers = ProTiers(),
            Features = new()
            {
                new() { FeatureKey = "MANAGERS_AND_TEAMS", IsIncluded = true, LimitValue = 3, DisplayText = "Up to 3" },
                new() { FeatureKey = "DAILY_CHECKINS", IsIncluded = true },
                new() { FeatureKey = "WEEKLY_SUGGESTED_ACTION", IsIncluded = true, DisplayText = "Clover actions", SubText = "*with Clover Resources" },
                new() { FeatureKey = "PORTFOLIO_VIEW", IsIncluded = true },
                new() { FeatureKey = "ERAN_COACH", IsIncluded = true }
            }
        };
    }
}
