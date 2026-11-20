using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Infrastructure.Models.DeterministicLayer;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Implementation;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CloverBackoffice.Tests.Engine
{
    /// <summary>
    /// A member removed from a team in the app leaves it: their membership ends today, so the
    /// floor stops counting them from tomorrow (MVP roster leftDay), and their answers stay.
    /// </summary>
    public class EngineMemberLeaveTests
    {
        private const uint Unit = 9401;
        private const string Token = "member-token-1";

        [Fact]
        public async Task Leaving_ends_the_membership_today_and_leaving_again_changes_nothing()
        {
            await using var db = NewDb();
            var service = Service(db);
            await service.EnrolAsync(new EngineEnrolRequestDto { TeamCode = $"S{Unit}", MemberToken = Token });

            var left = await service.LeaveAsync(new EngineLeaveRequestDto { BusinessUnitId = Unit, MemberToken = Token });
            var again = await service.LeaveAsync(new EngineLeaveRequestDto { BusinessUnitId = Unit, MemberToken = Token });

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            Assert.Equal(today, left.EffectiveTo);
            Assert.Equal(today, again.EffectiveTo);
            var period = Assert.Single(db.EngineTeamMemberships);
            Assert.Equal(today, period.EffectiveTo);
        }

        [Fact]
        public async Task Someone_not_on_the_team_cannot_leave_it()
        {
            await using var db = NewDb();
            var service = Service(db);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.LeaveAsync(new EngineLeaveRequestDto { BusinessUnitId = Unit, MemberToken = "never-enrolled" }));
        }

        private static EngineCheckInService Service(ApplicationDbContext db)
        {
            db.BusinessUnits.Add(new BusinessUnit
            {
                Id = Unit, BusinessUnitCode = $"BU{Unit}", BusinessUnitShortCode = $"S{Unit}", Name = "Team",
                NameInvariant = "team", RegistrationDate = DateTime.UtcNow, ProfileId = 94,
                ActiveDailyQuestionSetGroupId = 1, ActiveDailyQuestionSetGroup = null!
            });
            db.EngineTeamSettings.Add(new EngineTeamSettings
            {
                BusinessUnitId = Unit, StartDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-10),
                WorkingWeek = "every-day", IsEnabled = true, CreatedAt = DateTime.UtcNow
            });
            db.SaveChanges();
            return new EngineCheckInService(db, new Mock<IRridaEngineContextFactory>().Object,
                new Mock<IEngineTeamProvisioner>().Object, NullLogger<EngineCheckInService>.Instance);
        }

        private static ApplicationDbContext NewDb() =>
            new TestApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"leave-{Guid.NewGuid()}")
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);
    }
}
