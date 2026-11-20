using System.Runtime.CompilerServices;
using CloverBackoffice.Components.Enums;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO.TeamHealth;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Infrastructure.Models.DeterministicLayer;
using CloverBackoffice.Infrastructure.Models.TeamHealth;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Implementation;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Rrida.Engine;
using Xunit;
using ProfileEntity = CloverBackoffice.Infrastructure.Models.Profile;

namespace CloverBackoffice.Tests.TeamHealth
{
    /// <summary>
    /// Portfolio View per manager: GET api/portfolio/managers, teams, alerts and eran-context.
    /// Portfolio Manager 500 has Team Manager 510 and Portfolio Manager 501 below them, and 501
    /// has Team Manager 511. Portfolio Manager 600 and their Team Manager 520 are another
    /// portfolio in the same profile; 530 reports to 500 from another profile. In the profile
    /// also: Team Manager 540, under nobody and without teams, and Profile Admin 550. RRIDA
    /// Admin 1 sits in a profile of their own, as the seeded admin does on Dev.
    /// </summary>
    public class PortfolioViewServiceTests
    {
        private const uint Profile = 70, ForeignProfile = 71, AdminProfile = 72, DeletedProfile = 73;
        private const int Admin = 1, Pm = 500, SubPm = 501, Tm510 = 510, Tm511 = 511, OtherPm = 600, Tm520 = 520, ForeignTm = 530, Tm540 = 540, ProfileAdmin = 550;
        private const int LastDay = 99;   // rows Day 1..99, StartDate = today - 99

        private static readonly Lazy<EngineContext> Ctx = new(() => EngineContext.FromKit(Path.Combine(EngineRoot(), "kit", "data")));

        [Fact]
        public async Task Managers_are_everyone_below_the_portfolio_manager_and_nobody_else()
        {
            await using var db = await SeedAsync();

            var result = await Service(db).GetManagersAsync(Pm);

            Assert.True(result.IsSuccess);
            Assert.Equal(new[] { SubPm, Tm510, Tm511 }, result.Data!.Managers.Select(m => m.ManagerId));   // by name
            var sub = result.Data.Managers.Single(m => m.ManagerId == SubPm);
            Assert.True(sub.IsPortfolioManager);
            Assert.Equal(Pm, sub.ReportsToManagerId);
            Assert.Equal(SubPm, result.Data.Managers.Single(m => m.ManagerId == Tm511).ReportsToManagerId);
            Assert.Equal(2, result.Data.Managers.Single(m => m.ManagerId == Tm510).TeamCount);
        }

        [Fact]
        public async Task All_managers_returns_every_portfolio_team_in_attention_order()
        {
            await using var db = await SeedAsync();

            var result = await Service(db).GetTeamsAsync(Pm, null);

            Assert.True(result.IsSuccess);
            Assert.Null(result.Data!.ManagerId);
            Assert.Equal(new uint[] { 9002, 9003, 9001, 9005 }, result.Data.Teams.Select(t => t.TeamId));
            Assert.Equal(2, result.Data.ManagerCount);
            Assert.DoesNotContain(result.Data.Teams, t => t.TeamId == 9004);
        }

        [Fact]
        public async Task A_selected_manager_returns_only_their_teams()
        {
            await using var db = await SeedAsync();

            var result = await Service(db).GetTeamsAsync(Pm, Tm510);

            Assert.Equal(new uint[] { 9002, 9001 }, result.Data!.Teams.Select(t => t.TeamId));
            Assert.All(result.Data.Teams, t => Assert.Equal(Tm510, t.TeamManagerId));
            Assert.Equal("Team Manager 510", result.Data.ManagerName);
        }

