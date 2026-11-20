using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class DashboardSummaryDto
    {
        public OverallScoreDto OverallScore { get; set; }
        public TrendChartDto TrendChart { get; set; }
        public List<HealthScoreDto> HealthScores { get; set; }
        public List<MetricDto> Metrics { get; set; }

        // License coverage breakdown for the requested date range (selected
        // unit). Purely informational — lets the frontend show which sub-periods
        // are licensed vs unlicensed. Does not affect any health calculation.
        public List<LicensePeriodSegmentDto> LicensePeriods { get; set; }
    }

    public class TrendChartDto
    {
        public List<TrendChartEntryDto> Data { get; set; }
        public List<TrendChartEntryDto> PreviousData { get; set; }
        public List<TrendChartEntryDto> CurrentData { get; set; }
        public string MonthlyChange { get; set; }
        public string SixMonthChange { get; set; }
    }

    public class OverallScoreDto
    {
        public double Value { get; set; }
        public string Status { get; set; }
    }
    public class TrendChartEntryDto
    {
        public string Name { get; set; }
        public double Score { get; set; }
    }

    public class HealthScoreDto
    {
        public string Label { get; set; }
        public double Score { get; set; }
        public string Status { get; set; }
    }

    public class MetricDto
    {
        public string Title { get; set; }
        public int Value { get; set; }
        public string Change { get; set; }
        public string ChangeType { get; set; }
        public MetricStatusDto Status { get; set; }
    }
    public class MetricStatusDto
    {
        public string Label { get; set; }
    }
}
