using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class TopDataResponse
    {
        public List<string> Recommendations { get; set; }
        public List<string> Themes { get; set; }
        public List<string> Actions { get; set; }
    }

    public class TopReportDataResponse
    {
        public OverallScoreDto OverallScore { get; set; }
        public TrendChartDto TrendChart { get; set; }
        public List<HealthScoreDto> HealthScores { get; set; }
        public List<MetricDto> Metrics { get; set; }

        public List<string> Recommendations { get; set; }
        public List<string> Themes { get; set; }
        public List<string> Actions { get; set; }
        public string? RecommendedWorkSheet { get; set; }
    }



}
