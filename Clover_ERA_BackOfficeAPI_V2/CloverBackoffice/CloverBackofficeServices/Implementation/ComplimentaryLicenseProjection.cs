using CloverBackoffice.Infrastructure.Models;
using System.Collections.Generic;

namespace CloverBackoffice.Services.Implementation
{
    // Bridges complimentary grants into the existing purchased-assignment read
    // model. A complimentary beneficiary unit does not have its own
    // AssignedCompanyLicense row — it "rides" the primary team's purchased
    // license. The per-unit DTO builders (BuildUnitDetail / BuildUnitLicense /
    // BuildStatusDto) all resolve a unit's license by matching an assignment
    // whose BusinessUnitId == the unit. This helper fabricates that assignment
    // (and, where a builder filters license.Assignments by unit, a shallow-cloned
    // license carrying only it) so the beneficiary unit shows the SAME license,
    // expiry, and subscription as the primary team — without consuming a slot or
    // touching the real assignment rows.
    internal static class ComplimentaryLicenseProjection
    {
        // A synthetic ACTIVE assignment placing the backing license on the
        // complimentary beneficiary unit. GracePeriod is inherited from the
        // primary assignment (the ride is on the primary team's license).
        public static AssignedCompanyLicense SyntheticAssignment(
            ComplimentaryLicenseGrant grant, CompanyLicense backingLicense, int? primaryGrace)
            => new AssignedCompanyLicense
            {
                CompanyLicenseId = grant.CompanyLicenseId,
                BusinessUnitId = grant.ComplimentaryBusinessUnitId,
                AssignedAt = grant.GrantedAt,
                IsActive = true,
                GracePeriod = primaryGrace,
                CompanyLicense = backingLicense
            };

        // A shallow clone of the backing license carrying ONLY the synthetic
        // beneficiary assignment. Used by builders that filter
        // license.Assignments by unit id. The original license object (the
        // primary team's view) is never mutated.
        public static CompanyLicense SyntheticLicense(
            ComplimentaryLicenseGrant grant, CompanyLicense backingLicense, int? primaryGrace)
        {
            var clone = new CompanyLicense
            {
                Id = backingLicense.Id,
                ProfileId = backingLicense.ProfileId,
                LicenseKey = backingLicense.LicenseKey,
                LicenseType = backingLicense.LicenseType,
                CompanySubscriptionId = backingLicense.CompanySubscriptionId,
                PlanId = backingLicense.PlanId,
                ExpiryDate = backingLicense.ExpiryDate,
                Notes = backingLicense.Notes,
                CreatedAt = backingLicense.CreatedAt,
                UpdatedAt = backingLicense.UpdatedAt,
                IsDel = backingLicense.IsDel,
                CompanySubscription = backingLicense.CompanySubscription,
                Plan = backingLicense.Plan
            };
            clone.Assignments = new List<AssignedCompanyLicense>
            {
                SyntheticAssignment(grant, clone, primaryGrace)
            };
            return clone;
        }
    }
}
