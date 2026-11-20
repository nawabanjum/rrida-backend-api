using CloverBackoffice.Repository.Implementation;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Implementation;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static CloverBackoffice.Tests.TeamHealth.MilestoneTestData;

namespace CloverBackoffice.Tests.TeamHealth
{
    /// <summary>ERAN Coach chat on Team Health is team-specific: no team sees another's chat.</summary>
    public class EranCoachTeamChatTests
    {
        private const int Manager = 150;
        private const long TeamA = 217;
        private const long TeamB = 231;

        private readonly string _db = Guid.NewGuid().ToString();

        private EranCoachConversationService Service(ApplicationDbContext db) =>
            new(new EranCoachConversationRepository(db));

        private async Task<int> StartChatAsync(long? team, string message)
        {
            using var db = CreateContext(_db);
            var turn = await Service(db).BeginTurnAsync(Manager, 69, 2, team, null, message);
            return turn!.ConversationId;
        }

        [Fact]
        public async Task List_with_team_returns_only_that_teams_conversations()
        {
            var a = await StartChatAsync(TeamA, "About team A");
            var b = await StartChatAsync(TeamB, "About team B");

            using var db = CreateContext(_db);
            var listA = await Service(db).ListAsync(Manager, TeamA);
            var listB = await Service(db).ListAsync(Manager, TeamB);

            Assert.Equal(new[] { a }, listA.Select(c => c.Id));
            Assert.Equal(TeamA, listA[0].UnitId);
            Assert.Equal(new[] { b }, listB.Select(c => c.Id));
        }

        [Fact]
        public async Task List_without_team_still_returns_everything()
        {
            await StartChatAsync(TeamA, "About team A");
            await StartChatAsync(TeamB, "About team B");

            using var db = CreateContext(_db);
            Assert.Equal(2, (await Service(db).ListAsync(Manager, null)).Count);
        }

        [Fact]
        public async Task Messages_of_another_teams_conversation_are_not_returned()
        {
            var a = await StartChatAsync(TeamA, "About team A");

            using var db = CreateContext(_db);
            Assert.Null(await Service(db).GetMessagesAsync(Manager, a, TeamB));

            // Switching back to team A shows its history again.
            var back = await Service(db).GetMessagesAsync(Manager, a, TeamA);
            Assert.Equal("About team A", Assert.Single(back!).Content);
        }

        [Fact]
        public async Task Continuing_a_conversation_under_another_team_is_refused_and_stores_nothing()
        {
            var a = await StartChatAsync(TeamA, "About team A");

            using (var db = CreateContext(_db))
            {
                var turn = await Service(db).BeginTurnAsync(Manager, 69, 2, TeamB, a, "Now about team B");
                Assert.True(turn!.TeamMismatch);
            }

            using var check = CreateContext(_db);
            Assert.Equal(1, await check.EranCoachMessages.CountAsync(m => m.ConversationId == a));
        }

        [Fact]
        public async Task Continuing_a_conversation_under_its_own_team_appends()
        {
            var a = await StartChatAsync(TeamA, "About team A");

            using (var db = CreateContext(_db))
            {
                var turn = await Service(db).BeginTurnAsync(Manager, 69, 2, TeamA, a, "More about team A");
                Assert.False(turn!.TeamMismatch);
                Assert.Equal(a, turn.ConversationId);
            }

            using var check = CreateContext(_db);
            Assert.Equal(2, await check.EranCoachMessages.CountAsync(m => m.ConversationId == a));
        }

        [Fact]
        public async Task Coach_memory_is_kept_per_team()
        {
            using (var db = CreateContext(_db))
            {
                var repo = new EranCoachMemoryRepository(db);
                await repo.UpsertAsync(Manager, TeamA, "Team A: two leavers");
                await repo.UpsertAsync(Manager, TeamB, "Team B: new hire");
            }

            using var check = CreateContext(_db);
            var read = new EranCoachMemoryRepository(check);
            Assert.Equal("Team A: two leavers", (await read.GetByManagerAsync(Manager, TeamA))!.SummaryText);
            Assert.Equal("Team B: new hire", (await read.GetByManagerAsync(Manager, TeamB))!.SummaryText);
            Assert.Null(await read.GetByManagerAsync(Manager, null));
        }
    }
}
