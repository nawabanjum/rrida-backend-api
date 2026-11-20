using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Implementation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using UserEntity = CloverBackoffice.Infrastructure.Models.User;
using static CloverBackoffice.Tests.TeamHealth.MilestoneTestData;

namespace CloverBackoffice.Tests.Profile
{
    /// <summary>
    /// POST api/profile/unlock-user: a Profile Admin (own profile) or RRIDA Admin
    /// (any profile) clears the failed-login lock; only the lock fields change.
    /// get-users shows isLocked / lockedUntil.
    /// </summary>
    public class UnlockUserTests
    {
        private const int MaxAttempts = 5;
        private const int WindowMinutes = 4;

        private readonly string _db = Guid.NewGuid().ToString();

        public UnlockUserTests()
        {
            using var db = CreateContext(_db);
            Seed(db);
            foreach (var user in db.Users)
                user.IsVerified = true;
            db.SaveChanges();
        }

        private UserAccountLockService Service(ApplicationDbContext db)
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LoginPolicy:MaxFailedAttempts"] = MaxAttempts.ToString(),
                ["LoginPolicy:LockoutWindowMinutes"] = WindowMinutes.ToString()
            }).Build();
            return new UserAccountLockService(db, Roles(), config, NullLogger<UserAccountLockService>.Instance);
        }

        private void SetLock(int userId, int attempts, double minutesAgo, string? status = null, bool? verified = null)
        {
            using var db = CreateContext(_db);
            var user = db.Users.Single(u => u.UserId == userId);
            user.FailedLoginAttempts = attempts;
            user.LastFailedLoginAt = DateTime.UtcNow.AddMinutes(-minutesAgo);
            if (status != null) user.Status = status;
            if (verified != null) user.IsVerified = verified.Value;
            db.SaveChanges();
        }

        private void Lock(int userId) => SetLock(userId, MaxAttempts, minutesAgo: 1);

        private async Task<UnlockUserResultDto> Unlock(int userId, int actor)
        {
            using var db = CreateContext(_db);
            return await Service(db).UnlockUserAsync(userId, actor);
        }

        private async Task<string> UnlockFails<TException>(int userId, int actor) where TException : Exception
            => (await Assert.ThrowsAsync<TException>(() => Unlock(userId, actor))).Message;

        private UserEntity Get(int userId)
        {
            using var db = CreateContext(_db);
            return db.Users.Single(u => u.UserId == userId);
        }

        // ---------- who may unlock ----------

        [Fact]
        public async Task Profile_admin_unlocks_a_locked_user_of_their_own_profile()
        {
            Lock(ManagerOfTeam);

            var result = await Unlock(ManagerOfTeam, ProfileAdminA);

            Assert.Equal(ManagerOfTeam, result.UserId);
            Assert.False(result.IsLocked);
            Assert.Equal(0, result.FailedLoginAttempts);
            Assert.NotNull(result.WasLockedUntil);
            Assert.Equal(ProfileAdminA, result.UnlockedByUserId);
            Assert.Equal("1", result.Status);
            Assert.Equal("Active", result.StatusName);
            Assert.True(result.IsActive);

            var user = Get(ManagerOfTeam);
            Assert.Equal(0, user.FailedLoginAttempts);
            Assert.Null(user.LastFailedLoginAt);
        }

        [Fact]
        public async Task Rrida_admin_unlocks_users_of_any_profile()
        {
            Lock(ManagerOfTeam);
            Lock(ProfileAdminB);

            await Unlock(ManagerOfTeam, RridaAdmin);
            await Unlock(ProfileAdminB, RridaAdmin);

            Assert.Equal(0, Get(ManagerOfTeam).FailedLoginAttempts);
            Assert.Equal(0, Get(ProfileAdminB).FailedLoginAttempts);
        }

        [Fact]
        public async Task Profile_admin_cannot_unlock_a_user_of_another_profile()
        {
            Lock(ProfileAdminB);

            var message = await UnlockFails<UnauthorizedAccessException>(ProfileAdminB, ProfileAdminA);

            Assert.Equal("You can only unlock users in your own profile.", message);
            Assert.Equal(MaxAttempts, Get(ProfileAdminB).FailedLoginAttempts);
        }

        [Fact]
        public async Task Profile_admin_cannot_unlock_a_rrida_admin()
        {
            Lock(RridaAdmin);

            await UnlockFails<UnauthorizedAccessException>(RridaAdmin, ProfileAdminA);

            Assert.Equal(MaxAttempts, Get(RridaAdmin).FailedLoginAttempts);
        }

        [Theory]
        [InlineData(ManagerOfParent)]          // Team Manager
        [InlineData(PortfolioManagerOfTeam)]   // Portfolio Manager
        public async Task Other_roles_cannot_unlock(int actor)
        {
            Lock(ManagerOfTeam);

            var message = await UnlockFails<UnauthorizedAccessException>(ManagerOfTeam, actor);

            Assert.Equal("Only a Profile Admin or RRIDA Admin can unlock a user account.", message);
            Assert.Equal(MaxAttempts, Get(ManagerOfTeam).FailedLoginAttempts);
        }

        // ---------- which accounts ----------

        [Theory]
        [InlineData(0)]
        [InlineData(99999)]
        public async Task Unknown_user_is_refused(int userId)
        {
            Assert.Equal("User not found.", await UnlockFails<InvalidOperationException>(userId, ProfileAdminA));
        }

        [Fact]
        public async Task Deleted_user_is_refused()
        {
            Lock(ManagerOfTeam);
            using (var db = CreateContext(_db))
            {
                db.Users.Single(u => u.UserId == ManagerOfTeam).IsDel = true;
                db.SaveChanges();
            }

            Assert.Equal("User not found.", await UnlockFails<InvalidOperationException>(ManagerOfTeam, ProfileAdminA));
        }

        [Fact]
        public async Task User_below_the_attempt_limit_is_not_locked()
        {
            SetLock(ManagerOfTeam, MaxAttempts - 1, minutesAgo: 1);

            Assert.Equal("This user's account is not locked.", await UnlockFails<InvalidOperationException>(ManagerOfTeam, ProfileAdminA));
            Assert.Equal(MaxAttempts - 1, Get(ManagerOfTeam).FailedLoginAttempts);
        }

        [Fact]
        public async Task Lock_whose_window_has_passed_is_not_locked()
        {
            SetLock(ManagerOfTeam, MaxAttempts, minutesAgo: WindowMinutes + 1);

            Assert.Equal("This user's account is not locked.", await UnlockFails<InvalidOperationException>(ManagerOfTeam, ProfileAdminA));
        }

        [Fact]
        public async Task User_with_no_failed_logins_is_not_locked()
        {
            Assert.Equal("This user's account is not locked.", await UnlockFails<InvalidOperationException>(ManagerOfTeam, ProfileAdminA));
        }

        // ---------- nothing else changes ----------

        [Fact]
        public async Task Unlock_keeps_status_profile_verification_and_roles()
        {
            SetLock(ManagerOfTeam, MaxAttempts + 2, minutesAgo: 1, status: "3");
            var before = Get(ManagerOfTeam);

            var result = await Unlock(ManagerOfTeam, ProfileAdminA);

            var after = Get(ManagerOfTeam);
            Assert.Equal("3", after.Status);
            Assert.Equal("Inactive", result.StatusName);
            Assert.False(result.IsActive);
            Assert.Equal(before.ProfileId, after.ProfileId);
            Assert.Equal(before.UserGroupId, after.UserGroupId);
            Assert.Equal(before.IsVerified, after.IsVerified);
            Assert.Equal(before.Password, after.Password);
            Assert.Equal(before.ModifiedDate, after.ModifiedDate);
            Assert.Equal(0, after.FailedLoginAttempts);
        }

        [Fact]
        public async Task Unlock_keeps_a_pending_invitation_pending()
        {
            SetLock(ManagerOfTeam, MaxAttempts, minutesAgo: 1, status: "2", verified: false);

            var result = await Unlock(ManagerOfTeam, ProfileAdminA);

            Assert.Equal("Pending", result.StatusName);
            Assert.Equal("2", Get(ManagerOfTeam).Status);
            Assert.False(Get(ManagerOfTeam).IsVerified);
        }

        // ---------- get-users ----------

        [Fact]
        public void Get_users_rows_show_who_is_locked()
        {
            var lastFail = DateTime.UtcNow.AddMinutes(-1);
            var rows = new List<UserStatusDto>
            {
                new() { UserId = 1, FailedLoginAttempts = MaxAttempts, LastFailedLoginAt = lastFail },
                new() { UserId = 2, FailedLoginAttempts = MaxAttempts - 1, LastFailedLoginAt = lastFail },
                new() { UserId = 3, FailedLoginAttempts = MaxAttempts, LastFailedLoginAt = DateTime.UtcNow.AddMinutes(-WindowMinutes - 1) },
                new() { UserId = 4 }
            };

            using var db = CreateContext(_db);
            Service(db).ApplyLockState(rows);

            Assert.True(rows[0].IsLocked);
            Assert.Equal(lastFail.AddMinutes(WindowMinutes), rows[0].LockedUntil);
            Assert.All(rows.Skip(1), r => { Assert.False(r.IsLocked); Assert.Null(r.LockedUntil); });
        }
    }
}
