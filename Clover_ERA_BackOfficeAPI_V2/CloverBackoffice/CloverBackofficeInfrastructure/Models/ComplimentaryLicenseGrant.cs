using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CloverBackoffice.Infrastructure.Models
{
    // A complimentary ("free extra") coverage of ONE purchased CompanyLicense to
    // a SECOND business unit, granted because that unit shares a manager with the
    // unit already holding the license's active (purchased) assignment.
    //
    //   * The purchased assignment lives in AssignedCompanyLicenses and still
    //     obeys the one-active-per-license invariant. A grant NEVER creates an
    //     AssignedCompanyLicense row, so it does NOT consume a purchased slot and
    //     does NOT affect available-license counts.
    //   * PrimaryBusinessUnitId is Team A (the unit the license is purchased-
    //     assigned to). ComplimentaryBusinessUnitId is Team B (the extra team).
    //   * ManagerUserId is the shared user that justified the grant (a user who is
    //     actively assigned to BOTH units), recorded for audit.
    //
    // Rule: a purchased license covers at most LicenseTeamLimits.MaxTeamsPerLicense
    // (3) teams — the purchased team plus up to 2 active grants. A 4th team under
    // the same manager needs its own purchased license. Enforced at grant time
    // only; grants made under an older limit are never revoked by it.
    //
    // Querying active coverage of a unit:
    //   WHERE ComplimentaryBusinessUnitId = X AND IsActive = true LIMIT 1.
    public class ComplimentaryLicenseGrant
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        // The purchased license being shared.
        public int CompanyLicenseId { get; set; }

        // Team A — the unit holding the purchased (active) assignment when the
        // grant was created. Kept for audit/traceability; the live purchased
        // assignee is always resolved from AssignedCompanyLicenses.
        public uint PrimaryBusinessUnitId { get; set; }

        // Team B — the additional team this grant licenses for free.
        public uint ComplimentaryBusinessUnitId { get; set; }

        // The user who is actively assigned to both units and thus justifies the
        // shared-manager rule at grant time.
        public int ManagerUserId { get; set; }

        public DateTime GrantedAt { get; set; } = DateTime.UtcNow;

        // Null while the grant is still active. Set when revoked.
        public DateTime? RevokedAt { get; set; }

        public bool IsActive { get; set; } = true;

        public int? CreatedBy { get; set; }
        public int? UpdatedBy { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public bool IsDel { get; set; } = false;

        [ForeignKey("CompanyLicenseId")]
        public virtual CompanyLicense? CompanyLicense { get; set; }

        [ForeignKey("PrimaryBusinessUnitId")]
        public virtual BusinessUnit? PrimaryBusinessUnit { get; set; }

        [ForeignKey("ComplimentaryBusinessUnitId")]
        public virtual BusinessUnit? ComplimentaryBusinessUnit { get; set; }
    }
}
