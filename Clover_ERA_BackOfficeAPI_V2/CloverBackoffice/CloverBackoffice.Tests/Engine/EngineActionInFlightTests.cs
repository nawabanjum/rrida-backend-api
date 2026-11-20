using System.Runtime.CompilerServices;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.DTO.TeamHealth;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Infrastructure.Models.DeterministicLayer;
using CloverBackoffice.Infrastructure.Models.TeamHealth;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Implementation;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Rrida.Engine;
using Xunit;

namespace CloverBackoffice.Tests.Engine
{
    /// <summary>
    /// The Weekly Flag's move becomes an Action in Flight (a dot on the Team Health timeline)
    /// only when a manager taps "I'll do it" (POST api/rrida/manager/acknowledge), or "I did
    /// this" (confirm) without it. Delivering the move makes no dot.
    /// </summary>
    public class EngineActionInFlightTests
    {
        private const uint Unit = 9201;
        private static readonly User Manager = new() { UserId = 11 };
        private static readonly User OtherManager = new() { UserId = 12 };
        private static readonly Lazy<EngineContext> Ctx = new(() => EngineContext.FromKit(Path.Combine(EngineRoot(), "kit", "data")));

        [Fact]
        public async Task A_delivered_move_has_no_dot_until_it_is_taken_up()
        {
            await using var db = NewDb();
            var actionId = await SeedMove(db, "team");

            Assert.Empty(await Dots(db));

            await ManagerService(db).AcknowledgeAsync(Manager, Ack("C1"));

            var dot = Assert.Single(await Dots(db));
            Assert.Equal(actionId, dot.ActionId);
            Assert.Equal(await db.EngineCycles.Select(c => c.Id).SingleAsync(), dot.CycleId);
            Assert.Equal(1, dot.Rung);
            Assert.Equal(Manager.UserId, dot.AcknowledgedBy!.UserId);
            Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"), dot.Date);
            Assert.Equal(TeamActionStatuses.InFlight, dot.Status);
        }

        [Fact]
        public async Task Repeated_taps_by_any_manager_make_one_dot()
        {
            await using var db = NewDb();
            await SeedMove(db, "team");
            var manager = ManagerService(db);

            var first = await manager.AcknowledgeAsync(Manager, Ack("C1"));
            await manager.AcknowledgeAsync(Manager, Ack("C1"));
            var third = await manager.AcknowledgeAsync(OtherManager, Ack("C1"));

            Assert.Equal(first!.AcknowledgedAt, third!.AcknowledgedAt);
            Assert.Single(db.EngineAcknowledgements);
            var dot = Assert.Single(await Dots(db));
            Assert.Equal(Manager.UserId, dot.AcknowledgedBy!.UserId);   // the first to take it up
        }

        [Fact]
        public async Task A_refused_tap_makes_no_dot()
        {
            await using var db = NewDb();
            await SeedMove(db, "team");
            var manager = ManagerService(db);

            // Not the move in front of the team.
            await Assert.ThrowsAsync<InvalidOperationException>(() => manager.AcknowledgeAsync(Manager, Ack("C9")));
            // A team this manager may not see.
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                ManagerService(db, accessible: []).AcknowledgeAsync(Manager, Ack("C1")));

            Assert.Empty(db.EngineAcknowledgements);
            Assert.Empty(await Dots(db));
        }

        [Fact]
        public async Task I_did_this_without_ill_do_it_takes_the_move_up_and_makes_its_dot()
        {
            await using var db = NewDb();
            await SeedMove(db, "team");
            var manager = ManagerService(db);

            var after = await manager.ConfirmAsync(OtherManager,
                new EngineConfirmRequestDto { BusinessUnitId = Unit, CycleKey = "C1", WorkedIfMet = true });

            Assert.True(after!.IsConfirmed);
            Assert.True(after.IsAcknowledged);
            var confirmation = Assert.Single(db.EngineConfirmations);
            Assert.Equal(OtherManager.UserId, confirmation.ConfirmedByUserId);
            var dot = Assert.Single(await Dots(db));
            Assert.Equal(OtherManager.UserId, dot.AcknowledgedBy!.UserId);

            // The rest of the confirm workflow is as it was: no second confirm, no acknowledge after it.
            await Assert.ThrowsAsync<InvalidOperationException>(() => manager.ConfirmAsync(Manager,
                new EngineConfirmRequestDto { BusinessUnitId = Unit, CycleKey = "C1", WorkedIfMet = false }));
            await Assert.ThrowsAsync<InvalidOperationException>(() => manager.AcknowledgeAsync(Manager, Ack("C1")));
            Assert.Single(db.EngineAcknowledgements);
        }

