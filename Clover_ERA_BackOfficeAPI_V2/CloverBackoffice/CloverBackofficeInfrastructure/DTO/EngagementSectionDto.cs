using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class EngagementSectionDto
    {
        public double AvgTrendScore { get; set; }
        public double ChangeVsPrior { get; set; }
        public int CurrentPeriodDays { get; set; } = 30;
        public int PriorPeriodDays { get; set; } = 30;
    }
}
