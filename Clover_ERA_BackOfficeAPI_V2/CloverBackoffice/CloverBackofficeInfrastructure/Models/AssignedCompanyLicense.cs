using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CloverBackoffice.Infrastructure.Models
{
    // Append-only assignment history of a CompanyLicense to a BusinessUnit.
    //
    // When a license moves from Unit A → Unit B:
    //   1. The active row for Unit A is closed (IsActive=false, UnassignedAt=now).
    //   2. A new row is inserted for Unit B with IsActive=true.
    //
    // Querying current assignment: WHERE CompanyLicenseId=X AND IsActive=true LIMIT 1.
    // Querying history:           ORDER BY AssignedAt ASC.
    public class AssignedCompanyLicense
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        public int CompanyLicenseId { get; set; }

        public uint BusinessUnitId { get; set; }

        public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

        // Null while the assignment is still active. Set when closed.
        public DateTime? UnassignedAt { get; set; }

        public bool IsActive { get; set; } = true;

        // Extra days appended to the subscription's raw period-end when
        // computing this unit's effective expiry. Lives on the assignment
        // (not the shared CompanySubscription) so each unit holding a license
        // can have its own grace window.
        public int? GracePeriod { get; set; }

        public int? CreatedBy { get; set; }
        public int? UpdatedBy { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public bool IsDel { get; set; } = false;

        [ForeignKey("CompanyLicenseId")]
        public virtual CompanyLicense? CompanyLicense { get; set; }

        [ForeignKey("BusinessUnitId")]
        public virtual BusinessUnit? BusinessUnit { get; set; }
    }
}