        [Fact]
        public async Task I_did_this_after_ill_do_it_keeps_the_original_acknowledgement()
        {
            await using var db = NewDb();
            await SeedMove(db, "team");
            var manager = ManagerService(db);

            var acknowledged = await manager.AcknowledgeAsync(Manager, Ack("C1"));
            await manager.ConfirmAsync(OtherManager,
                new EngineConfirmRequestDto { BusinessUnitId = Unit, CycleKey = "C1", WorkedIfMet = true });

            var row = Assert.Single(db.EngineAcknowledgements);
            Assert.Equal(Manager.UserId, row.AcknowledgedByUserId);
            Assert.Equal(acknowledged!.AcknowledgedAt, row.AcknowledgedAt);
            Assert.Single(await Dots(db));
        }

        [Fact]
        public async Task A_decline_makes_no_dot()
        {
            await using var db = NewDb();
            await SeedMove(db, "content");

            var front = await ManagerService(db).DeclineAsync(Manager,
                new EngineDeclineRequestDto { BusinessUnitId = Unit, CycleKey = "C1", Reason = "One of us is on a plan." });

            Assert.Null(front);
            Assert.Single(db.EngineDeclines);
            Assert.Empty(db.EngineAcknowledgements);
            Assert.Empty(await Dots(db));
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private static EngineAcknowledgeRequestDto Ack(string cycleKey) => new() { BusinessUnitId = Unit, CycleKey = cycleKey };

        private static Task<List<TimelineActionDto>> Dots(ApplicationDbContext db) =>
            TeamActionService.LoadTimelineActionsAsync(db, Unit, DateOnly.MinValue, DateOnly.MaxValue, default);

        // One open cycle delivered on day 8, its confirmation due on day 11; today is day 21.
        private static async Task<long> SeedMove(ApplicationDbContext db, string route)
        {
            db.EngineTeamSettings.Add(new EngineTeamSettings
            {
                BusinessUnitId = Unit, StartDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-20), WorkingWeek = "every-day", IsEnabled = true
            });
            var cycle = new EngineCycle { BusinessUnitId = Unit, CycleKey = "C1", CycleIndex = 1, IssueFileKey = "MA-E-002", Issue = "Blockers", DeliveredDay = 8, ConfirmationDueDay = 11, SignalReadDay = 15 };
            db.EngineCycles.Add(cycle);
            await db.SaveChangesAsync();
            var delivery = new EngineCycleDelivery
            {
                CycleId = cycle.Id, Rung = 1, ActionKey = "MA-E-002-22", Route = route, DeliveredDay = 8, DeliveredText = "Ask what got in the way."
            };
            db.EngineCycleDeliveries.Add(delivery);
            await db.SaveChangesAsync();
            return delivery.Id;
        }

        private static ApplicationDbContext NewDb() =>
            new TestApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"action-in-flight-{Guid.NewGuid()}")
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);

        private static EngineManagerService ManagerService(ApplicationDbContext db, long[]? accessible = null)
        {
            var engine = new Mock<IRridaEngineContextFactory>();
            engine.Setup(e => e.Context).Returns(Ctx.Value);
            engine.Setup(e => e.Calendar).Returns(Ctx.Value.Calendar);
            var access = new Mock<IUnitAccessService>();
            access.Setup(a => a.GetAccessibleUnitIdsAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((accessible ?? [Unit]).ToList());
            return new EngineManagerService(db, engine.Object, access.Object, new Mock<IEngineReplayRunner>().Object,
                NullLogger<EngineManagerService>.Instance);
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
