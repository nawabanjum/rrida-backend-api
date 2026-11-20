using CloverBackoffice.API.Middlewares;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Implementation;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using UserEntity = CloverBackoffice.Infrastructure.Models.User;
using static CloverBackoffice.Tests.TeamHealth.MilestoneTestData;

namespace CloverBackoffice.Tests.Profile
{
    /// <summary>
    /// POST api/profile/activate-user: a Profile Admin (own profile) or RRIDA Admin
    /// (any profile) activates an invited or inactive user with a temporary
    /// password. Password hashed, verified + Active, roles/profile unchanged.
    /// </summary>
    public class ActivateUserTests
    {
        private const string GoodPassword = "Welcome2RRIDA!";
        private const string OldHash = "old-hash";

        private readonly string _db = Guid.NewGuid().ToString();
        private readonly Mock<IUserRepository> _userRepository = new();
        private readonly Mock<ITeamManagerHandoverService> _handover = new();

        public ActivateUserTests()
        {
            using var db = CreateContext(_db);
            Seed(db);
            foreach (var user in db.Users)
            {
                user.IsVerified = true;
                user.Password = OldHash;
            }
            db.SaveChanges();

            _userRepository.Setup(r => r.InvalidateUserTokensAsync(It.IsAny<int>())).ReturnsAsync(true);
        }

        private UserManualActivationService Service(ApplicationDbContext db) =>
            new(db, Roles(), _userRepository.Object, _handover.Object, NullLogger<UserManualActivationService>.Instance);

        private void SetState(int userId, string status, bool verified, int failedAttempts = 0)
        {
            using var db = CreateContext(_db);
            var user = db.Users.Single(u => u.UserId == userId);
            user.Status = status;
            user.IsVerified = verified;
            user.FailedLoginAttempts = failedAttempts;
            user.LastFailedLoginAt = failedAttempts > 0 ? DateTime.UtcNow : null;
            db.SaveChanges();
        }

        private void Invite(int userId) => SetState(userId, "2", verified: false);

        private async Task<ActivateUserResultDto> Activate(int userId, int actor, string? password = GoodPassword)
        {
            using var db = CreateContext(_db);
            return await Service(db).ActivateUserAsync(userId, password, actor);
        }

        private async Task<string> ActivateFails<TException>(int userId, int actor, string? password = GoodPassword)
            where TException : Exception
            => (await Assert.ThrowsAsync<TException>(() => Activate(userId, actor, password))).Message;

        private UserEntity Get(int userId)
        {
            using var db = CreateContext(_db);
            return db.Users.Single(u => u.UserId == userId);
        }

        private void AssertUntouched(int userId)
        {
            var user = Get(userId);
            Assert.Equal(OldHash, user.Password);
            Assert.NotEqual("1", user.Status);
        }

        // ---------- who may activate ----------

        [Fact]
        public async Task Profile_admin_activates_an_invited_user_of_their_own_profile()
        {
            Invite(ManagerOfTeam);

            var result = await Activate(ManagerOfTeam, ProfileAdminA);

            Assert.Equal(ManagerOfTeam, result.UserId);
            Assert.Equal("1", result.Status);
            Assert.Equal("Active", result.StatusName);
            Assert.True(result.IsActive);
            Assert.True(result.IsVerified);
            Assert.Equal("Pending", result.PreviousStatusName);
            Assert.Equal(ProfileAdminA, result.ActivatedByUserId);

            var user = Get(ManagerOfTeam);
            Assert.Equal("1", user.Status);
            Assert.True(user.IsVerified);
        }

        [Fact]
        public async Task Rrida_admin_activates_users_of_any_profile()
        {
            Invite(ManagerOfTeam);
            Invite(ProfileAdminB);

            await Activate(ManagerOfTeam, RridaAdmin);
            await Activate(ProfileAdminB, RridaAdmin);

            Assert.Equal("1", Get(ManagerOfTeam).Status);
            Assert.Equal("1", Get(ProfileAdminB).Status);
        }

        [Fact]
        public async Task Profile_admin_cannot_activate_a_user_of_another_profile()
        {
            Invite(ProfileAdminB);

            var message = await ActivateFails<UnauthorizedAccessException>(ProfileAdminB, ProfileAdminA);

            Assert.Equal("You can only activate users in your own profile.", message);
            AssertUntouched(ProfileAdminB);
        }

