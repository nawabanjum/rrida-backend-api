using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO.TeamHealth;
using CloverBackoffice.Infrastructure.Models.TeamHealth;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Implementation;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static CloverBackoffice.Tests.TeamHealth.MilestoneTestData;

namespace CloverBackoffice.Tests.TeamHealth
{
    /// <summary>Unit tests for TeamMilestoneService: validation, dates, permissions, persistence.</summary>
    public class TeamMilestoneServiceTests
    {
        private readonly string _db = Guid.NewGuid().ToString();
        private readonly SaveTracker _tracker = new();

        public TeamMilestoneServiceTests()
        {
            using var db = CreateContext(_db);
            Seed(db);
        }

        private (TeamMilestoneService Service, ApplicationDbContext Db) Create()
        {
            var db = CreateContext(_db, _tracker);
            return (new TeamMilestoneService(db, new TeamHealthAccessService(db, Roles()), new FixedClock(Today)), db);
        }

        private static UpsertTeamMilestoneDto Body(string? title = "New leader joined",
            string? description = "A new team leader joined the Support Desk team.",
            string? date = "2026-09-29", uint unitId = Team, uint id = 0) =>
            new() { Id = id, UnitId = unitId, Title = title, Description = description, Date = date };

        // ── Happy path ──────────────────────────────────────────────────────

        [Fact]
        public async Task Create_ValidRequest_PersistsAndReturnsMilestone()
        {
            var (svc, db) = Create();

            var result = await svc.UpsertAsync(Body(), ProfileAdminA);

            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Data);
            Assert.True(result.Data!.Id > 0);
            Assert.Equal("New leader joined", result.Data.Title);
            Assert.Equal("A new team leader joined the Support Desk team.", result.Data.Description);
            Assert.Equal("2026-09-29", result.Data.Date);
            Assert.Equal(Team, result.Data.UnitId);

            var row = await CreateContext(_db).TeamMilestones.SingleAsync();
            Assert.Equal(Team, row.UnitId);
            Assert.Equal(ProfileA, row.ProfileId);
            Assert.Equal(new DateOnly(2026, 9, 29), row.MilestoneDate);
            Assert.Equal(ProfileAdminA, row.CreatedBy);
            Assert.False(row.IsDel);
        }

        [Fact]
        public async Task Create_TrimsTitleAndDescription()
        {
            var (svc, _) = Create();

            var result = await svc.UpsertAsync(Body(title: "  New system  ", description: "  CRM went live. "), ProfileAdminA);

            Assert.True(result.IsSuccess);
            Assert.Equal("New system", result.Data!.Title);
            Assert.Equal("CRM went live.", result.Data.Description);
        }

        [Fact]
        public async Task Create_TitleExactly40Characters_IsAccepted()
        {
            var (svc, _) = Create();

            var result = await svc.UpsertAsync(Body(title: new string('a', 40)), ProfileAdminA);

            Assert.True(result.IsSuccess);
        }

        [Fact]
        public async Task Create_PastDate_IsAccepted()
        {
            var (svc, _) = Create();

            var result = await svc.UpsertAsync(Body(date: "2024-01-15"), ProfileAdminA);

            Assert.True(result.IsSuccess);
            Assert.Equal("2024-01-15", result.Data!.Date);
        }

        [Theory]
        [InlineData("29-09-2026", "2026-09-29")]
        [InlineData("01-07-2026", "2026-07-01")]
        [InlineData("29/09/2026", "2026-09-29")]
        [InlineData("2026/07/01", "2026-07-01")]
        [InlineData(" 2026-07-01 ", "2026-07-01")]
        public async Task Create_NormalisesDateToIso(string input, string expected)
        {
            var (svc, _) = Create();

            var result = await svc.UpsertAsync(Body(date: input), ProfileAdminA);

            Assert.True(result.IsSuccess);
            Assert.Equal(expected, result.Data!.Date);
            var row = await CreateContext(_db).TeamMilestones.SingleAsync();
            Assert.Equal(DateOnly.Parse(expected), row.MilestoneDate);
        }

        [Fact]
        public async Task Create_DoesNotWriteAnythingButTheMilestone()
        {
            var (svc, _) = Create();
            _tracker.Writes.Clear();

            var result = await svc.UpsertAsync(Body(date: "2026-06-30"), ProfileAdminA);

            Assert.True(result.IsSuccess);
            var write = Assert.Single(_tracker.Writes);
            Assert.Equal(typeof(TeamMilestone), write.Type);
            Assert.Equal(EntityState.Added, write.State);
        }

