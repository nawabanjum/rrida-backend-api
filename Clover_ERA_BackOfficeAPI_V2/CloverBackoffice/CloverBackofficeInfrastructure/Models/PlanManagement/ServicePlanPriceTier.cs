using System.ComponentModel.DataAnnotations.Schema;

namespace CloverBackoffice.Infrastructure.Models.PlanManagement
{
    /// <summary>
    /// One manager-count band of a plan's price: buying between <see cref="MinManagers"/> and
    /// <see cref="MaxManagers"/> managers costs <see cref="MonthlyPrice"/> / <see cref="AnnualPrice"/>
    /// per manager. E.g. Pro 1–4 $695 / $6,950, 5–9 $595 / $5,950, 10+ $495 / $4,950.
    ///
    /// A plan's tiers start at 1, have no gaps or overlaps, and only the last may be open-ended
    /// (<see cref="MaxManagers"/> null). The set is replaced as a whole.
    /// </summary>
    [Table("ServicePlanPriceTiers", Schema = "public")]
    public class ServicePlanPriceTier
    {
        public int Id { get; init; }

        [ForeignKey(nameof(ServicePlan))]
        public int ServicePlanId { get; set; }

        public virtual ServicePlan? ServicePlan { get; set; }

        public int MinManagers { get; set; }

        /// <summary>Null = no upper limit (last tier only).</summary>
        public int? MaxManagers { get; set; }

        /// <summary>Price per manager per month.</summary>
        [Column(TypeName = "numeric(10,2)")]
        public decimal MonthlyPrice { get; set; }

        /// <summary>Price per manager per year.</summary>
        [Column(TypeName = "numeric(10,2)")]
        public decimal AnnualPrice { get; set; }
    }
}
