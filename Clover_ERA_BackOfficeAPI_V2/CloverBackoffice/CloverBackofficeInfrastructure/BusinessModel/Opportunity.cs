using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace CloverBackoffice.BusinessModel
{
    public class Opportunity
    {
        public int Id { get; set; }

        [JsonIgnore]
        public DateTime CreatedAt { get; set; }
        [JsonIgnore]
        public DateTime UpdatedAt { get; set; }

        [Required]
        public string CompanyName { get; set; }
        public string CompanyWebsite { get; set; }

        [Required]
        public string PrimaryContactName { get; set; }
        [Required]
        public string PrimaryContactTitle { get; set; }
        [EmailAddress]
        public string PrimaryContactEmail { get; set; }
        public string PrimaryContactPhone { get; set; }

        [Required]
        public int Industryid { get; set; }
        [Required]
        public int NumberOfEmployeesid { get; set; }
        [Required]
        public string CompanyLocation { get; set; }

        [Required]
        public int stageid { get; set; }
        [Required]
        public DateTime ExpectedCloseDate { get; set; }

        public decimal EstimatedDealValue { get; set; }
        public decimal EstimatedRevenue { get; set; }
        public int EstimatedTeams { get; set; }

        [Required]
        public string BusinessPainPoints { get; set; }
        [Required]
        public int CurrentRelationshipStatusid { get; set; }

        public string AdditionalNotes { get; set; }
        public bool AcceptTerms1 { get; set; }
        public bool AcceptTerms2 { get; set; }

        public int ProfileId { get; set; }

        [JsonIgnore]
        public bool IsDel { get; set; } = false;

        [JsonIgnore]
        public int CreatedBy { get; set; }

        [JsonIgnore]
        public int UpdatedBy { get; set; } = 0;
    }
}