        // ── Validation (400) ────────────────────────────────────────────────

        [Theory]
        [InlineData(null, ResponseMessages.Errors.MilestoneTitleRequired)]
        [InlineData("", ResponseMessages.Errors.MilestoneTitleRequired)]
        [InlineData("   ", ResponseMessages.Errors.MilestoneTitleRequired)]
        [InlineData("This title is far too long to fit in forty", ResponseMessages.Errors.MilestoneTitleTooLong)]
        public async Task Create_InvalidTitle_IsBadRequest(string? title, string message)
        {
            var (svc, _) = Create();

            var result = await svc.UpsertAsync(Body(title: title), ProfileAdminA);

            AssertBadRequest(result, message);
        }

        [Theory]
        [InlineData(null, ResponseMessages.Errors.MilestoneDescriptionRequired)]
        [InlineData("", ResponseMessages.Errors.MilestoneDescriptionRequired)]
        [InlineData("  ", ResponseMessages.Errors.MilestoneDescriptionRequired)]
        public async Task Create_MissingDescription_IsBadRequest(string? description, string message)
        {
            var (svc, _) = Create();

            var result = await svc.UpsertAsync(Body(description: description), ProfileAdminA);

            AssertBadRequest(result, message);
        }

        [Fact]
        public async Task Create_DescriptionOver200_IsBadRequest()
        {
            var (svc, _) = Create();

            var result = await svc.UpsertAsync(Body(description: new string('d', 201)), ProfileAdminA);

            AssertBadRequest(result, ResponseMessages.Errors.MilestoneDescriptionTooLong);
        }

        [Theory]
        [InlineData(null, ResponseMessages.Errors.MilestoneDateRequired)]
        [InlineData("", ResponseMessages.Errors.MilestoneDateRequired)]
        [InlineData("not-a-date", ResponseMessages.Errors.MilestoneDateInvalid)]
        [InlineData("31-02-2026", ResponseMessages.Errors.MilestoneDateInvalid)]
        [InlineData("2026-13-01", ResponseMessages.Errors.MilestoneDateInvalid)]
        [InlineData("09-29-2026", ResponseMessages.Errors.MilestoneDateInvalid)]
        [InlineData("2026-09-29T10:00:00", ResponseMessages.Errors.MilestoneDateInvalid)]
        [InlineData("2026-10-15", ResponseMessages.Errors.MilestoneDateInFuture)]
        public async Task Create_InvalidDate_IsBadRequest(string? date, string message)
        {
            var (svc, _) = Create();

            var result = await svc.UpsertAsync(Body(date: date), ProfileAdminA);

            AssertBadRequest(result, message);
        }

        [Fact]
        public async Task Create_NullBody_IsBadRequest()
        {
            var (svc, _) = Create();

            var result = await svc.UpsertAsync(null, ProfileAdminA);

            AssertBadRequest(result, ResponseMessages.Errors.MilestoneUnitRequired);
        }

        [Fact]
        public async Task Create_MissingUnitId_IsBadRequest()
        {
            var (svc, _) = Create();

            var result = await svc.UpsertAsync(Body(unitId: 0), ProfileAdminA);

            AssertBadRequest(result, ResponseMessages.Errors.MilestoneUnitRequired);
        }

        [Fact]
        public async Task Create_InvalidRequest_SavesNothing()
        {
            var (svc, _) = Create();

            await svc.UpsertAsync(Body(title: new string('x', 41)), ProfileAdminA);

            Assert.Empty(await CreateContext(_db).TeamMilestones.ToListAsync());
        }

        // ── Team lookup (404) ───────────────────────────────────────────────

        [Theory]
        [InlineData(9999u)]
        [InlineData(DeletedTeam)]
        public async Task Create_UnknownOrDeletedTeam_IsNotFound(uint unitId)
        {
            var (svc, _) = Create();

            var result = await svc.UpsertAsync(Body(unitId: unitId), RridaAdmin);

            Assert.Equal(TeamMilestoneError.NotFound, result.Error);
        }

        // ── Permissions (403) ───────────────────────────────────────────────

        [Theory]
        [InlineData(RridaAdmin)]
        [InlineData(ProfileAdminA)]
        [InlineData(ManagerOfTeam)]
        [InlineData(ManagerOfParent)]
        [InlineData(PortfolioManagerOfTeam)]
        [InlineData(SeniorPortfolioManager)]
        public async Task Create_AllowedRoles_Succeed(int userId)
        {
            var (svc, _) = Create();

            var result = await svc.UpsertAsync(Body(), userId);

            Assert.True(result.IsSuccess, result.Message);
        }