        [Fact]
        public async Task Profile_admin_cannot_activate_a_rrida_admin()
        {
            Invite(RridaAdmin);

            await ActivateFails<UnauthorizedAccessException>(RridaAdmin, ProfileAdminA);

            AssertUntouched(RridaAdmin);
        }

        [Theory]
        [InlineData(ManagerOfParent)]          // Team Manager
        [InlineData(PortfolioManagerOfTeam)]   // Portfolio Manager
        public async Task Other_roles_cannot_activate(int actor)
        {
            Invite(ManagerOfTeam);

            var message = await ActivateFails<UnauthorizedAccessException>(ManagerOfTeam, actor);

            Assert.Equal("Only a Profile Admin or RRIDA Admin can activate a user account.", message);
            AssertUntouched(ManagerOfTeam);
        }

        // ---------- which accounts ----------

        [Theory]
        [InlineData(0)]
        [InlineData(99999)]
        public async Task Unknown_user_is_refused(int userId)
        {
            Assert.Equal("User not found.", await ActivateFails<InvalidOperationException>(userId, ProfileAdminA));
        }

        [Fact]
        public async Task Deleted_user_is_refused()
        {
            Invite(ManagerOfTeam);
            using (var db = CreateContext(_db))
            {
                db.Users.Single(u => u.UserId == ManagerOfTeam).IsDel = true;
                db.SaveChanges();
            }

            Assert.Equal("User not found.", await ActivateFails<InvalidOperationException>(ManagerOfTeam, ProfileAdminA));
        }

        [Fact]
        public async Task Already_active_user_is_refused()
        {
            var message = await ActivateFails<InvalidOperationException>(ManagerOfTeam, ProfileAdminA);

            Assert.StartsWith("This user is already active.", message);
            Assert.Equal(OldHash, Get(ManagerOfTeam).Password);
        }

        [Fact]
        public async Task Inactive_user_is_activated()
        {
            SetState(ManagerOfTeam, "3", verified: true);

            var result = await Activate(ManagerOfTeam, ProfileAdminA);

            Assert.Equal("Inactive", result.PreviousStatusName);
            Assert.Equal("1", Get(ManagerOfTeam).Status);
        }

        [Fact]
        public async Task Active_but_unverified_invitee_is_activated()
        {
            SetState(ManagerOfTeam, "1", verified: false);

            await Activate(ManagerOfTeam, ProfileAdminA);

            Assert.True(Get(ManagerOfTeam).IsVerified);
        }

        // ---------- password ----------

        [Theory]
        [InlineData(null, "Temporary password is required.")]
        [InlineData("", "Temporary password is required.")]
        [InlineData("Short1!", "Password must be at least 10 characters.")]
        [InlineData("alllowercase1", "Password must contain an upper and a lower case letter.")]
        [InlineData("ALLUPPERCASE1", "Password must contain an upper and a lower case letter.")]
        [InlineData("OnlyLettersHere", "Password must contain a number or a symbol.")]
        [InlineData("Has Space 123", "Password cannot contain spaces.")]
        public async Task Weak_password_is_refused_and_nothing_changes(string? password, string expected)
        {
            Invite(ManagerOfTeam);

            Assert.Equal(expected, await ActivateFails<InvalidOperationException>(ManagerOfTeam, ProfileAdminA, password));
            AssertUntouched(ManagerOfTeam);
            Assert.False(Get(ManagerOfTeam).IsVerified);
        }

        [Fact]
        public void Password_longer_than_bcrypt_reads_is_refused()
        {
            Assert.Equal("Password cannot be longer than 72 characters.", PasswordPolicy.Validate("Aa1" + new string('x', 70)));
        }

        [Theory]
        [InlineData("Welcome2RRIDA")]   // number
        [InlineData("Welcome-RRIDA")]   // symbol
        public void Password_matching_the_screen_rules_is_accepted(string password)
        {
            Assert.Null(PasswordPolicy.Validate(password));
        }