        [Theory]
        [InlineData(Pm, Tm520, TeamMilestoneError.Forbidden)]       // another portfolio's manager
        [InlineData(Pm, ForeignTm, TeamMilestoneError.Forbidden)]   // reports to them from another profile
        [InlineData(Pm, -1, TeamMilestoneError.BadRequest)]
        [InlineData(Tm510, null, TeamMilestoneError.Forbidden)]     // not a Portfolio Manager
        public async Task Out_of_scope_requests_are_refused(int actor, int? managerId, TeamMilestoneError expected)
        {
            await using var db = await SeedAsync();
            var service = Service(db);

            Assert.Equal(expected, (await service.GetTeamsAsync(actor, managerId)).Error);
            Assert.Equal(expected, (await service.GetAlertsAsync(actor, managerId)).Error);
            Assert.Equal(expected, (await service.GetEranContextAsync(actor, managerId)).Error);
            Assert.Equal(expected, (await service.BuildEranContextTextAsync(actor, managerId)).Error);
        }

        [Fact]
        public async Task Each_team_carries_state_weeks_members_and_a_quarter_of_weekly_trend()
        {
            await using var db = await SeedAsync();

            var teams = (await Service(db).GetTeamsAsync(Pm, null)).Data!.Teams.ToDictionary(t => t.TeamId);

            var cruise = teams[9001];
            Assert.Equal(TeamStates.Cruise, cruise.HealthState);
            Assert.Equal(14, cruise.WeeksCount);                 // counted days 7..99
            Assert.Equal(5, cruise.MemberCount);
            Assert.Equal("Team Manager 510", cruise.TeamManagerName);
            Assert.Equal(13, cruise.Trend.Count);
            Assert.All(cruise.Trend, p => Assert.Equal(100, p.Score));
            Assert.Equal(cruise.AsOfDate, cruise.Trend[^1].AsOfDate);

            var headwinds = teams[9002];
            Assert.Equal(TeamStates.Headwinds, headwinds.HealthState);
            Assert.Equal(TeamStates.Cruise, headwinds.Vertical);
            Assert.True(headwinds.HeadwindsFlag);
            Assert.Equal(3, headwinds.WeeksCount);              // days 79..99

            var stall = teams[9003];
            Assert.Equal(TeamStates.Stall, stall.HealthState);
            Assert.Equal(5, stall.WeeksCount);                  // days 65..99
            Assert.Equal(0, stall.Trend[^1].Score);
            Assert.Equal(100, stall.Trend[^1].Stall);
            Assert.Equal(TeamStates.Drift, stall.Trend[0].State);
        }

        [Fact]
        public async Task A_team_below_the_visibility_floor_reads_as_calibrating()
        {
            await using var db = await SeedAsync();

            var tiny = (await Service(db).GetTeamsAsync(Pm, Tm511)).Data!.Teams.Single(t => t.TeamId == 9005);

            Assert.True(tiny.IsCalibrating);
            Assert.Null(tiny.HealthState);
            Assert.Equal(0, tiny.WeeksCount);
            Assert.Empty(tiny.Trend);
            Assert.Equal(2, tiny.MemberCount);
        }

        [Fact]
        public async Task The_member_count_is_the_team_size_when_one_is_set()
        {
            await using var db = await SeedAsync();
            db.BusinessUnits.Single(b => b.Id == 9001).TeamSize = 7;   // 5 enrolled devices, 7 people
            await db.SaveChangesAsync();

            var cruise = (await Service(db).GetTeamsAsync(Pm, null)).Data!.Teams.Single(t => t.TeamId == 9001);

            Assert.Equal(7, cruise.MemberCount);
            Assert.Equal(TeamStates.Cruise, cruise.HealthState);   // 5 enrolled still clears the floor of 4
        }

        [Fact]
        public async Task A_team_size_below_the_visibility_floor_withholds_the_state_even_with_more_enrolments()
        {
            await using var db = await SeedAsync();
            db.BusinessUnits.Single(b => b.Id == 9001).TeamSize = 3;   // 5 enrolments, but 3 people
            await db.SaveChangesAsync();

            var team = (await Service(db).GetTeamsAsync(Pm, null)).Data!.Teams.Single(t => t.TeamId == 9001);

            Assert.Equal(3, team.MemberCount);
            Assert.True(team.IsCalibrating);
            Assert.Null(team.HealthState);
        }

