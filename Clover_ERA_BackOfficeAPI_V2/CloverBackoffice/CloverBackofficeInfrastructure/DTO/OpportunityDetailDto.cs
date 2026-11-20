using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class OpportunityDetailDto
    {
        public int Id { get; set; }
        public string CompanyName { get; set; }
        public string CompanyWebsite { get; set; }
        public string PrimaryContactName { get; set; }
        public string PrimaryContactTitle { get; set; }
        public string PrimaryContactEmail { get; set; }
        public string PrimaryContactPhone { get; set; }
        public string CompanyLocation { get; set; }
        public DateTime ExpectedCloseDate { get; set; }
        public decimal? EstimatedDealValue { get; set; }
        public decimal? EstimatedRevenue { get; set; }
        public int? EstimatedTeams { get; set; }
        public string BusinessPainPoints { get; set; }
        public string AdditionalNotes { get; set; }
        public bool AcceptTerms1 { get; set; }
        public bool AcceptTerms2 { get; set; }

        public string IndustryName { get; set; }
        public int IndustryId { get; set; }
        public int NumberOfEmployeesId { get; set; }
        public string NumberOfEmployeesName { get; set; }
        public int RelationshipStatusId { get; set; }
        public string RelationshipStatusName { get; set; }
        public int StageId { get; set; }
        public string StageName { get; set; }

        public CompaniesDto Company { get; set; }
    }

    public class CompaniesDto
    {
        public long Id { get; set; }
        public string DomainName { get; set; }
        public string Name { get; set; }

        /// Was Companies.CompanyShortCode.
        public string ProfileShortCode { get; set; }
        public bool IsPartner { get; set; }
    }
}