        [Fact]
        public async Task Password_is_stored_as_a_bcrypt_hash_the_login_check_accepts()
        {
            Invite(ManagerOfTeam);

            await Activate(ManagerOfTeam, ProfileAdminA);

            var stored = Get(ManagerOfTeam).Password;
            Assert.NotEqual(GoodPassword, stored);
            Assert.StartsWith("$2", stored);
            Assert.True(BCrypt.Net.BCrypt.Verify(GoodPassword, stored));
        }

        [Fact]
        public async Task Response_never_carries_the_password()
        {
            Invite(ManagerOfTeam);

            var result = await Activate(ManagerOfTeam, ProfileAdminA);

            var json = System.Text.Json.JsonSerializer.Serialize(result);
            Assert.DoesNotContain(GoodPassword, json);
            Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Audit_log_masks_the_temporary_password()
        {
            var body = $"{{\"userId\":300,\"temporaryPassword\":\"{GoodPassword}\"}}";

            var stored = AuditMiddleware.MaskSecrets(new PathString("/api/profile/activate-user"), body);

            Assert.DoesNotContain(GoodPassword, stored);
            Assert.Contains("\"userId\":300", stored);
            Assert.Equal("***", AuditMiddleware.MaskSecrets(new PathString("/api/profile/activate-user"), "not json " + GoodPassword));
            // Other endpoints are logged as before.
            Assert.Equal(body, AuditMiddleware.MaskSecrets(new PathString("/api/profile/unlock-user"), body));
        }

        // ---------- side effects ----------

        [Fact]
        public async Task Activation_clears_a_failed_login_lock_signs_out_and_finalises_a_handover()
        {
            SetState(ManagerOfTeam, "2", verified: false, failedAttempts: 5);

            await Activate(ManagerOfTeam, ProfileAdminA);

            var user = Get(ManagerOfTeam);
            Assert.Equal(0, user.FailedLoginAttempts);
            Assert.Null(user.LastFailedLoginAt);
            _userRepository.Verify(r => r.InvalidateUserTokensAsync(ManagerOfTeam), Times.Once);
            _handover.Verify(h => h.FinalizePendingForUserAsync(ManagerOfTeam, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Open_invitation_links_are_revoked_but_manual_reset_links_are_kept()
        {
            Invite(ManagerOfTeam);
            using (var db = CreateContext(_db))
            {
                db.ResetPasswordLinks.AddRange(
                    new ResetPasswordLinks { Id = 1, UserId = ManagerOfTeam, ResetLink = "invite-param", CreatedAt = DateTime.UtcNow },
                    new ResetPasswordLinks { Id = 2, UserId = ManagerOfTeam, ResetLink = "https://app/resetPassword?data=x", CreatedAt = DateTime.UtcNow },
                    new ResetPasswordLinks { Id = 3, UserId = ManagerOfParent, ResetLink = "other-user-invite", CreatedAt = DateTime.UtcNow });
                db.SaveChanges();
            }

            await Activate(ManagerOfTeam, ProfileAdminA);

            using var check = CreateContext(_db);
            Assert.True(check.ResetPasswordLinks.Single(l => l.Id == 1).IsLinkUsed);
            Assert.False(check.ResetPasswordLinks.Single(l => l.Id == 2).IsLinkUsed);
            Assert.False(check.ResetPasswordLinks.Single(l => l.Id == 3).IsLinkUsed);
        }

        [Fact]
        public async Task Activation_keeps_profile_role_and_teams()
        {
            Invite(ManagerOfTeam);
            var before = Get(ManagerOfTeam);
            int teamsBefore;
            using (var db = CreateContext(_db))
                teamsBefore = db.UserTeams.Count(ut => ut.UserId == ManagerOfTeam && !ut.IsUnAssigned);

            await Activate(ManagerOfTeam, ProfileAdminA);

            var after = Get(ManagerOfTeam);
            Assert.Equal(before.ProfileId, after.ProfileId);
            Assert.Equal(before.UserGroupId, after.UserGroupId);
            Assert.Equal(before.Username, after.Username);
            Assert.Equal(before.Name, after.Name);
            using var check = CreateContext(_db);
            Assert.Equal(teamsBefore, check.UserTeams.Count(ut => ut.UserId == ManagerOfTeam && !ut.IsUnAssigned));
        }
    }
}
