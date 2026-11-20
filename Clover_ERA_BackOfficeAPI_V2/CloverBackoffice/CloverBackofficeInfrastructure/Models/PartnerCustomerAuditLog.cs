using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CloverBackoffice.Infrastructure.Models
{
    public class PartnerCustomerAuditLog
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        public int ActorUserId { get; set; }

        public uint CustomerId { get; set; }

        public uint? OldPartnerId { get; set; }

        public uint? NewPartnerId { get; set; }

        [MaxLength(20)]
        public required string ActionType { get; set; }
    }
}
