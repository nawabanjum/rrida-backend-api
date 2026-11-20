using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class CategoryDetailDto
    {
        public string Category { get; set; }
        public double Score { get; set; }
        public string Status { get; set; }
        public TrendChartDto TrendChart { get; set; }
        public CategoryResponseRateDto ResponseRate { get; set; }
    }

    public class CategoryResponseRateDto
    {
        public int CurrentPeriodCount { get; set; }
        public int PreviousPeriodCount { get; set; }
        public string Change { get; set; }
        public string ChangeType { get; set; }
    }
}
