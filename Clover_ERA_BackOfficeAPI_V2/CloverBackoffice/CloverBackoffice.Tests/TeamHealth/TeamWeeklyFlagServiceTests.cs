using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO.TeamHealth;
using CloverBackoffice.Infrastructure.Models.TeamHealth;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Implementation;
using Xunit;
using static CloverBackoffice.Tests.TeamHealth.MilestoneTestData;

namespace CloverBackoffice.Tests.TeamHealth
{
    /// <summary>Unit tests for TeamWeeklyFlagService (read only).</summary>
    public class TeamWeeklyFlagServiceTests
    {
        private readonly string _db = Guid.NewGuid().ToString();
        private readonly SaveTracker _tracker = new();

        public TeamWeeklyFlagServiceTests()
        {
            using var db = CreateContext(_db);
            Seed(db);
            AddFlag(db, Team, "2026-08-11", "Growth reads as blocked.", "Protect time.");
            AddFlag(db, Team, "2026-08-25", "Several members reflected not feeling acknowledged for their efforts.",
                "Name the win from the last quarter out loud at your next team meeting.", cycleId: 42);
            AddFlag(db, Team, "2026-08-18", "Two members described the new system adding steps.", "Ask in one-to-ones.");
            AddFlag(db, Team, "2026-08-04", "Removed flag", "x", isDel: true);
            AddFlag(db, OtherTeamA, "2026-08-25", "Other team", "x");
        }

        internal static void AddFlag(ApplicationDbContext db, uint unitId, string start, string whatCameUp, string action, bool isDel = false, long? cycleId = null)
        {
            var s = DateOnly.Parse(start);
            db.TeamWeeklyFlags.Add(new TeamWeeklyFlag
            {
                ProfileId = unitId == TeamB ? ProfileB : ProfileA,
                UnitId = unitId,
                WeekStartDate = s,
                WeekEndDate = s.AddDays(6),
                WhatCameUp = whatCameUp,
                SuggestedAction = action,
                Source = "Test",
                CycleId = cycleId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                IsDel = isDel
            });
            db.SaveChanges();
        }

        private TeamWeeklyFlagService Service()
        {
            var db = CreateContext(_db, _tracker);
            return new TeamWeeklyFlagService(db, new TeamHealthAccessService(db, Roles()));
        }

        [Fact]
        public async Task AllWeeks_LatestFirst_WithLabelsAndContent()
        {
            var r = await Service().GetAsync("294", null, ManagerOfTeam);

            Assert.True(r.IsSuccess, r.Message);
            Assert.Equal("294", r.Data!.TeamId);
            Assert.Equal(new[] { "2026-08-25", "2026-08-18", "2026-08-11" }, r.Data.WeeklyFlags.Select(f => f.WeekStartDate));

            var latest = r.Data.WeeklyFlags[0];
            Assert.Equal("2026-08-31", latest.WeekEndDate);
            Assert.Equal("Week of 25 Aug", latest.Label);
            Assert.Equal("Several members reflected not feeling acknowledged for their efforts.", latest.WhatCameUp);
            Assert.Equal("Name the win from the last quarter out loud at your next team meeting.", latest.SuggestedAction);
            Assert.True(latest.Id > 0);

            // The cycle behind the week's action; a week the engine did not tie to one has none.
            Assert.Equal(42, latest.CycleId);
            Assert.Null(r.Data.WeeklyFlags[1].CycleId);
        }

        [Theory]
        [InlineData("2026-08-25", "2026-08-25")]
        [InlineData("25-08-2026", "2026-08-25")]
        [InlineData("2026-08-28", "2026-08-25")] // any day inside the week
        [InlineData("2026-08-18", "2026-08-18")]
        public async Task SpecificWeek_ReturnsThatFlagOnly(string week, string expectedStart)
        {
            var r = await Service().GetAsync("294", week, ProfileAdminA);

            var flag = Assert.Single(r.Data!.WeeklyFlags);
            Assert.Equal(expectedStart, flag.WeekStartDate);
        }

        [Theory]
        [InlineData("2026-09-08")]  // no flag that week
        [InlineData("2026-08-04")]  // only a deleted flag that week
        public async Task WeekWithoutFlag_IsSuccessWithEmptyList(string week)
        {
            var r = await Service().GetAsync("294", week, ProfileAdminA);

            Assert.True(r.IsSuccess);
            Assert.Empty(r.Data!.WeeklyFlags);
        }

        [Fact]
        public async Task TeamWithNoFlags_IsSuccessWithEmptyList()
        {
            var r = await Service().GetAsync("290", null, RridaAdmin);

            Assert.True(r.IsSuccess);
            Assert.Equal("290", r.Data!.TeamId);
            Assert.Empty(r.Data.WeeklyFlags);
        }

        [Fact]
        public async Task ReadsNeverWrite()
        {
            await Service().GetAsync("294", null, ProfileAdminA);
            await Service().GetAsync("294", "2026-08-25", ProfileAdminA);

            Assert.Empty(_tracker.Writes);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("team-123")]
        [InlineData("0")]
        [InlineData("-1")]
        public async Task InvalidTeamId_IsBadRequest(string? teamId)
        {
            var r = await Service().GetAsync(teamId, null, ProfileAdminA);

            Assert.Equal(TeamMilestoneError.BadRequest, r.Error);
            Assert.Equal(ResponseMessages.Errors.TimelineTeamIdInvalid, r.Message);
        }

        [Theory]
        [InlineData("")]
        [InlineData("last week")]
        [InlineData("2026-02-30")]
        public async Task InvalidWeek_IsBadRequest(string week)
        {
            var r = await Service().GetAsync("294", week, ProfileAdminA);

            Assert.Equal(TeamMilestoneError.BadRequest, r.Error);
            Assert.Equal(ResponseMessages.Errors.WeeklyFlagWeekInvalid, r.Message);
        }

        [Theory]
        [InlineData("9999")]
        [InlineData("296")] // soft-deleted team
        public async Task UnknownTeam_IsNotFound(string teamId)
        {
            Assert.Equal(TeamMilestoneError.NotFound, (await Service().GetAsync(teamId, null, RridaAdmin)).Error);
        }

        [Theory]
        [InlineData(ProfileAdminB)]
        [InlineData(PortfolioManagerNoReports)]
        [InlineData(UnassignedManager)]
        public async Task NotPermitted_IsForbidden(int userId)
        {
            Assert.Equal(TeamMilestoneError.Forbidden, (await Service().GetAsync("294", null, userId)).Error);
        }

        [Theory]
        [InlineData(RridaAdmin)]
        [InlineData(ManagerOfParent)]
        [InlineData(PortfolioManagerOfTeam)]
        public async Task PermittedRoles_Succeed(int userId)
        {
            Assert.True((await Service().GetAsync("294", null, userId)).IsSuccess);
        }
    }
}
