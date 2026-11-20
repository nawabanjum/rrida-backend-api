using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;

namespace CloverBackoffice.Services.Interfaces
{
    public interface ILicenseAvailabilityService
    {
        // Total / consumed / available licences of a profile. When none is
        // available, also a Stripe Checkout URL for one more licence (only for a
        // caller of that profile — the Stripe customer is resolved from their
        // email, exactly as /api/subscription/checkout does). Throws
        // UnauthorizedAccessException for another profile's caller.
        Task<LicenseAvailabilityDto> GetAvailabilityAsync(uint profileId, User actor);
    }
}
