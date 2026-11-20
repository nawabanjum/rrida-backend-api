using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CloverBackoffice.Infrastructure.Models
{
    // Append-only audit trail for ComplimentaryLicenseGrant, kept in its OWN
    // table so complimentary coverage is reported separately from purchased
    // license assignments (AssignedCompanyLicenseAuditLogs). VersionNumber
    // auto-increments per grant Id so the history reads 1, 2, 3 …
    [Table("ComplimentaryLicenseGrantAuditLogs", Schema = "public")]
    public class ComplimentaryLicenseGrantAuditLog
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int AuditId { get; set; }

        public int Id { get; set; }
        public int CompanyLicenseId { get; set; }
        public uint PrimaryBusinessUnitId { get; set; }
        public uint ComplimentaryBusinessUnitId { get; set; }
        public int ManagerUserId { get; set; }
        public DateTime GrantedAt { get; set; }
        public DateTime? RevokedAt { get; set; }
        public bool IsActive { get; set; }
        public int? CreatedBy { get; set; }
        public int? UpdatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public bool IsDel { get; set; }

        public DateTime LoggedAt { get; set; } = DateTime.UtcNow;
        public int VersionNumber { get; set; } = 1;

        [MaxLength(20)]
        public string OperationType { get; set; } = string.Empty;
    }
}
