using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CloverBackoffice.Infrastructure.Models.PlanManagement
{
    /// <summary>
    /// The terms of a plan at one point in time: its price tiers, the extension rule and every
    /// feature value, frozen as JSON (<see cref="DTO.PlanManagement.PlanTerms"/>). A new version is
    /// saved whenever an admin changes a plan's terms; name, description and notes are not terms.
    ///
    /// A licence keeps the version it was bought on (CompanyLicenses.ServicePlanVersionId), so
    /// later edits never change what an existing customer gets until an admin migrates them
    /// (POST api/admin/plans/{id}/migrate-licences). A licence with no version reads version 1.
    /// </summary>
    [Table("ServicePlanVersions", Schema = "public")]
    public class ServicePlanVersion
    {
        public const int ReasonMaxLength = 500;

        public int Id { get; init; }

        [ForeignKey(nameof(ServicePlan))]
        public int ServicePlanId { get; set; }

        public virtual ServicePlan? ServicePlan { get; set; }

        /// <summary>1, 2, 3 … per plan.</summary>
        public int VersionNumber { get; set; }

        /// <summary>PlanTerms as JSON.</summary>
        [Column(TypeName = "jsonb")]
        public required string Terms { get; set; }

        [MaxLength(ReasonMaxLength)]
        public string? Reason { get; set; }

        public int CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
