using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class OpportunityListDto
    {
        public int Id { get; set; }
        public string CompanyName { get; set; }
        public string ContactName { get; set; }
        public string ContactTitle { get; set; }
        public string StageName { get; set; }
        public DateTime ExpectedCloseDate { get; set; }
        public int DaysLeft { get; set; }
        public DateTime LastUpdate { get; set; }
        public string AdditionalNotes { get; set; }
        public string? PartnerName { get; set; }

    }
}
