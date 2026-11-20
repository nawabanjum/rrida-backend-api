using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class ResponseRateSectionDto
    {
        public double AvgResponsesPerTeam { get; set; }
        public int TotalResponses { get; set; }
        public int TotalTeams { get; set; }
        public double ChangeVsPrior { get; set; }
        public int CurrentPeriodDays { get; set; } = 30;
        public int PriorPeriodDays { get; set; } = 30;
    }
}