        [Theory]
        [InlineData(UnassignedManager)]
        [InlineData(PortfolioManagerNoReports)]
        [InlineData(FormerPortfolioManager)]
        [InlineData(ProfileAdminB)]
        [InlineData(12345)]
        public async Task Create_NotPermitted_IsForbidden(int userId)
        {
            var (svc, _) = Create();

            var result = await svc.UpsertAsync(Body(), userId);

            Assert.Equal(TeamMilestoneError.Forbidden, result.Error);
            Assert.Empty(await CreateContext(_db).TeamMilestones.ToListAsync());
        }

        [Fact]
        public async Task Create_PortfolioManagerOnTeamOutsideTheirLine_IsForbidden()
        {
            var (svc, _) = Create();

            var result = await svc.UpsertAsync(Body(unitId: OtherTeamA), PortfolioManagerOfTeam);

            Assert.Equal(TeamMilestoneError.Forbidden, result.Error);
        }

        [Fact]
        public async Task Create_TeamManagerOnAnotherTeam_IsForbidden()
        {
            var (svc, _) = Create();

            var result = await svc.UpsertAsync(Body(unitId: OtherTeamA), ManagerOfTeam);

            Assert.Equal(TeamMilestoneError.Forbidden, result.Error);
        }

        // ── Edit (upsert with id) ───────────────────────────────────────────

        [Fact]
        public async Task Upsert_WithId_UpdatesExistingMilestone()
        {
            var (svc, _) = Create();
            var created = await svc.UpsertAsync(Body(date: "2026-06-30"), ProfileAdminA);

            var (svc2, _) = Create();
            var updated = await svc2.UpsertAsync(Body(title: "Two leavers", description: "Team went 9 to 7.",
                date: "07-07-2026", id: created.Data!.Id), ManagerOfTeam);

            Assert.True(updated.IsSuccess, updated.Message);
            Assert.Equal(created.Data.Id, updated.Data!.Id);
            Assert.Equal("Two leavers", updated.Data.Title);
            Assert.Equal("2026-07-07", updated.Data.Date);

            var row = await CreateContext(_db).TeamMilestones.SingleAsync();
            Assert.Equal("Team went 9 to 7.", row.Description);
            Assert.Equal(new DateOnly(2026, 7, 7), row.MilestoneDate);
            Assert.Equal(ProfileAdminA, row.CreatedBy);
            Assert.Equal(ManagerOfTeam, row.UpdatedBy);
        }

        [Fact]
        public async Task Upsert_Edit_WritesOnlyThatMilestone()
        {
            var (svc, _) = Create();
            var created = await svc.UpsertAsync(Body(), ProfileAdminA);
            _tracker.Writes.Clear();

            var (svc2, _) = Create();
            await svc2.UpsertAsync(Body(title: "Edited", id: created.Data!.Id), ProfileAdminA);

            var write = Assert.Single(_tracker.Writes);
            Assert.Equal(typeof(TeamMilestone), write.Type);
            Assert.Equal(EntityState.Modified, write.State);
        }

        [Fact]
        public async Task Upsert_UnknownId_IsNotFound()
        {
            var (svc, _) = Create();

            var result = await svc.UpsertAsync(Body(id: 9999), ProfileAdminA);

            Assert.Equal(TeamMilestoneError.NotFound, result.Error);
            Assert.Empty(await CreateContext(_db).TeamMilestones.ToListAsync());
        }

        [Fact]
        public async Task Upsert_IdOfAnotherTeamsMilestone_IsNotFound_AndUnchanged()
        {
            using (var seed = CreateContext(_db))
                AddMilestone(seed, OtherTeamA, "2026-07-01", "Other team");
            var otherId = (await CreateContext(_db).TeamMilestones.SingleAsync()).Id;
            var (svc, _) = Create();

            // Allowed on 294, tries to edit a milestone of 295 by its id.
            var result = await svc.UpsertAsync(Body(title: "Hijack", id: otherId), ManagerOfTeam);

            Assert.Equal(TeamMilestoneError.NotFound, result.Error);
            Assert.Equal("Other team", (await CreateContext(_db).TeamMilestones.SingleAsync()).Title);
        }

