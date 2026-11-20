using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CloverBackoffice.Infrastructure.Models
{
    [Table("CompanyLicenseAuditLogs", Schema = "public")]
    public class CompanyLicenseAuditLog
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int AuditId { get; set; }

        public int Id { get; set; }
        public uint ProfileId { get; set; }

        [MaxLength(64)]
        public string LicenseKey { get; set; } = string.Empty;

        [MaxLength(20)]
        public string LicenseType { get; set; } = string.Empty;

        public int? CompanySubscriptionId { get; set; }
        public int? PlanId { get; set; }

        // Snapshot of the license's individual expiry at log time.
        public DateTime? ExpiryDate { get; set; }

        // Snapshot of the extension remark at log time.
        [MaxLength(1000)]
        public string? Notes { get; set; }

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