        [Fact]
        public async Task A_large_team_size_cannot_reveal_a_team_with_few_enrolments()
        {
            await using var db = await SeedAsync();
            db.BusinessUnits.Single(b => b.Id == 9005).TeamSize = 10;  // only 2 enrolled
            await db.SaveChangesAsync();

            var tiny = (await Service(db).GetTeamsAsync(Pm, Tm511)).Data!.Teams.Single(t => t.TeamId == 9005);

            Assert.Equal(10, tiny.MemberCount);
            Assert.True(tiny.IsCalibrating);   // the floor counts the fewer of the two: 2
        }

        [Fact]
        public async Task Alerts_are_the_headwinds_and_stall_teams_in_scope()
        {
            await using var db = await SeedAsync();
            var service = Service(db);

            var all = (await service.GetAlertsAsync(Pm, null)).Data!;
            Assert.Equal(2, all.AttentionCount);
            Assert.Equal(all.AttentionCount, all.Teams.Count);
            Assert.Equal(new uint[] { 9002, 9003 }, all.ReviewTeamIds);
            Assert.Equal("2 teams need you, not the manager.", all.Title);
            Assert.Contains("Field Sales is carrying external load", all.Message);
            Assert.Contains("Implementation has been in Stall for 5 weeks.", all.Message);
            Assert.Equal(Tm511, all.Teams.Single(t => t.TeamId == 9003).ManagerId);

            var one = (await service.GetAlertsAsync(Pm, Tm511)).Data!;
            Assert.Equal(new uint[] { 9003 }, one.ReviewTeamIds);   // 9005 is below the floor
            Assert.Equal("1 team needs you, not the manager.", one.Title);

            var none = (await service.GetAlertsAsync(Pm, SubPm)).Data!;
            Assert.Equal(0, none.AttentionCount);
            Assert.Null(none.Title);
        }

        [Fact]
        public async Task Eran_is_told_only_about_the_teams_in_scope()
        {
            await using var db = await SeedAsync();
            var service = Service(db);

            var brief = (await service.GetEranContextAsync(Pm, null)).Data!;
            Assert.Equal("portfolio", brief.Scope);
            Assert.Equal("Two of your four teams want a look this week.", brief.Lead);
            Assert.Contains("Why is Implementation still in Stall?", brief.Prompts);
            Assert.Equal(1, brief.StateCounts.Calibrating);

            var all = (await service.BuildEranContextTextAsync(Pm, null)).Data!;
            Assert.Contains("Platform", all);
            Assert.Contains("Implementation", all);
            Assert.DoesNotContain("Other Team", all);
            Assert.DoesNotContain("Team Manager 520", all);

            var one = (await service.BuildEranContextTextAsync(Pm, Tm510)).Data!;
            Assert.Contains("one manager in their portfolio: Team Manager 510", one);
            Assert.Contains("Field Sales", one);
            Assert.DoesNotContain("Implementation", one);
        }

        [Fact]
        public async Task Rrida_admin_sees_every_manager_in_the_selected_profile()
        {
            await using var db = await SeedAsync();

            var result = await Service(db).GetManagersAsync(Admin, Profile);

            Assert.True(result.IsSuccess);
            Assert.Equal(Profile, result.Data!.ProfileId);
            Assert.Equal(new[] { Pm, SubPm, Tm510, Tm511, Tm520, Tm540, OtherPm }, result.Data.Managers.Select(m => m.ManagerId));
            Assert.Equal(Pm, result.Data.Managers.Single(m => m.ManagerId == Tm510).ReportsToManagerId);
            Assert.Equal(0, result.Data.Managers.Single(m => m.ManagerId == Pm).ReportsToManagerId);
            Assert.True(result.Data.Managers.Single(m => m.ManagerId == Pm).IsPortfolioManager);
            Assert.Equal(0, result.Data.Managers.Single(m => m.ManagerId == Tm540).TeamCount);
        }

