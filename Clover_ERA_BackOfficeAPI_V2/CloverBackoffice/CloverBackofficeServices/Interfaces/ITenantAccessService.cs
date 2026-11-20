using System.Threading.Tasks;
using CloverBackoffice.Infrastructure.Models;

namespace CloverBackoffice.Services.Interfaces
{
    // Single source of truth for "may this user act on this company?".
    //
    // Every endpoint that accepts a company id is multi-tenant surface: without
    // this check a valid token for company A also reads company B simply by
    // swapping the id in the request. The rules are deliberately narrow:
    //
    //   * A RridaAdmin spans every tenant by design — it is the platform role.
    //   * Anyone else may act on their OWN company.
    //   * A company admin whose own company is flagged IsPartner may also act on
    //     the companies assigned to it (Company.PartnerId == their company). This
    //     is the existing partner feature, not a new grant — the partner company
    //     dropdown and CompanyTeamService.StripPartnerFields already depend on it.
    //
    // Everything else is denied. Modules that need to be stricter than this
    // baseline (e.g. the License module, which refuses partner→customer access
    // outright) keep their own additional check on top.
    public interface ITenantAccessService
    {
        Task<bool> CanAccessCompanyAsync(User? user, uint profileId);

        // Same rules, resolved through the unit's owning company. Gating only the
        // company id would leave the identical hole one parameter over: most
        // read endpoints accept a unitId, and a unit id from another tenant is
        // just as effective as a company id from another tenant.
        Task<bool> CanAccessUnitAsync(User? user, uint unitId);

        // And again through the user's own profile. The user-management endpoints
        // (delete user, update status, reset a password manually) name a USER, not
        // a company or a unit, so both checks above miss them entirely — a Profile
        // Admin could delete or reset the password of somebody in another profile.
        Task<bool> CanAccessUserAsync(User? user, uint userId);
    }
}
