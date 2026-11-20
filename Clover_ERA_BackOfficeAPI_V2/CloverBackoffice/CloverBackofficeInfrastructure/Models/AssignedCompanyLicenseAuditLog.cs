using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CloverBackoffice.Infrastructure.Models
{
    [Table("AssignedCompanyLicenseAuditLogs", Schema = "public")]
    public class AssignedCompanyLicenseAuditLog
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int AuditId { get; set; }

        public int Id { get; set; }
        public int CompanyLicenseId { get; set; }
        public uint BusinessUnitId { get; set; }
        public DateTime AssignedAt { get; set; }
        public DateTime? UnassignedAt { get; set; }
        public bool IsActive { get; set; }
        public int? GracePeriod { get; set; }
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