        [Fact]
        public async Task Rrida_admin_gets_all_teams_of_the_profile_or_one_managers()
        {
            await using var db = await SeedAsync();
            var service = Service(db);

            var all = await service.GetTeamsAsync(Admin, null, Profile);
            Assert.True(all.IsSuccess);
            Assert.Equal(new uint[] { 9001, 9002, 9003, 9004, 9005 }, all.Data!.Teams.Select(t => t.TeamId).Order());
            Assert.Equal(3, all.Data.ManagerCount);

            var one = await service.GetTeamsAsync(Admin, Tm520, Profile);
            Assert.Equal(new uint[] { 9004 }, one.Data!.Teams.Select(t => t.TeamId));
            Assert.Equal("Team Manager 520", one.Data.ManagerName);
        }

        [Fact]
        public async Task Rrida_admin_gets_alerts_and_eran_context_for_the_profile()
        {
            await using var db = await SeedAsync();
            var service = Service(db);

            var alerts = (await service.GetAlertsAsync(Admin, null, Profile)).Data!;
            Assert.Equal(new uint[] { 9002, 9003, 9004 }, alerts.ReviewTeamIds.Order());

            var brief = (await service.GetEranContextAsync(Admin, null, Profile)).Data!;
            Assert.Equal("portfolio", brief.Scope);
            Assert.Equal("Three of this profile's five teams want a look this week.", brief.Lead);

            var all = (await service.BuildEranContextTextAsync(Admin, null, Profile)).Data!;
            Assert.Contains("a RRIDA administrator looking at every team across the managers in this profile", all);
            Assert.Contains("Other Team", all);
            Assert.DoesNotContain("Portfolio Manager", all);

            var one = (await service.BuildEranContextTextAsync(Admin, Tm520, Profile)).Data!;
            Assert.Contains("one manager in this profile: Team Manager 520", one);
            Assert.DoesNotContain("Implementation", one);
        }

        [Theory]
        [InlineData(null, null, TeamMilestoneError.BadRequest)]             // never falls back to their own profile
        [InlineData(0u, null, TeamMilestoneError.BadRequest)]
        [InlineData(9999u, null, TeamMilestoneError.NotFound)]
        [InlineData(DeletedProfile, null, TeamMilestoneError.NotFound)]
        [InlineData(Profile, ForeignTm, TeamMilestoneError.Forbidden)]      // that manager is in another profile
        [InlineData(ForeignProfile, Tm510, TeamMilestoneError.Forbidden)]
        [InlineData(Profile, -1, TeamMilestoneError.BadRequest)]
        public async Task Rrida_admin_out_of_scope_requests_are_refused(uint? profile, int? managerId, TeamMilestoneError expected)
        {
            await using var db = await SeedAsync();
            var service = Service(db);

            Assert.Equal(expected, (await service.GetTeamsAsync(Admin, managerId, profile)).Error);
            Assert.Equal(expected, (await service.GetAlertsAsync(Admin, managerId, profile)).Error);
            Assert.Equal(expected, (await service.GetEranContextAsync(Admin, managerId, profile)).Error);
            Assert.Equal(expected, (await service.BuildEranContextTextAsync(Admin, managerId, profile)).Error);
        }

        [Fact]
        public async Task A_portfolio_manager_stays_in_their_own_portfolio_whatever_profile_they_send()
        {
            await using var db = await SeedAsync();
            var service = Service(db);

            var own = await service.GetManagersAsync(Pm, Profile);
            Assert.Equal(new[] { SubPm, Tm510, Tm511 }, own.Data!.Managers.Select(m => m.ManagerId));

            Assert.Equal(TeamMilestoneError.Forbidden, (await service.GetTeamsAsync(Pm, Tm520, Profile)).Error);
            Assert.Equal(TeamMilestoneError.Forbidden, (await service.GetManagersAsync(Pm, ForeignProfile)).Error);
            Assert.Equal(TeamMilestoneError.Forbidden, (await service.GetTeamsAsync(Pm, null, ForeignProfile)).Error);
        }

