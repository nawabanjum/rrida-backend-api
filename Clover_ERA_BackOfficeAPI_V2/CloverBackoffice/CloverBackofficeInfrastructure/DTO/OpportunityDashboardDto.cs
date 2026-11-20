using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class OpportunityDashboardDto
    {
        public int TotalOpportunities { get; set; }
        public decimal TotalEstimatedMonthlyValue { get; set; }
        public int TotalActivePipeline { get; set; }
        public int TotalExpiringSoon { get; set; }
        public int TotalClosedWon { get; set; }
    }
}