        [Fact]
        public async Task Upsert_DeletedMilestone_IsNotFound()
        {
            using (var seed = CreateContext(_db))
                AddMilestone(seed, Team, "2026-07-01", "Removed", isDel: true);
            var id = (await CreateContext(_db).TeamMilestones.SingleAsync()).Id;
            var (svc, _) = Create();

            var result = await svc.UpsertAsync(Body(id: id), ProfileAdminA);

            Assert.Equal(TeamMilestoneError.NotFound, result.Error);
        }

        [Fact]
        public async Task Upsert_Edit_InvalidField_IsBadRequest_AndUnchanged()
        {
            var (svc, _) = Create();
            var created = await svc.UpsertAsync(Body(), ProfileAdminA);

            var (svc2, _) = Create();
            var result = await svc2.UpsertAsync(Body(title: new string('x', 41), id: created.Data!.Id), ProfileAdminA);

            Assert.Equal(TeamMilestoneError.BadRequest, result.Error);
            Assert.Equal("New leader joined", (await CreateContext(_db).TeamMilestones.SingleAsync()).Title);
        }

        [Fact]
        public async Task Upsert_Edit_NotPermitted_IsForbidden()
        {
            var (svc, _) = Create();
            var created = await svc.UpsertAsync(Body(), ProfileAdminA);

            var (svc2, _) = Create();
            var result = await svc2.UpsertAsync(Body(title: "Edited", id: created.Data!.Id), PortfolioManagerNoReports);

            Assert.Equal(TeamMilestoneError.Forbidden, result.Error);
        }

        // ── Timeline read ───────────────────────────────────────────────────

        [Fact]
        public async Task GetForTeam_ReturnsTeamMilestonesOldestFirst_ExcludingDeletedAndOtherTeams()
        {
            using (var seed = CreateContext(_db))
            {
                AddMilestone(seed, Team, "2026-09-10", "Second");
                AddMilestone(seed, Team, "2026-06-30", "First");
                AddMilestone(seed, Team, "2026-08-01", "Removed", isDel: true);
                AddMilestone(seed, OtherTeamA, "2026-07-01", "Other team");
            }
            var (svc, _) = Create();

            var result = await svc.GetForTeamAsync(Team, null, null, ManagerOfTeam);

            Assert.True(result.IsSuccess);
            Assert.Equal(new[] { "First", "Second" }, result.Data!.Select(m => m.Title));
            Assert.Equal("2026-06-30", result.Data[0].Date);
        }

        [Fact]
        public async Task GetForTeam_FiltersByInclusiveDateRange()
        {
            using (var seed = CreateContext(_db))
            {
                AddMilestone(seed, Team, "2026-08-29", "Before");
                AddMilestone(seed, Team, "2026-08-30", "Start");
                AddMilestone(seed, Team, "2026-09-29", "End");
            }
            var (svc, _) = Create();

            var result = await svc.GetForTeamAsync(Team, "30-08-2026", "2026-09-29", ProfileAdminA);

            Assert.Equal(new[] { "Start", "End" }, result.Data!.Select(m => m.Title));
        }

        [Fact]
        public async Task GetForTeam_CreatedMilestoneAppearsOnTimeline()
        {
            var (svc, _) = Create();
            var created = await svc.UpsertAsync(Body(date: "2026-07-07"), ManagerOfTeam);

            var (svc2, _) = Create();
            var list = await svc2.GetForTeamAsync(Team, null, null, ManagerOfTeam);

            var item = Assert.Single(list.Data!);
            Assert.Equal(created.Data!.Id, item.Id);
            Assert.Equal("2026-07-07", item.Date);
        }

        [Theory]
        [InlineData("bad", null)]
        [InlineData(null, "bad")]
        [InlineData("2026-09-29", "2026-09-01")]
        public async Task GetForTeam_InvalidRange_IsBadRequest(string? from, string? to)
        {
            var (svc, _) = Create();

            var result = await svc.GetForTeamAsync(Team, from, to, ProfileAdminA);

            Assert.Equal(TeamMilestoneError.BadRequest, result.Error);
        }

        [Fact]
        public async Task GetForTeam_NotPermitted_IsForbidden()
        {
            var (svc, _) = Create();

            var result = await svc.GetForTeamAsync(Team, null, null, ProfileAdminB);

            Assert.Equal(TeamMilestoneError.Forbidden, result.Error);
        }

        private static void AssertBadRequest<T>(TeamMilestoneResult<T> result, string message)
        {
            Assert.False(result.IsSuccess);
            Assert.Equal(TeamMilestoneError.BadRequest, result.Error);
            Assert.Equal(message, result.Message);
        }
    }
}