        [Theory]
        [InlineData(Tm510)]
        [InlineData(ProfileAdmin)]
        public async Task Other_roles_stay_refused_even_naming_their_own_profile(int actor)
        {
            await using var db = await SeedAsync();
            var service = Service(db);

            Assert.Equal(TeamMilestoneError.Forbidden, (await service.GetManagersAsync(actor, Profile)).Error);
            Assert.Equal(TeamMilestoneError.Forbidden, (await service.GetTeamsAsync(actor, null, Profile)).Error);
        }

        // ---- action columns ---------------------------------------------------------------

        [Fact]
        public async Task Teams_without_actions_return_zero_and_empty_action_fields()
        {
            await using var db = await SeedAsync();

            var teams = (await Service(db).GetTeamsAsync(Pm, null)).Data!.Teams;

            Assert.All(teams, t =>
            {
                Assert.Equal(0, t.ActionsCountLast90Days);
                Assert.Equal(0, t.ActionsInFlight);
                Assert.Null(t.Action);
                Assert.Null(t.LastActionUpdate);
                Assert.Null(t.LatestAction);
            });
        }

        [Fact]
        public async Task Action_columns_count_manual_and_taken_up_engine_actions_per_team()
        {
            await using var db = await SeedAsync();
            var now = DateTime.UtcNow;

            // Platform (9001): an open manual action, an old done one, a deleted one, and an
            // engine action taken up 5 days ago that the manager closed yesterday.
            AddManual(db, 9001, "Run a retro", ManagerActionStatuses.Open, created: now.AddDays(-10), updated: now.AddDays(-2));
            AddManual(db, 9001, "Old plan", ManagerActionStatuses.Done, created: now.AddDays(-120), updated: now.AddDays(-100));
            AddManual(db, 9001, "Deleted", ManagerActionStatuses.Open, created: now.AddDays(-1), updated: now.AddDays(-1), isDel: true);
            var closedEngine = await AddEngineAsync(db, 9001, "C1", "Ask what got in the way.", takenUp: now.AddDays(-5));
            db.TeamActionStates.Add(new TeamActionState
            {
                ProfileId = Profile, UnitId = 9001, CycleKey = "C1", Rung = 1, ActionKey = "MA-E-002-22",
                Status = TeamActionStatuses.Closed, ClosedAt = now.AddDays(-1), Version = 1,
                CreatedAt = now.AddDays(-1), UpdatedAt = now.AddDays(-1)
            });

            // Implementation (9003): one engine action in flight, taken up 3 days ago.
            await AddEngineAsync(db, 9003, "C1", "Name one blocker.", takenUp: now.AddDays(-3));
            await db.SaveChangesAsync();

            var teams = (await Service(db).GetTeamsAsync(Pm, null)).Data!.Teams.ToDictionary(t => t.TeamId);

            var platform = teams[9001];
            Assert.Equal(2, platform.ActionsCountLast90Days);       // the open manual + the engine one; old and deleted excluded
            Assert.Equal(1, platform.ActionsInFlight);
            Assert.Equal("1 in flight", platform.Action);
            Assert.Equal(now.AddDays(-1), platform.LastActionUpdate!.Value, TimeSpan.FromSeconds(1));
            Assert.Equal(DateTimeKind.Utc, platform.LastActionUpdate.Value.Kind);
            Assert.Equal($"engine-{closedEngine}", platform.LatestAction!.ActionId);
            Assert.Equal(ManagerActionSources.Engine, platform.LatestAction.Source);
            Assert.Equal(ManagerActionStatuses.Done, platform.LatestAction.Status);

            var implementation = teams[9003];
            Assert.Equal(1, implementation.ActionsCountLast90Days);
            Assert.Equal("1 in flight", implementation.Action);
            Assert.Equal("Name one blocker.", implementation.LatestAction!.Title);
            Assert.Equal(ManagerActionStatuses.InProgress, implementation.LatestAction.Status);

            // Nothing leaks onto the other teams.
            Assert.Equal(0, teams[9002].ActionsCountLast90Days);
            Assert.Null(teams[9002].Action);
            Assert.Null(teams[9002].LastActionUpdate);
        }

