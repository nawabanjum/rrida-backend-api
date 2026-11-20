using CloverBackoffice.Components.Enums;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Infrastructure.Models.TeamHealth;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;

namespace CloverBackoffice.Tests.TeamHealth
{
    /// <summary>
    /// Shared fixture data for the milestone tests. Ids mirror the Dev DB where they
    /// exist (profile 124, unit 294, Profile Admin 272).
    /// </summary>
    internal static class MilestoneTestData
    {
        public const uint ProfileA = 124;
        public const uint ProfileB = 200;

        public const uint ParentUnit = 290;     // profile A, parent of 294
        public const uint Team = 294;           // profile A, the team under test
        public const uint OtherTeamA = 295;     // profile A, nobody below manages it
        public const uint DeletedTeam = 296;    // profile A, soft-deleted
        public const uint TeamB = 500;          // profile B

        public const int TeamSize = 4;          // team 294: 4 members x 2 screens x 7 days = 56 possible

        public const int RridaAdmin = 1;
        public const int ProfileAdminA = 272;
        public const int ManagerOfTeam = 300;        // assigned to 294
        public const int ManagerOfParent = 301;      // assigned to 290, so reaches 294
        public const int UnassignedManager = 302;    // was on 294, now unassigned
        public const int PortfolioManagerOfTeam = 303;     // 300 reports to them
        public const int PortfolioManagerNoReports = 304;  // nobody reports to them
        public const int SeniorPortfolioManager = 305;     // 303 reports to them
        public const int FormerPortfolioManager = 306;     // 301 reported to them, now unassigned
        public const int ProfileAdminB = 400;

        public static readonly DateTimeOffset Today = new(2026, 9, 29, 10, 0, 0, TimeSpan.Zero);

        private static readonly Dictionary<int, (int? profileId, int[] roles)> Users = new()
        {
            [RridaAdmin] = (null, new[] { (int)UserGroups.RridaAdmin }),
            [ProfileAdminA] = ((int)ProfileA, new[] { (int)UserGroups.ProfileAdmin }),
            [ManagerOfTeam] = ((int)ProfileA, new[] { (int)UserGroups.TeamManager }),
            [ManagerOfParent] = ((int)ProfileA, new[] { (int)UserGroups.TeamManager }),
            [UnassignedManager] = ((int)ProfileA, new[] { (int)UserGroups.TeamManager }),
            [PortfolioManagerOfTeam] = ((int)ProfileA, new[] { (int)UserGroups.PortfolioManager }),
            [PortfolioManagerNoReports] = ((int)ProfileA, new[] { (int)UserGroups.PortfolioManager }),
            [SeniorPortfolioManager] = ((int)ProfileA, new[] { (int)UserGroups.PortfolioManager }),
            [FormerPortfolioManager] = ((int)ProfileA, new[] { (int)UserGroups.PortfolioManager }),
            [ProfileAdminB] = ((int)ProfileB, new[] { (int)UserGroups.ProfileAdmin }),
        };

        public static ApplicationDbContext CreateContext(string dbName, SaveTracker? tracker = null)
        {
            var builder = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(dbName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning));
            if (tracker != null)
                builder.AddInterceptors(tracker);
            return new TestApplicationDbContext(builder.Options);
        }

        public static void Seed(ApplicationDbContext db)
        {
            db.BusinessUnits.AddRange(
                Unit(ParentUnit, ProfileA, null),
                Unit(Team, ProfileA, (int)ParentUnit, teamSize: TeamSize),
                Unit(OtherTeamA, ProfileA, null),
                Unit(DeletedTeam, ProfileA, null, isDel: true),
                Unit(TeamB, ProfileB, null));

            foreach (var (id, (profileId, roles)) in Users)
            {
                db.Users.Add(new User
                {
                    UserId = id,
                    TenantId = "t",
                    Username = $"user{id}@yopmail.com",
                    Name = $"User {id}",
                    UserGroupId = roles[0],
                    ProfileId = profileId,
                    Status = "1",
                    Type = "User",
                    UserGroup = null!
                });
            }

            db.UserTeams.AddRange(
                new UserTeam { Id = 1, UserId = ManagerOfTeam, UnitId = Team, CreatedDate = DateTime.UtcNow },
                new UserTeam { Id = 2, UserId = ManagerOfParent, UnitId = ParentUnit, CreatedDate = DateTime.UtcNow },
                new UserTeam { Id = 3, UserId = UnassignedManager, UnitId = Team, CreatedDate = DateTime.UtcNow, IsUnAssigned = true });

            db.PortfolioManagerAssignments.AddRange(
                new PortfolioManagerAssignment { Id = 1, ProfileId = ProfileA, ManagerUserId = ManagerOfTeam, PortfolioManagerUserId = PortfolioManagerOfTeam, CreatedDate = DateTime.UtcNow },
                new PortfolioManagerAssignment { Id = 2, ProfileId = ProfileA, ManagerUserId = PortfolioManagerOfTeam, PortfolioManagerUserId = SeniorPortfolioManager, CreatedDate = DateTime.UtcNow },
                new PortfolioManagerAssignment { Id = 3, ProfileId = ProfileA, ManagerUserId = ManagerOfParent, PortfolioManagerUserId = FormerPortfolioManager, CreatedDate = DateTime.UtcNow, IsUnAssigned = true });

            db.SaveChanges();
        }

        public static void AddMilestone(ApplicationDbContext db, uint unitId, string date, string title, bool isDel = false)
        {
            db.TeamMilestones.Add(new TeamMilestone
            {
                ProfileId = unitId == TeamB ? ProfileB : ProfileA,
                UnitId = unitId,
                Title = title,
                Description = title + " description",
                MilestoneDate = DateOnly.Parse(date),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                IsDel = isDel
            });
            db.SaveChanges();
        }

        public static IUserRoleAssignmentService Roles()
        {
            var mock = new Mock<IUserRoleAssignmentService>();
            mock.Setup(r => r.GetRoleIdsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((int userId, CancellationToken _) =>
                    Users.TryGetValue(userId, out var u) ? u.roles.ToList() : new List<int>());
            return mock.Object;
        }

        private static BusinessUnit Unit(uint id, uint profileId, int? parentId, bool isDel = false, int? teamSize = null) => new()
        {
            Id = id,
            BusinessUnitCode = $"BU{id}",
            BusinessUnitShortCode = $"S{id}",
            Name = $"Team {id}",
            NameInvariant = $"team {id}",
            RegistrationDate = DateTime.UtcNow,
            ProfileId = profileId,
            ParentId = parentId,
            TeamSize = teamSize,
            IsDel = isDel,
            ActiveDailyQuestionSetGroupId = 1,
            ActiveDailyQuestionSetGroup = null!
        };
    }

    internal sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedClock(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    /// <summary>Records every entity type and state written by SaveChanges.</summary>
    internal sealed class SaveTracker : SaveChangesInterceptor
    {
        public List<(Type Type, EntityState State)> Writes { get; } = new();

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            Record(eventData.Context);
            return base.SavingChangesAsync(eventData, result, ct);
        }

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            Record(eventData.Context);
            return base.SavingChanges(eventData, result);
        }

        private void Record(DbContext? context)
        {
            if (context == null) return;
            foreach (var e in context.ChangeTracker.Entries())
            {
                if (e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                    Writes.Add((e.Metadata.ClrType, e.State));
            }
        }
    }
}
