using CloverBackoffice.Infrastructure.Models;

namespace CloverBackoffice.Repository.Interfaces
{
    public interface ICompanySubscriptionRepository
    {
        Task<CompanySubscription?> GetActiveByProfileIdAsync(uint profileId);
        Task<CompanySubscription> CreateAsync(CompanySubscription subscription);
        Task<CompanySubscription> UpdateAsync(CompanySubscription subscription);
        Task<List<CompanySubscription>> GetExpiredTrialsAsync();
        Task<CompanySubscription?> GetByStripeSubscriptionIdAsync(string stripeSubscriptionId);
        Task<CompanySubscription?> GetByStripeCustomerIdAsync(string stripeCustomerId);

        // A single subscription by its internal Id, scoped to the owning company
        // so a caller can only ever target its OWN company's subscription. Returns
        // null when the id doesn't exist, is soft-deleted, or belongs to another
        // company. Used by the cancel / reactivate flow to act on exactly one
        // recurring subscription without affecting any other.
        Task<CompanySubscription?> GetByIdForCompanyAsync(int subscriptionId, uint profileId);

        // From the given subscription ids, returns the subset that are currently
        // ACTIVE (Status=Active and not soft-deleted). Used to stamp each payment
        // row's HasSubscription flag in one query instead of per-payment lookups.
        Task<HashSet<int>> GetActiveSubscriptionIdsAsync(IEnumerable<int> subscriptionIds);

        Task<Profile?> GetCompanyByIdAsync(uint profileId);
        Task<BusinessUnit?> GetBusinessUnitForCompanyAsync(uint unitId, uint profileId);

        // Owning company of a unit — used to resolve scope when only a UnitId is
        // supplied. Returns null when the unit doesn't exist / is soft-deleted.
        Task<uint?> GetProfileIdForUnitAsync(uint unitId);
        Task<List<BusinessUnit>> GetBusinessUnitsByCompanyAsync(uint profileId);
        Task<CompanySubscription?> GetLatestByProfileIdAsync(uint profileId);
        Task<List<AssignedCompanyLicense>> GetActiveLicenseAssignmentsByCompanyAsync(uint profileId);
        Task<AssignedCompanyLicense?> GetActiveLicenseAssignmentForUnitAsync(uint unitId);

        // Latest assignment per business unit regardless of IsActive — used
        // by /status as a fallback when the company has expired and the
        // active-only query returns empty. Lets us still emit a status DTO
        // (with IsExpired=true + RenewalUrl) instead of throwing "No
        // subscription found".
        Task<List<AssignedCompanyLicense>> GetLatestLicenseAssignmentsByCompanyAsync(uint profileId);
        Task<AssignedCompanyLicense?> GetLatestLicenseAssignmentForUnitAsync(uint unitId);
        Task<List<CompanyLicense>> GetCompanyLicensesWithAssignmentsAsync(uint profileId);
        Task<List<CompanyLicense>> GetActiveLicensesForUnitAsync(uint unitId);
        Task<CompanyLicense?> GetCompanyLicenseByIdAsync(int licenseId);
    }
}
