using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CloverBackoffice.Infrastructure.Models
{
    [Table("PromoCodeUsageAuditLogs", Schema = "public")]
    public class PromoCodeUsageAuditLog
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int AuditId { get; set; }

        public int Id { get; set; }

        public int PromoCodeId { get; set; }

        public uint ProfileId { get; set; }

        public int? CompanySubscriptionId { get; set; }

        public DateTime UsedAt { get; set; }

        // Audit fields
        public DateTime LoggedAt { get; set; } = DateTime.UtcNow;

        public int VersionNumber { get; set; } = 1;

        [MaxLength(20)]
        public string OperationType { get; set; } = string.Empty; // INSERT, UPDATE, DELETE
    }
}