        [Fact]
        public async Task Only_actions_from_the_last_90_days_are_counted()
        {
            await using var db = await SeedAsync();
            var now = DateTime.UtcNow;
            AddManual(db, 9002, "Day 90", ManagerActionStatuses.Done, created: now.AddDays(-89), updated: now.AddDays(-89));
            AddManual(db, 9002, "Day 91", ManagerActionStatuses.Done, created: now.AddDays(-90), updated: now.AddDays(-90));
            await db.SaveChangesAsync();

            var team = (await Service(db).GetTeamsAsync(Pm, Tm510)).Data!.Teams.Single(t => t.TeamId == 9002);

            Assert.Equal(1, team.ActionsCountLast90Days);
            Assert.Null(team.Action);                                // both done: nothing in flight
            Assert.Equal("Day 90", team.LatestAction!.Title);
            Assert.Equal(now.AddDays(-89), team.LastActionUpdate!.Value, TimeSpan.FromSeconds(1));
        }

        private static void AddManual(ApplicationDbContext db, uint unitId, string title, string status,
            DateTime created, DateTime updated, bool isDel = false) =>
            db.ManagerActions.Add(new ManagerAction
            {
                ProfileId = Profile, UnitId = unitId, Title = title, Source = ManagerActionSources.Manual,
                Status = status, CreatedBy = Tm510, CreatedAt = created, UpdatedAt = updated,
                CompletedAt = status == ManagerActionStatuses.Done ? updated : null, IsDel = isDel
            });

        // One delivered engine move, taken up with "I'll do it" at takenUp; returns the api/actions engine id.
        private static async Task<long> AddEngineAsync(ApplicationDbContext db, uint unitId, string key, string text, DateTime takenUp)
        {
            var cycle = new EngineCycle { BusinessUnitId = unitId, CycleKey = key, CycleIndex = 1, IssueFileKey = "MA-E-002", Issue = "Blockers", DeliveredDay = 8 };
            db.EngineCycles.Add(cycle);
            await db.SaveChangesAsync();
            var delivery = new EngineCycleDelivery { CycleId = cycle.Id, Rung = 1, ActionKey = "MA-E-002-22", Route = "team", DeliveredDay = 8, DeliveredText = text };
            db.EngineCycleDeliveries.Add(delivery);
            db.EngineAcknowledgements.Add(new EngineAcknowledgement
            {
                BusinessUnitId = unitId, CycleKey = key, Rung = 1, AcknowledgedByUserId = Tm510, AcknowledgedAt = takenUp
            });
            await db.SaveChangesAsync();
            return delivery.Id;
        }

        // ---- data -------------------------------------------------------------------------

        private static async Task<ApplicationDbContext> SeedAsync()
        {
            var db = new TestApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"portfolio-view-{Guid.NewGuid()}")
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);

            foreach (var (id, isDel) in new[] { (Profile, false), (ForeignProfile, false), (AdminProfile, false), (DeletedProfile, true) })
                db.Profiles.Add(new ProfileEntity
                {
                    Id = id,
                    ProfileShortCode = $"P{id}",
                    DomainName = $"p{id}.test",
                    ProfileName = $"Profile {id}",
                    RegistrationDate = DateTime.UtcNow,
                    IsDel = isDel
                });

            foreach (var id in new[] { Pm, SubPm, OtherPm })
                db.Users.Add(User(id, Profile, UserGroups.PortfolioManager));
            foreach (var id in new[] { Tm510, Tm511, Tm520, Tm540 })
                db.Users.Add(User(id, Profile, UserGroups.TeamManager));
            db.Users.Add(User(ProfileAdmin, Profile, UserGroups.ProfileAdmin));
            db.Users.Add(User(ForeignTm, ForeignProfile, UserGroups.TeamManager));
            db.Users.Add(User(Admin, AdminProfile, UserGroups.RridaAdmin));

            Report(db, Tm510, Pm, Profile);
            Report(db, SubPm, Pm, Profile);
            Report(db, Tm511, SubPm, Profile);
            Report(db, Tm520, OtherPm, Profile);
            Report(db, ForeignTm, Pm, ForeignProfile);

