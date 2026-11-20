using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CloverBackoffice.Infrastructure.Models
{
    public class PromoCodeUsage
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        public int PromoCodeId { get; set; }

        public uint ProfileId { get; set; }

        public int? CompanySubscriptionId { get; set; }

        public DateTime UsedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        [ForeignKey("PromoCodeId")]
        public virtual PromoCode? PromoCode { get; set; }

        [ForeignKey(nameof(ProfileId))]
        public virtual Profile? Profile { get; set; }

        [ForeignKey("CompanySubscriptionId")]
        public virtual CompanySubscription? CompanySubscription { get; set; }
    }
}
