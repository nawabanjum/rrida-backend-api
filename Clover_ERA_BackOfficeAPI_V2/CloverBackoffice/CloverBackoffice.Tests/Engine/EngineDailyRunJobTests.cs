using CloverBackoffice.Infrastructure.Models.DeterministicLayer;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Implementation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace CloverBackoffice.Tests.Engine
{
    /// <summary>
    /// Which teams the nightly engine run replays: enabled teams whose clock has started and
    /// that someone has enrolled in. On Dev every team had settings, and 268 of 285 had nobody
    /// in them; each would still have written a calibrating message a day for Eran to phrase.
    /// </summary>
    public class EngineDailyRunJobTests
    {
        private static readonly DateOnly Through = new(2026, 10, 22);

        [Fact]
        public async Task Only_enabled_started_teams_that_someone_has_enrolled_in_are_run()
        {
            await using var db = NewDb();
            Team(db, 329, enabled: true, start: Through.AddDays(-7), members: 1);      // run
            Team(db, 2, enabled: true, start: Through.AddDays(-22), members: 0);       // nobody enrolled: skipped
            Team(db, 330, enabled: false, start: Through.AddDays(-7), members: 1);     // switched off: skipped
            Team(db, 331, enabled: true, start: Through.AddDays(1), members: 1);       // clock not started: skipped
            await db.SaveChangesAsync();

            var teams = await EngineDailyRunJob.TeamsToRunAsync(db, Through);

            Assert.Equal(new uint[] { 329 }, teams);
        }

        [Fact]
        public async Task A_team_whose_members_have_all_left_keeps_running()
        {
            await using var db = NewDb();
            Team(db, 314, enabled: true, start: Through.AddDays(-30), members: 1, left: Through.AddDays(-3));
            await db.SaveChangesAsync();

            Assert.Equal(new uint[] { 314 }, await EngineDailyRunJob.TeamsToRunAsync(db, Through));
        }

        [Fact]
        public async Task Teams_are_run_in_id_order()
        {
            await using var db = NewDb();
            Team(db, 330, enabled: true, start: Through.AddDays(-7), members: 1);
            Team(db, 307, enabled: true, start: Through.AddDays(-7), members: 2);
            Team(db, 329, enabled: true, start: Through.AddDays(-7), members: 1);
            await db.SaveChangesAsync();

            Assert.Equal(new uint[] { 307, 329, 330 }, await EngineDailyRunJob.TeamsToRunAsync(db, Through));
        }

        private static long _nextMember = 1;

        private static void Team(ApplicationDbContext db, uint unit, bool enabled, DateOnly start, int members, DateOnly? left = null)
        {
            db.EngineTeamSettings.Add(new EngineTeamSettings
            {
                BusinessUnitId = unit, StartDate = start, WorkingWeek = "mon-fri", IsEnabled = enabled, CreatedAt = DateTime.UtcNow
            });
            for (var i = 0; i < members; i++)
            {
                var member = new EngineMember
                {
                    Id = Interlocked.Increment(ref _nextMember), ProfileId = 1, TokenHash = $"hash-{unit}-{i}",
                    EngineMemberKey = $"m-{unit}-{i}", CreatedAt = DateTime.UtcNow
                };
                db.EngineMembers.Add(member);
                db.EngineTeamMemberships.Add(new EngineTeamMembership
                {
                    MemberId = member.Id, BusinessUnitId = unit, EffectiveFrom = start, EffectiveTo = left, CreatedAt = DateTime.UtcNow
                });
            }
        }

        private static ApplicationDbContext NewDb() =>
            new TestApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"engine-run-{Guid.NewGuid()}")
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);
    }
}