            Team(db, 9001, "Platform", Tm510, 5, _ => ("cruise", false));
            Team(db, 9002, "Field Sales", Tm510, 5, d => ("cruise", d >= 79));
            Team(db, 9003, "Implementation", Tm511, 5, d => (d >= 65 ? "stall" : "drift", false));
            Team(db, 9004, "Other Team", Tm520, 5, _ => ("stall", false));
            Team(db, 9005, "Tiny", Tm511, 2, _ => ("stall", false));

            await db.SaveChangesAsync();
            return db;
        }

        private static User User(int id, uint profile, UserGroups group) => new()
        {
            UserId = id,
            Username = $"u{id}@test.local",
            Name = $"Team Manager {id}",
            TenantId = "t",
            Type = "User",
            Status = "1",
            UserGroupId = (int)group,
            UserGroup = null!,
            ProfileId = (int)profile
        };

        private static void Report(ApplicationDbContext db, int manager, int portfolioManager, uint profile) =>
            db.PortfolioManagerAssignments.Add(new PortfolioManagerAssignment
            {
                ManagerUserId = manager,
                PortfolioManagerUserId = portfolioManager,
                ProfileId = profile,
                CreatedDate = DateTime.UtcNow
            });

        private static void Team(ApplicationDbContext db, uint unitId, string name, int manager, int members,
            Func<int, (string Vertical, bool Headwinds)> day)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            db.BusinessUnits.Add(new BusinessUnit
            {
                Id = unitId, BusinessUnitCode = $"BU{unitId}", BusinessUnitShortCode = $"S{unitId}", Name = name,
                NameInvariant = name.ToLowerInvariant(), RegistrationDate = DateTime.UtcNow, ProfileId = Profile, IsDel = false,
                ActiveDailyQuestionSetGroupId = 1, ActiveDailyQuestionSetGroup = null!
            });
            db.UserTeams.Add(new UserTeam { UserId = manager, UnitId = unitId, CreatedDate = DateTime.UtcNow });
            db.EngineTeamSettings.Add(new EngineTeamSettings { BusinessUnitId = unitId, StartDate = today.AddDays(-LastDay), IsEnabled = true });

            for (var m = 0; m < members; m++)
                db.EngineTeamMemberships.Add(new EngineTeamMembership
                {
                    MemberId = unitId * 100 + m,
                    BusinessUnitId = unitId,
                    EffectiveFrom = today.AddDays(-LastDay)
                });

            for (var d = 1; d <= LastDay; d++)
            {
                var (vertical, headwinds) = day(d);
                db.EngineStateDaily.Add(new EngineStateDaily
                {
                    BusinessUnitId = unitId,
                    Day = d,
                    Schema = "s",
                    ConfigId = "c",
                    Vertical = vertical,
                    Horizontal = "within",
                    HeadwindsFlag = headwinds,
                    RecordJson = "{}"
                });
            }
        }

        private static PortfolioViewService Service(ApplicationDbContext db)
        {
            var roles = new Mock<IUserRoleAssignmentService>();
            roles.Setup(r => r.GetRoleIdsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((int id, CancellationToken _) => id switch
                {
                    Pm or SubPm or OtherPm => new List<int> { (int)UserGroups.PortfolioManager },
                    Admin => new List<int> { (int)UserGroups.RridaAdmin },
                    ProfileAdmin => new List<int> { (int)UserGroups.ProfileAdmin },
                    _ => new List<int> { (int)UserGroups.TeamManager }
                });

            var engine = new Mock<IRridaEngineContextFactory>();
            engine.Setup(e => e.Context).Returns(Ctx.Value);

            return new PortfolioViewService(db, roles.Object, engine.Object);
        }

        private static string EngineRoot([CallerFilePath] string source = "")
        {
            for (var dir = new DirectoryInfo(Path.GetDirectoryName(source)!); dir is not null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "external", "RRIDA-Engine");
                if (Directory.Exists(candidate)) return candidate;
            }
            throw new DirectoryNotFoundException("external/RRIDA-Engine not found");
        }
    }
}
