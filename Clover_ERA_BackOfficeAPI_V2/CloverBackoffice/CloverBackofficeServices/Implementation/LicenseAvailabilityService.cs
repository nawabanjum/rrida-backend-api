using CloverBackoffice.Components.Enums;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace CloverBackoffice.Services.Implementation
{
    // Profile-level licence availability for the Add User flow. Kept apart from
    // the manager licence team limit ("N of 3"), which is a different rule.
    public class LicenseAvailabilityService : ILicenseAvailabilityService
    {
        // Buy Additional Licences opens a checkout for one licence.
        private const int AdditionalLicenseCount = 1;

        private readonly ICompanyLicenseRepository _licenseRepo;
        private readonly ISubscriptionService _subscriptionService;
        private readonly ILogger<LicenseAvailabilityService> _logger;

        public LicenseAvailabilityService(
            ICompanyLicenseRepository licenseRepo,
            ISubscriptionService subscriptionService,
            ILogger<LicenseAvailabilityService> logger)
        {
            _licenseRepo = licenseRepo;
            _subscriptionService = subscriptionService;
            _logger = logger;
        }

        public async Task<LicenseAvailabilityDto> GetAvailabilityAsync(uint profileId, User actor)
        {
            // Same rule as CompanyLicenseService: RRIDA Admin, or the caller's own profile.
            var isOwnProfile = actor.ProfileId.HasValue && (uint)actor.ProfileId.Value == profileId;
            if (!isOwnProfile && actor.UserGroupId != (int)UserGroups.RridaAdmin)
                throw new UnauthorizedAccessException("You are not authorized to view licenses for this company.");

            var usage = await _licenseRepo.GetLicenseUsageAsync(profileId);

            var result = new LicenseAvailabilityDto
            {
                ProfileId = profileId,
                TotalLicenses = usage.TotalLicenses,
                ConsumedLicenses = usage.ConsumedLicenses,
                AvailableLicenses = usage.AvailableLicenses,
                RemainingLicenses = usage.AvailableLicenses,
                TotalTeamManagers = usage.TotalTeamManagers,
                TotalTeams = usage.TotalTeams,
                MaxTeamsPerLicense = usage.TeamsPerLicense,
                MaxTeams = usage.MaxTeams,
                IsLicenseLimitReached = usage.AvailableLicenses == 0,
                Plans = await _licenseRepo.GetPlanLicenseCountsAsync(profileId) ?? new()
            };

            if (result.IsLicenseLimitReached && isOwnProfile)
                result.BuyLicenseCheckoutUrl = await TryCreateCheckoutUrlAsync(profileId, actor);

            return result;
        }

        // The same checkout /api/subscription/checkout opens. A failure leaves the
        // URL null rather than failing the counts — the frontend can still call
        // the checkout endpoint itself.
        private async Task<string?> TryCreateCheckoutUrlAsync(uint profileId, User actor)
        {
            try
            {
                var current = await _subscriptionService.GetCurrentSubscriptionAsync(profileId);
                var cycle = Enum.TryParse<BillingCycle>(current?.BillingCycle, true, out var parsed)
                            && Enum.IsDefined(typeof(BillingCycle), parsed)
                    ? parsed
                    : BillingCycle.Monthly;

                // An RRIDA Admin viewing another profile: that profile's billing contact.
                var (email, name) = actor.ProfileId == (int)profileId
                    ? (actor.Username, actor.Name)
                    : await _subscriptionService.GetProfileBillingContactAsync(profileId) ?? (actor.Username, actor.Name);

                var checkout = await _subscriptionService.CreateCheckoutAsync(
                    profileId, AdditionalLicenseCount, cycle, email, name);

                return checkout.SessionUrl;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Buy-licence checkout could not be created for profile {ProfileId}.", profileId);
                return null;
            }
        }
    }
}
