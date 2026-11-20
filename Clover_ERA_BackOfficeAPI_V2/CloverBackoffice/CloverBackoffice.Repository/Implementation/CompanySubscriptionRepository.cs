using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Repository.Implementation
{
    public class CompanySubscriptionRepository : ICompanySubscriptionRepository
    {
        private readonly ApplicationDbContext _context;
        private readonly IPaymentAuditLogRepository _auditLog;

        public CompanySubscriptionRepository(ApplicationDbContext context, IPaymentAuditLogRepository auditLog)
        {
            _context = context;
            _auditLog = auditLog;
        }

        public async Task<CompanySubscription?> GetActiveByProfileIdAsync(uint profileId)
        {
            return await _context.CompanySubscriptions
                .Include(s => s.Plan)
                .Where(s => s.ProfileId == profileId && !s.IsDel)
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefaultAsync();
        }

        public async Task<CompanySubscription> CreateAsync(CompanySubscription subscription)
        {
            subscription.CreatedAt = DateTime.UtcNow;
            subscription.UpdatedAt = DateTime.UtcNow;
            await _context.CompanySubscriptions.AddAsync(subscription);
            await _context.SaveChangesAsync();

            await _auditLog.LogAsync(subscription, "INSERT");
            return subscription;
        }

        public async Task<CompanySubscription> UpdateAsync(CompanySubscription subscription)
        {
            subscription.UpdatedAt = DateTime.UtcNow;
            _context.CompanySubscriptions.Update(subscription);
            await _context.SaveChangesAsync();

            await _auditLog.LogAsync(subscription, "UPDATE");
            return subscription;
        }

        public async Task<List<CompanySubscription>> GetExpiredTrialsAsync()
        {
            return await _context.CompanySubscriptions
                .Where(s => s.Status == SubscriptionStatus.Trial
                         && s.CurrentPeriodEnd < DateTime.UtcNow
                         && !s.IsDel)
                .ToListAsync();
        }

        public async Task<CompanySubscription?> GetByStripeSubscriptionIdAsync(string stripeSubscriptionId)
        {
            return await _context.CompanySubscriptions
                .Include(s => s.Plan)
                .FirstOrDefaultAsync(s => s.StripeSubscriptionId == stripeSubscriptionId && !s.IsDel);
        }

        public async Task<CompanySubscription?> GetByStripeCustomerIdAsync(string stripeCustomerId)
        {
            return await _context.CompanySubscriptions
                .Include(s => s.Plan)
                .Where(s => s.StripeCustomerId == stripeCustomerId && !s.IsDel)
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefaultAsync();
        }

        // Tracked (no AsNoTracking): the cancel / reactivate flow mutates this
        // entity and saves via Update(). The ProfileId predicate is the ownership
        // gate — a subscription id from another company simply returns null.
        public async Task<CompanySubscription?> GetByIdForCompanyAsync(int subscriptionId, uint profileId)
        {
            return await _context.CompanySubscriptions
                .Include(s => s.Plan)
                .FirstOrDefaultAsync(s => s.Id == subscriptionId
                                       && s.ProfileId == profileId
                                       && !s.IsDel);
        }

        // Read-only, batched: the LIVE subset of the given ids. "Live" = not
        // soft-deleted, Status=Active, AND not scheduled to cancel. The last clause
        // matters: a canceled subscription keeps Status=Active during its
        // cancel-at-period-end wind-down (Stripe reports it active until the period
        // ends), so CancelAtPeriodEnd is the only signal it's ending — a canceled
        // subscription must NOT count as active for HasSubscription. AsNoTracking so
        // it never disturbs entities the caller may be mutating in the same scope.
        public async Task<HashSet<int>> GetActiveSubscriptionIdsAsync(IEnumerable<int> subscriptionIds)
        {
            var ids = subscriptionIds.Distinct().ToList();
            if (ids.Count == 0)
                return new HashSet<int>();

            var active = await _context.CompanySubscriptions
                .AsNoTracking()
                .Where(s => ids.Contains(s.Id)
                         && !s.IsDel
                         && s.Status == SubscriptionStatus.Active
                         && !s.CancelAtPeriodEnd)
                .Select(s => s.Id)
                .ToListAsync();

            return active.ToHashSet();
        }

        // AsNoTracking on every status-path read so EF goes to Postgres each
        // call. Without this, a tracked entity loaded earlier in the same
        // scope (or carried over by a misconfigured scope) can shadow the
        // freshly-updated DB row. The service mutates these entities and
        // saves via Update(), which works on detached instances too.
        public async Task<Profile?> GetCompanyByIdAsync(uint profileId)
        {
            return await _context.Profiles
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == profileId && !c.IsDel);
        }

        public async Task<BusinessUnit?> GetBusinessUnitForCompanyAsync(uint unitId, uint profileId)
        {
            return await _context.BusinessUnits
                .AsNoTracking()
                .Where(bu => bu.Id == unitId
                          && bu.Profile.Id == profileId
                          && (bu.IsDel == null || bu.IsDel == false))
                .FirstOrDefaultAsync();
        }

        public async Task<uint?> GetProfileIdForUnitAsync(uint unitId)
        {
            return await _context.BusinessUnits
                .AsNoTracking()
                .Where(bu => bu.Id == unitId
                          && (bu.IsDel == null || bu.IsDel == false))
                .Select(bu => (uint?)bu.Profile.Id)
                .FirstOrDefaultAsync();
        }

        public async Task<List<BusinessUnit>> GetBusinessUnitsByCompanyAsync(uint profileId)
        {
            return await _context.BusinessUnits
                .AsNoTracking()
                .Where(bu => bu.Profile.Id == profileId
                          && (bu.IsDel == null || bu.IsDel == false))
                .OrderBy(bu => bu.Name)
                .ToListAsync();
        }

        public async Task<List<AssignedCompanyLicense>> GetActiveLicenseAssignmentsByCompanyAsync(uint profileId)
        {
            return await _context.AssignedCompanyLicenses
                .AsNoTracking()
                .Include(a => a.CompanyLicense!)
                    .ThenInclude(l => l.CompanySubscription!)
                        .ThenInclude(s => s.Plan)
                .Include(a => a.BusinessUnit!)
                .Where(a => a.IsActive
                         && !a.IsDel
                         && a.CompanyLicense != null
                         && a.CompanyLicense.ProfileId == profileId
                         && !a.CompanyLicense.IsDel)
                .OrderBy(a => a.BusinessUnitId)
                .ToListAsync();
        }

        // Returns the latest assignment per business unit for a company,
        // regardless of IsActive / license.IsDel. Used by /status when the
        // active-only lookup is empty (sub expired → cascade flipped every
        // assignment off). The DTO layer then decides whether the unit is
        // expired/renewable based on dates and emits a RenewalUrl.
        public async Task<List<AssignedCompanyLicense>> GetLatestLicenseAssignmentsByCompanyAsync(uint profileId)
        {
            var rows = await _context.AssignedCompanyLicenses
                .AsNoTracking()
                .Include(a => a.CompanyLicense!)
                    .ThenInclude(l => l.CompanySubscription!)
                        .ThenInclude(s => s.Plan)
                .Include(a => a.BusinessUnit!)
                .Where(a => !a.IsDel
                         && a.CompanyLicense != null
                         && a.CompanyLicense.ProfileId == profileId)
                .ToListAsync();

            // Pick the most-recent assignment per unit. AssignedAt may be
            // null on very old rows so fall back to CreatedAt for ordering.
            return rows
                .GroupBy(a => a.BusinessUnitId)
                .Select(g => g
                    .OrderByDescending(a => a.AssignedAt)
                    .First())
                .OrderBy(a => a.BusinessUnitId)
                .ToList();
        }

        public async Task<AssignedCompanyLicense?> GetLatestLicenseAssignmentForUnitAsync(uint unitId)
        {
            return await _context.AssignedCompanyLicenses
                .AsNoTracking()
                .Include(a => a.CompanyLicense!)
                    .ThenInclude(l => l.CompanySubscription!)
                        .ThenInclude(s => s.Plan)
                .Include(a => a.BusinessUnit!)
                .Where(a => !a.IsDel
                         && a.BusinessUnitId == unitId
                         && a.CompanyLicense != null)
                .OrderByDescending(a => a.AssignedAt)
                .FirstOrDefaultAsync();
        }

        public async Task<AssignedCompanyLicense?> GetActiveLicenseAssignmentForUnitAsync(uint unitId)
        {
            return await _context.AssignedCompanyLicenses
                .AsNoTracking()
                .Include(a => a.CompanyLicense!)
                    .ThenInclude(l => l.CompanySubscription!)
                        .ThenInclude(s => s.Plan)
                .Include(a => a.BusinessUnit!)
                .Where(a => a.IsActive
                         && !a.IsDel
                         && a.BusinessUnitId == unitId
                         && a.CompanyLicense != null
                         && !a.CompanyLicense.IsDel)
                .FirstOrDefaultAsync();
        }

        public async Task<CompanySubscription?> GetLatestByProfileIdAsync(uint profileId)
        {
            return await _context.CompanySubscriptions
                .Include(s => s.Plan)
                .Where(s => s.ProfileId == profileId && !s.IsDel)
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefaultAsync();
        }

        // Single license by id, with CompanySubscription + Plan + Assignments
        // eager-loaded. Assignments lets the apply flow detect "already in
        // use" without a second query.
        public async Task<CompanyLicense?> GetCompanyLicenseByIdAsync(int licenseId)
        {
            return await _context.CompanyLicenses
                .Include(l => l.CompanySubscription!)
                    .ThenInclude(s => s.Plan)
                .Include(l => l.Plan)
                .Include(l => l.Assignments)
                .FirstOrDefaultAsync(l => l.Id == licenseId && !l.IsDel);
        }

        // Licenses currently pinned to a single unit (active assignments only).
        // Used by the single-unit subscription-detail path, which must NOT see
        // any license that doesn't belong to this unit. Assignments is loaded
        // as a filtered include so consumers can read AssignedAt off the
        // license without a second query.
        public async Task<List<CompanyLicense>> GetActiveLicensesForUnitAsync(uint unitId)
        {
            return await _context.CompanyLicenses
                .Include(l => l.CompanySubscription!)
                    .ThenInclude(s => s.Plan)
                .Include(l => l.Plan)
                .Include(l => l.Assignments!
                    .Where(a => a.IsActive && !a.IsDel && a.BusinessUnitId == unitId))
                .Where(l => !l.IsDel
                         && l.Assignments!.Any(a => a.IsActive && !a.IsDel && a.BusinessUnitId == unitId))
                .OrderBy(l => l.Id)
                .ToListAsync();
        }

        // All licenses owned by a company, with each license's currently-active
        // assignment (and the unit it's pinned to) eager-loaded. Unassigned
        // licenses come back with an empty Assignments collection.
        public async Task<List<CompanyLicense>> GetCompanyLicensesWithAssignmentsAsync(uint profileId)
        {
            return await _context.CompanyLicenses
                .Include(l => l.CompanySubscription!)
                    .ThenInclude(s => s.Plan)
                .Include(l => l.Plan)
                .Include(l => l.Assignments!.Where(a => a.IsActive && !a.IsDel))
                    .ThenInclude(a => a.BusinessUnit)
                .Where(l => l.ProfileId == profileId && !l.IsDel)
                .OrderBy(l => l.Id)
                .ToListAsync();
        }
    }
}
