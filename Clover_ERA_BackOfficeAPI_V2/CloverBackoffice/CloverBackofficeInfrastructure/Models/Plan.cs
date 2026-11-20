using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CloverBackoffice.Infrastructure.Models
{
    public class Plan
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [MaxLength(50)]
        public required string Name { get; set; }

        [MaxLength(30)]
        public required string Slug { get; set; }

        public int MaxTeams { get; set; }                      // 0 = custom/unlimited

        public int MaxPeoplePerTeam { get; set; } = 20;

        public int MaxPeople { get; set; }                     // 0 = unlimited

        [Column(TypeName = "decimal(10,2)")]
        public decimal MonthlyPrice { get; set; }

        [Column(TypeName = "decimal(10,2)")]
        public decimal AnnualPrice { get; set; }

        [Column(TypeName = "decimal(10,2)")]
        public decimal PricePerTeamMonthly { get; set; }

        public int SavingsPercent { get; set; } = 0;

        public bool IsCustom { get; set; } = false;

        public bool IsPopular { get; set; } = false;

        [Column(TypeName = "jsonb")]
        public string? Features { get; set; }                  // JSON array of feature strings

        public int TrialDays { get; set; } = 14;

        public bool RequiresCreditCard { get; set; }

        [MaxLength(100)]
        public string? StripePriceIdMonthly { get; set; }

        [MaxLength(100)]
        public string? StripePriceIdAnnual { get; set; }

        public int SortOrder { get; set; } = 0;

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
