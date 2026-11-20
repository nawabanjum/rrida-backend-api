using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class    OpportunityFilterRequest
    {
        public string Status { get; set; } = "ALL";

        public int? StageId { get; set; } 
        public string CompanyName { get; set; }
        public string? OpportunityName { get; set; }

    }
}
