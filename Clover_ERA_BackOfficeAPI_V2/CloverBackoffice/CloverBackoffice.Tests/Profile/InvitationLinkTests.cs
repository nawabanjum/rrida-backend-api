using System.Reflection;
using System.Security.Cryptography;
using CloverBackoffice.Components;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Implementation;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace CloverBackoffice.Tests.Profile
{
    /// <summary>
    /// Only the latest invitation link of a user is usable. A new invitation (upsert-user,
    /// resend-invitation, invitation-link minting) makes every earlier link fail both on the
    /// accept-invite page load (verify-email) and when the password is set (reset-password /
    /// post-password-landing).
    /// </summary>
    public class InvitationLinkTests
    {
        private const int PendingUserId = 330;
        private const string Email = "fusion32U1@yopmail.com";

        static InvitationLinkTests()
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureKeyVault:SecretNames:EncryptionKey"] = "TestKey",
                ["AzureKeyVault:SecretNames:EncryptionIV"] = "TestIV",
                ["TestKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                ["TestIV"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)),
            }).Build();
            BussinessManager.Initialize(config, new SendGridSettings());
        }

        [Fact]
        public async Task A_new_invitation_makes_the_previous_link_unusable()
        {
            await using var db = NewDb();
            var service = Service(db);

            var first = await Mint(service);   // upsert-user email
            await Task.Delay(5);
            var latest = await Mint(service);  // resend-invitation email

            // Accept-invite page load with the old link.
            var oldCheck = await service.ValidateInvitationLinkAsync(first);
            Assert.False(oldCheck.IsSuccess);
            Assert.Equal(ResponseMessages.Errors.InvitationLinkSuperseded, oldCheck.Message);

            // Setting a password with the old link.
            var oldReset = await service.ResetPasswordAsync(new ResetPasswordDto { Param = first, NewPassword = "Passw0rd!" });
            Assert.False(oldReset.IsSuccess);
            Assert.Equal(ResponseMessages.Errors.InvitationLinkSuperseded, oldReset.Message);

            var user = await db.Users.SingleAsync();
            Assert.False(user.IsVerified);
            Assert.Equal("2", user.Status);

            // The latest link works.
            Assert.True((await service.ValidateInvitationLinkAsync(latest)).IsSuccess);
            var reset = await service.ResetPasswordAsync(new ResetPasswordDto { Param = latest, NewPassword = "Passw0rd!" });
            Assert.True(reset.IsSuccess);

            user = await db.Users.SingleAsync();
            Assert.True(user.IsVerified);
            Assert.Equal("1", user.Status);
        }

        [Fact]
        public async Task Opening_the_invite_page_does_not_activate_the_user()
        {
            await using var db = NewDb();
            var service = Service(db);
            var link = await Mint(service);

            Assert.True((await service.ValidateInvitationLinkAsync(link)).IsSuccess);

            var user = await db.Users.SingleAsync();
            Assert.False(user.IsVerified);
            Assert.Equal("2", user.Status);
        }

        [Fact]
        public async Task Copy_link_returns_the_latest_invitation()
        {
            await using var db = NewDb();
            var service = Service(db);

            await Mint(service);
            await Task.Delay(5);
            var latest = await Mint(service);

            var copied = await service.GetOrCreateInvitationLinkAsync(await db.Users.SingleAsync());

            Assert.EndsWith("data=" + latest, copied.InviteUrl);
        }

        [Fact]
        public async Task The_latest_link_is_single_use()
        {
            await using var db = NewDb();
            var service = Service(db);
            var link = await Mint(service);

            Assert.True((await service.ResetPasswordAsync(new ResetPasswordDto { Param = link, NewPassword = "Passw0rd!" })).IsSuccess);

            var again = await service.ValidateInvitationLinkAsync(link);
            Assert.False(again.IsSuccess);
            Assert.Equal(ResponseMessages.Errors.LinkAlreadyUsed, again.Message);
        }

        // Every invitation sender (upsert-user, create-manager, resend-invitation,
        // invitation-link) mints through this one method; called directly so no email goes out.
        private static Task<string> Mint(AccountRegistrationService service) =>
            (Task<string>)typeof(AccountRegistrationService)
                .GetMethod("MintInvitationParamAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(service, new object[] { Email, PendingUserId })!;

        private static ApplicationDbContext NewDb()
        {
            var db = new TestApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"invitation-links-{Guid.NewGuid()}")
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);

            db.Users.Add(new User
            {
                UserId = PendingUserId,
                Username = Email,
                Name = "Pending User",
                TenantId = "t",
                Type = "User",
                Status = "2",
                IsVerified = false
            });
            db.SaveChanges();
            return db;
        }

        private static AccountRegistrationService Service(ApplicationDbContext db)
        {
            var repository = new Mock<IAccountRegistrationRepository>();
            repository.Setup(r => r.SaveResetPasswordLink(It.IsAny<ResetPasswordLinks>()))
                .Returns<ResetPasswordLinks>(async l => { db.ResetPasswordLinks.Add(l); await db.SaveChangesAsync(); });
            repository.Setup(r => r.GetUserByEmailIncludingUnverifiedAsync(It.IsAny<string>()))
                .Returns<string>(e => db.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == e.ToLower())!);
            repository.Setup(r => r.UpdateUserPasswordAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
            repository.Setup(r => r.UpdateUserAsync(It.IsAny<User>()))
                .Returns<User>(async u => { db.Users.Update(u); await db.SaveChangesAsync(); });
            repository.Setup(r => r.MarkResetPasswordLinkUsedAsync(It.IsAny<int>()))
                .Returns<int>(async id =>
                {
                    var link = await db.ResetPasswordLinks.SingleAsync(l => l.Id == id);
                    link.IsLinkUsed = true;
                    await db.SaveChangesAsync();
                });

            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DomainValidation:Enabled"] = "false"
            }).Build();

            return new AccountRegistrationService(
                new JwtTokenGenerator(config),
                Options.Create(new SendGridSettings { FrontendBaseUrl = "https://app.test" }),
                config,
                repository.Object,
                db,
                Mock.Of<IUserRepository>(),
                Mock.Of<ICompanyLicenseService>(),
                Mock.Of<ITeamManagerHandoverService>(),
                NullLogger<AccountRegistrationService>.Instance);
        }
    }
}
