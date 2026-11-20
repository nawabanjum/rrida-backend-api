using CloverBackoffice.Components.Enums;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using System.Linq;
using System.Text.Json;
using static System.Runtime.InteropServices.JavaScript.JSType;
using ComponentsCloverCategory = CloverBackoffice.Components.Enums.CloverCategory;
using ModelsCloverCategory = CloverBackoffice.Infrastructure.Models.CloverCategory;

namespace CloverBackoffice.Repository.Implementation
{
    public class AggregateDashboardRepository : IAggregateDashboardRepository
    {
        private readonly ApplicationDbContext _context;
        private readonly FeedbackScoreThresholdsOptions _thresholds;


        public AggregateDashboardRepository(ApplicationDbContext context, IOptions<FeedbackScoreThresholdsOptions> thresholds)
        {
            _context = context;
            _thresholds = thresholds.Value;
        }

        public async Task<BusinessUnit?> GetBusinessUnitAsync(long unitId)
        {
            return await _context.BusinessUnits
                .Where(b => b.Id == unitId && b.IsDel != true)
                .FirstOrDefaultAsync();
        }

        // Recursive method to get all children
        public async Task<List<long>> GetBusinessUnitHierarchyRecursiveAsync(long parentId)
        {
            var result = new List<long> { parentId };

            var children = await _context.BusinessUnits
                .Where(b => b.ParentId == parentId && b.IsDel != true)
                .Select(c => c.Id)
                .ToListAsync();

            foreach (var childId in children)
            {
                result.AddRange(await GetBusinessUnitHierarchyRecursiveAsync(childId));
            }

            return result;
        }

        // Check if a unit has feedback data in the given date range
        public async Task<bool> UnitHasFeedbackDataAsync(long unitId, DateTimeOffset fromDate, DateTimeOffset toDate)
        {
            return await _context.DailyFeedbacks
                .AnyAsync(df => df.BusinessUnitId == unitId 
                    && df.SubmissionDateTime >= fromDate.UtcDateTime 
                    && df.SubmissionDateTime <= toDate.UtcDateTime);
        }

        // Updated to accept multiple UnitIds
        public async Task<List<DailyFeedbackReportRowDto>> GetDailyFeedbackReportAsync(List<long> unitIds, DateTimeOffset fromDate, DateTimeOffset toDate)
        {
            string query = @"
                SELECT DISTINCT ON (df.""Id"")
                    df.""Id"" AS ""FeedbackId"",
                    df.""SubmissionDateTime"",
                    df.""SentimentType"",
                    df.""FreeText"",
                    df.""BusinessUnitId"",
                    dq.""Id"" AS ""QuestionId"",
                    dq.""Text"" AS ""QuestionText"",
                    dr.""Id"" AS ""ReasonId"",
                    dr.""Text"" AS ""ReasonText"",
                    dr.""RequiresFreeTextFeedback"" AS ""RequiresFreeText"",
                    dr.""SpecificationId"",
                    dqs.""Ordinal"" AS ""SpecificationOrdinal"",
                    dqs.""DailyQuestionSetGroupId"" AS ""SpecificationGroupId"",
                    dqs.""CloverCategory"" AS ""SpecificationCloverCategory""
                FROM ""DailyFeedback"" df
                LEFT JOIN ""DailyQuestions"" dq ON df.""DailyQuestionId"" = dq.""Id""
                LEFT JOIN ""DailyReasons"" dr ON df.""DailyReasonId"" = dr.""Id""
                LEFT JOIN ""DailyQuestionSpecifications"" dqs ON dr.""SpecificationId"" = dqs.""Id""
                WHERE df.""BusinessUnitId"" = ANY(@UnitIds)
                    AND df.""SubmissionDateTime"" BETWEEN @FromDate AND @ToDate;
            ";

            var result = await _context.DailyFeedbackReportRowDtos
                .FromSqlRaw(query,
                    new Npgsql.NpgsqlParameter("@UnitIds", unitIds.ToArray()),
                    new Npgsql.NpgsqlParameter("@FromDate", fromDate.UtcDateTime),
                    new Npgsql.NpgsqlParameter("@ToDate", toDate.UtcDateTime))
                .ToListAsync();

            return result;
        }

        public async Task<DashboardSummaryDto> AnalyzeAndSaveFeedbackInternal(List<DailyFeedbackReportRowDto> feedbackRows, List<long> unitIds, long requestedUnitId, DateTime requestedFromDate, DateTime requestedToDate)
        {
            // Calculate analysis results grouped by time periods (aggregate across all unitIds)
            var analysisResults = CalculateFeedbackAnalysis(feedbackRows, requestedFromDate, requestedToDate, unitIds);
            return await GetDashboardSummaryFromAnalysisAsync(unitIds, requestedUnitId, analysisResults, feedbackRows, requestedFromDate, requestedToDate);
        }

        public List<FeedbackAnalysisResult> CalculateFeedbackAnalysis(List<DailyFeedbackReportRowDto> feedbackRows, DateTime fromDate, DateTime toDate, List<long> unitIds)
        {
            var analysisResults = new List<FeedbackAnalysisResult>();

            // Determine granularity based on date range
            var duration = (toDate - fromDate).TotalDays;
            string granularity = duration switch
            {
                >= 31 => "monthly",   // 31 days or more → monthly grouping
                >= 8 => "weekly",     // 8-30 days → weekly grouping
                _ => "daily"          // 1-7 days or less → daily grouping
            };

            // Filter feedback to only include the specified unitIds
            var filteredFeedback = feedbackRows
                .Where(r => unitIds.Contains((long)r.BusinessUnitId))
                .ToList();

            // Group by category AND time period (aggregate across all unitIds)
            var grouped = filteredFeedback
                .Where(r => r.SpecificationCloverCategory.HasValue &&
                           Enum.IsDefined(typeof(CloverBackoffice.Components.Enums.CloverCategory), r.SpecificationCloverCategory.Value))
                .GroupBy(r => new
                {
                    Category = ((CloverBackoffice.Components.Enums.CloverCategory)r.SpecificationCloverCategory!.Value).ToString(),
                    TimeBucket = granularity switch
                    {
                        "monthly" => new DateTime(r.SubmissionDateTime.Year, r.SubmissionDateTime.Month, 1),
                        "weekly" => r.SubmissionDateTime.Date.AddDays(-(int)r.SubmissionDateTime.DayOfWeek),
                        _ => r.SubmissionDateTime.Date
                    }
                });

            foreach (var group in grouped)
            {
                var totalCount = group.Count();
                if (totalCount == 0) continue;

                double angry = group.Count(r => r.SentimentType == (int)CloverBackoffice.Components.Enums.DailyFeedbackSentimentType.Angry);
                double sad = group.Count(r => r.SentimentType == (int)CloverBackoffice.Components.Enums.DailyFeedbackSentimentType.Sad);
                double indifferent = group.Count(r => r.SentimentType == (int)CloverBackoffice.Components.Enums.DailyFeedbackSentimentType.Indifferent);
                double happy = group.Count(r => r.SentimentType == (int)CloverBackoffice.Components.Enums.DailyFeedbackSentimentType.Happy);

                // Use the first unitId as the BusinessUnitId for the aggregated result
                // This is just for storage - the actual aggregation is across all unitIds
                var result = new FeedbackAnalysisResult
                {
                    AnalysisDate = group.Key.TimeBucket,
                    SubmissionDate = group.Key.TimeBucket,
                    CategoryName = group.Key.Category,
                    AngryPercentage = Math.Round((angry / totalCount) * 100, 2),
                    SadPercentage = Math.Round((sad / totalCount) * 100, 2),
                    IndifferentPercentage = Math.Round((indifferent / totalCount) * 100, 2),
                    HappyPercentage = Math.Round((happy / totalCount) * 100, 2),
                    BusinessUnitId = (uint)unitIds.First() // Use first unitId for storage purposes
                };
                analysisResults.Add(result);
            }

            return analysisResults;
        }
        public async Task<DashboardSummaryDto> GetDashboardSummaryFromAnalysisAsync(List<long> unitIds, long requestedUnitId, List<FeedbackAnalysisResult> analysisResults, List<DailyFeedbackReportRowDto> rawFeedback, DateTime requestedFromDate, DateTime requestedToDate)
        {
            var result = new DashboardSummaryDto();

            // Determine granularity based on requested date range
            var duration = (requestedToDate - requestedFromDate).TotalDays;
            string granularity = duration switch
            {
                >= 31 => "monthly",   // 31 days or more → monthly view
                >= 8 => "weekly",     // 8-30 days → weekly view
                _ => "daily"          // 1-7 days or less → daily view
            };

            // 1️ Overall Score (current period only) - aggregate across all unitIds
            var overallScores = analysisResults
                .Where(x => unitIds.Contains((long)x.BusinessUnitId) &&
                            x.AnalysisDate >= requestedFromDate &&
                            x.AnalysisDate <= requestedToDate)
                .Select(x =>
                    (x.AngryPercentage * 1 +
                     x.SadPercentage * 34 +
                     x.IndifferentPercentage * 67 +
                     x.HappyPercentage * 100) / 100.0
                ).ToList();

            if (overallScores.Any())
            {
                var avg = Math.Round(overallScores.Average(), 2);
                result.OverallScore = new OverallScoreDto
                {
                    Value = avg,
                    Status = GetStatusFromScore(avg)
                };
            }

            // 2 Trend Chart - Group by time buckets and include both current + previous periods
            // Calculate extended date range (doubled to include previous period)
            var extendedDuration = (requestedToDate - requestedFromDate).TotalDays;
            var extendedFromDate = requestedFromDate.AddDays(-extendedDuration);
            
            // Generate all time buckets for the entire extended range
            var allTimeBuckets = new List<DateTime>();
            var currentDate = extendedFromDate;
            
            while (currentDate <= requestedToDate)
            {
                DateTime bucket = granularity switch
                {
                    "monthly" => new DateTime(currentDate.Year, currentDate.Month, 1),
                    "weekly" => currentDate.Date.AddDays(-(int)currentDate.DayOfWeek), // Start of week (Sunday)
                    _ => currentDate.Date // Daily
                };
                
                if (!allTimeBuckets.Contains(bucket))
                {
                    allTimeBuckets.Add(bucket);
                }
                
                // Move to next period
                currentDate = granularity switch
                {
                    "monthly" => bucket.AddMonths(1),
                    "weekly" => bucket.AddDays(7),
                    _ => bucket.AddDays(1)
                };
            }
            
            // Get actual data from analysis results - aggregate across all unitIds
            var trendDataRaw = analysisResults
                .Where(x => unitIds.Contains((long)x.BusinessUnitId))
                .GroupBy(x => x.AnalysisDate)
                .Select(g => new
                {
                    Date = g.Key,
                    Score = Math.Round(g.Average(x =>
                        (x.AngryPercentage * 1 +
                         x.SadPercentage * 34 +
                         x.IndifferentPercentage * 67 +
                         x.HappyPercentage * 100) / 100.0), 2)
                })
                .ToDictionary(t => t.Date, t => t.Score);

            // Build complete trend data with all time buckets, filling missing ones with 0
            // Create a list that includes both the bucket date and the DTO for easier filtering
            var trendDataWithDates = allTimeBuckets
                .OrderBy(d => d)
                .Select(bucket => new
                {
                    BucketDate = bucket,
                    Entry = new TrendChartEntryDto
                    {
                        Name = granularity switch
                        {
                            "monthly" => bucket.ToString("MMM yyyy"),
                            "weekly" => $"Week {bucket:MM/dd}",
                            _ => bucket.ToString("MM/dd")
                        },
                        Score = trendDataRaw.ContainsKey(bucket) ? trendDataRaw[bucket] : 0
                    }
                })
                .ToList();

            if (trendDataWithDates.Count > 0)
            {
                string monthlyChange = "N/A";
                string sixMonthChange = "N/A";

                // Get all data (combined previous + current periods)
                var allData = trendDataWithDates.Select(t => t.Entry).ToList();

                // Split data into current period and previous period (include ALL periods, even with zero scores)
                // A bucket is in the current period if its period overlaps with the requested range
                var currentPeriodData = trendDataWithDates
                    .Where(t =>
                    {
                        bool isInCurrentPeriod = granularity switch
                        {
                            "monthly" => t.BucketDate <= requestedToDate && t.BucketDate.AddMonths(1) > requestedFromDate,
                            "weekly" => t.BucketDate <= requestedToDate && t.BucketDate.AddDays(7) > requestedFromDate,
                            _ => t.BucketDate >= requestedFromDate && t.BucketDate <= requestedToDate
                        };
                        return isInCurrentPeriod;
                    })
                    .Select(t => t.Entry)
                    .ToList();
                    
                var previousPeriodData = trendDataWithDates
                    .Where(t =>
                    {
                        bool isInPreviousPeriod = granularity switch
                        {
                            "monthly" => t.BucketDate.AddMonths(1) <= requestedFromDate,
                            "weekly" => t.BucketDate.AddDays(7) <= requestedFromDate,
                            _ => t.BucketDate < requestedFromDate
                        };
                        return isInPreviousPeriod;
                    })
                    .Select(t => t.Entry)
                    .ToList();

                // Calculate average for current vs previous period (only considering non-zero scores for comparison)
                var currentPeriodDataWithScores = currentPeriodData.Where(t => t.Score > 0).ToList();
                var previousPeriodDataWithScores = previousPeriodData.Where(t => t.Score > 0).ToList();
                
                if (currentPeriodDataWithScores.Any() && previousPeriodDataWithScores.Any())
                {
                    var currentAvg = currentPeriodDataWithScores.Average(t => t.Score);
                    var previousAvg = previousPeriodDataWithScores.Average(t => t.Score);
                    var change = currentAvg - previousAvg;
                    var percent = previousAvg != 0 ? (change / previousAvg) * 100 : 0;
                    monthlyChange = change >= 0
                        ? $"↗ {Math.Abs(percent):0.#}% vs previous period"
                        : $"↘ {Math.Abs(percent):0.#}% vs previous period";
                }

                // Calculate overall trend (first vs last in entire dataset, only considering non-zero scores)
                var nonZeroTrendData = allData.Where(t => t.Score > 0).ToList();
                if (nonZeroTrendData.Count >= 2)
                {
                    var last = nonZeroTrendData[^1].Score;
                    var first = nonZeroTrendData[0].Score;
                    var change = last - first;
                    var percent = first != 0 ? (change / first) * 100 : 0;
                    sixMonthChange = change >= 0
                        ? $"↗ {Math.Abs(percent):0.#}% overall trend"
                        : $"↘ {Math.Abs(percent):0.#}% overall trend";
                }

                result.TrendChart = new TrendChartDto
                {
                    Data = allData,
                    PreviousData = previousPeriodData,
                    CurrentData = currentPeriodData,
                    MonthlyChange = monthlyChange,
                    SixMonthChange = sixMonthChange
                };
            }

            // 3 Health Scores (current period only, grouped by CategoryName) - aggregate across all unitIds
            // Calculate scores for categories that have data
            var calculatedScores = analysisResults
                .Where(x => unitIds.Contains((long)x.BusinessUnitId) &&
                            x.AnalysisDate >= requestedFromDate &&
                            x.AnalysisDate <= requestedToDate)
                .GroupBy(x => x.CategoryName)
                .Select(g =>
                {
                    var avg = Math.Round(g.Average(x =>
                        (x.AngryPercentage * 1 +
                         x.SadPercentage * 34 +
                         x.IndifferentPercentage * 67 +
                         x.HappyPercentage * 100) / 100.0), 2);
                    return new
                    {
                        CategoryName = g.Key,
                        Score = avg
                    };
                })
                .ToDictionary(x => x.CategoryName, x => x.Score);

            // Ensure all 6 categories are always included, with default score of 0 for missing ones
            var allCategories = Enum.GetValues(typeof(CloverBackoffice.Components.Enums.CloverCategory))
                .Cast<CloverBackoffice.Components.Enums.CloverCategory>()
                .Select(cat => cat.ToString())
                .ToList();

            var healthScores = allCategories
                .Select(categoryName =>
                {
                    var score = calculatedScores.ContainsKey(categoryName) ? calculatedScores[categoryName] : 0;
                    return new HealthScoreDto
                    {
                        Label = categoryName,
                        Score = score,
                        Status = GetStatusFromScore(score)
                    };
                })
                .OrderBy(h => (int)Enum.Parse<CloverBackoffice.Components.Enums.CloverCategory>(h.Label))
                .ToList();

            result.HealthScores = healthScores;

            // 4️⃣ Response Rate (compare current period vs previous period) - SUM across all unitIds
            var metrics = new List<MetricDto>();

            var currentPeriodCount = rawFeedback.Count(r =>
                unitIds.Contains((long)r.BusinessUnitId) &&
                r.SubmissionDateTime.UtcDateTime >= requestedFromDate &&
                r.SubmissionDateTime.UtcDateTime <= requestedToDate);

            var previousPeriodStart = requestedFromDate - (requestedToDate - requestedFromDate);
            var previousPeriodCount = rawFeedback.Count(r =>
                unitIds.Contains((long)r.BusinessUnitId) &&
                r.SubmissionDateTime.UtcDateTime >= previousPeriodStart &&
                r.SubmissionDateTime.UtcDateTime < requestedFromDate);

            if (currentPeriodCount > 0 || previousPeriodCount > 0)
            {
                string changeStr = "N/A";
                string changeType = "neutral";
                string statusLabel = "No Data";

                // Calculate percentage change
                if (previousPeriodCount == 0 && currentPeriodCount > 0)
                {
                    // If previous period has no data but current has data, show positive trend
                    // Show +100% as a simple convention to indicate positive change from zero baseline
                    changeStr = "+100% vs previous period";
                    changeType = "positive";
                    statusLabel = "On Target";
                }
                else if (previousPeriodCount > 0)
                {
                    // Calculate percentage difference: ((current - previous) / previous) * 100
                    var change = ((double)(currentPeriodCount - previousPeriodCount) / previousPeriodCount) * 100;
                    
                    // Cap percentage at ±100% for better user-friendliness and clarity
                    var cappedChange = Math.Max(-100, Math.Min(100, change));
                    
                    changeStr = cappedChange >= 0
                        ? $"+{Math.Abs(cappedChange):0.0}% vs previous period"
                        : $"-{Math.Abs(cappedChange):0.0}% vs previous period";

                    changeType = cappedChange >= 0 ? "positive" : "negative";
                    statusLabel = cappedChange >= 0 ? "On Target" : "Needs Attention";
                }
                else
                {
                    // Both periods have no data
                    changeStr = "0% vs previous period";
                    changeType = "neutral";
                    statusLabel = "No Data";
                }

                metrics.Add(new MetricDto
                {
                    Title = "Responses",
                    Value = currentPeriodCount,
                    Change = changeStr,
                    ChangeType = changeType,
                    Status = new MetricStatusDto { Label = statusLabel }
                });
            }

            result.Metrics = metrics;
            if (result.Metrics == null || !result.Metrics.Any() || result.Metrics.All(m => m.Value == 0))
            {
                result.OverallScore = new OverallScoreDto { Value = 0, Status = "Needs Work" };
                result.TrendChart = new TrendChartDto
                {
                    Data = new List<TrendChartEntryDto>
                    {
                        new TrendChartEntryDto { Name = DateTime.UtcNow.ToString("MMM yyyy"), Score = 0 }
                    },
                    PreviousData = new List<TrendChartEntryDto>(),
                    CurrentData = new List<TrendChartEntryDto>
                    {
                        new TrendChartEntryDto { Name = DateTime.UtcNow.ToString("MMM yyyy"), Score = 0 }
                    },
                    MonthlyChange = "N/A",
                    SixMonthChange = "N/A"
                };
                result.HealthScores = new List<HealthScoreDto>
        {
            new HealthScoreDto { Label = "Communication", Score = 0, Status = "Excellent" },
            new HealthScoreDto { Label = "Learning", Score = 0, Status = "Excellent" },
            new HealthScoreDto { Label = "Opportunities", Score = 0, Status = "Urgent Attention" },
            new HealthScoreDto { Label = "Vulnerability", Score = 0, Status = "Good" },
            new HealthScoreDto { Label = "Enablement", Score = 0, Status = "Urgent Attention" },
            new HealthScoreDto { Label = "Reflection", Score = 0, Status = "Excellent" }
        };
                result.Metrics = new List<MetricDto>
        {
            new MetricDto
            {
                Title = "Responses",
                Value = 0,
                Change = "N/A",
                ChangeType = "neutral",
                Status = new MetricStatusDto { Label = "No Data" }
            }
        };
            }

            return result;
        }
        public string GetStatusFromScore(double score)
        {
            if (score >= _thresholds.ExcellentMin)
                return "Excellent";
            else if (score >= _thresholds.GoodMin)
                return "Good";
            else if (score >= _thresholds.NeedsWorkMin)
                return "Needs Work";
            else
                return "Urgent Attention";
        }


        public TrendChartDto CalculateTrendChartFromFeedback(List<DailyFeedbackReportRowDto> feedbackRows, List<long> unitIds, DateTime requestedFromDate, DateTime requestedToDate)
        {
            var analysisResults = CalculateFeedbackAnalysis(feedbackRows, requestedFromDate, requestedToDate, unitIds);

            var duration = (requestedToDate - requestedFromDate).TotalDays;
            string granularity = duration switch
            {
                >= 31 => "monthly",
                >= 8 => "weekly",
                _ => "daily"
            };

            var extendedDuration = duration;
            var extendedFromDate = requestedFromDate.AddDays(-extendedDuration);

            var allTimeBuckets = new List<DateTime>();
            var currentDate = extendedFromDate;

            while (currentDate <= requestedToDate)
            {
                DateTime bucket = granularity switch
                {
                    "monthly" => new DateTime(currentDate.Year, currentDate.Month, 1),
                    "weekly" => currentDate.Date.AddDays(-(int)currentDate.DayOfWeek),
                    _ => currentDate.Date
                };

                if (!allTimeBuckets.Contains(bucket))
                    allTimeBuckets.Add(bucket);

                currentDate = granularity switch
                {
                    "monthly" => bucket.AddMonths(1),
                    "weekly" => bucket.AddDays(7),
                    _ => bucket.AddDays(1)
                };
            }

            var trendDataRaw = analysisResults
                .Where(x => unitIds.Contains((long)x.BusinessUnitId))
                .GroupBy(x => x.AnalysisDate)
                .Select(g => new
                {
                    Date = g.Key,
                    Score = Math.Round(g.Average(x =>
                        (x.AngryPercentage * 1 +
                         x.SadPercentage * 34 +
                         x.IndifferentPercentage * 67 +
                         x.HappyPercentage * 100) / 100.0), 2)
                })
                .ToDictionary(t => t.Date, t => t.Score);

            var trendDataWithDates = allTimeBuckets
                .OrderBy(d => d)
                .Select(bucket => new
                {
                    BucketDate = bucket,
                    Entry = new TrendChartEntryDto
                    {
                        Name = granularity switch
                        {
                            "monthly" => bucket.ToString("MMM yyyy"),
                            "weekly" => $"Week {bucket:MM/dd}",
                            _ => bucket.ToString("MM/dd")
                        },
                        Score = trendDataRaw.ContainsKey(bucket) ? trendDataRaw[bucket] : 0
                    }
                })
                .ToList();

            if (trendDataWithDates.Count == 0)
            {
                return new TrendChartDto
                {
                    Data = new List<TrendChartEntryDto> { new() { Name = DateTime.UtcNow.ToString("MMM yyyy"), Score = 0 } },
                    PreviousData = new List<TrendChartEntryDto>(),
                    CurrentData = new List<TrendChartEntryDto> { new() { Name = DateTime.UtcNow.ToString("MMM yyyy"), Score = 0 } },
                    MonthlyChange = "N/A",
                    SixMonthChange = "N/A"
                };
            }

            string monthlyChange = "N/A";
            string sixMonthChange = "N/A";

            var allData = trendDataWithDates.Select(t => t.Entry).ToList();

            var currentPeriodData = trendDataWithDates
                .Where(t =>
                {
                    bool isInCurrentPeriod = granularity switch
                    {
                        "monthly" => t.BucketDate <= requestedToDate && t.BucketDate.AddMonths(1) > requestedFromDate,
                        "weekly" => t.BucketDate <= requestedToDate && t.BucketDate.AddDays(7) > requestedFromDate,
                        _ => t.BucketDate >= requestedFromDate && t.BucketDate <= requestedToDate
                    };
                    return isInCurrentPeriod;
                })
                .Select(t => t.Entry)
                .ToList();

            var previousPeriodData = trendDataWithDates
                .Where(t =>
                {
                    bool isInPreviousPeriod = granularity switch
                    {
                        "monthly" => t.BucketDate.AddMonths(1) <= requestedFromDate,
                        "weekly" => t.BucketDate.AddDays(7) <= requestedFromDate,
                        _ => t.BucketDate < requestedFromDate
                    };
                    return isInPreviousPeriod;
                })
                .Select(t => t.Entry)
                .ToList();

            var currentPeriodDataWithScores = currentPeriodData.Where(t => t.Score > 0).ToList();
            var previousPeriodDataWithScores = previousPeriodData.Where(t => t.Score > 0).ToList();

            if (currentPeriodDataWithScores.Any() && previousPeriodDataWithScores.Any())
            {
                var currentAvg = currentPeriodDataWithScores.Average(t => t.Score);
                var previousAvg = previousPeriodDataWithScores.Average(t => t.Score);
                var change = currentAvg - previousAvg;
                var percent = previousAvg != 0 ? (change / previousAvg) * 100 : 0;
                monthlyChange = change >= 0
                    ? $"↗ {Math.Abs(percent):0.#}% vs previous period"
                    : $"↘ {Math.Abs(percent):0.#}% vs previous period";
            }

            var nonZeroTrendData = allData.Where(t => t.Score > 0).ToList();
            if (nonZeroTrendData.Count >= 2)
            {
                var last = nonZeroTrendData[^1].Score;
                var first = nonZeroTrendData[0].Score;
                var change = last - first;
                var percent = first != 0 ? (change / first) * 100 : 0;
                sixMonthChange = change >= 0
                    ? $"↗ {Math.Abs(percent):0.#}% overall trend"
                    : $"↘ {Math.Abs(percent):0.#}% overall trend";
            }

            return new TrendChartDto
            {
                Data = allData,
                PreviousData = previousPeriodData,
                CurrentData = currentPeriodData,
                MonthlyChange = monthlyChange,
                SixMonthChange = sixMonthChange
            };
        }

        public async Task<TeamsSectionDto> GetTeamsSection(List<long> allUnitIds, List<long> licensedUnitIds, List<long> complimentaryUnitIds)
        {
            return new TeamsSectionDto
            {
                TotalTeams = allUnitIds?.Count ?? 0,              // overall (licensed + unlicensed)
                ActiveLicensedTeams = licensedUnitIds?.Count ?? 0, // active-license subset
                ComplimentaryLicenseCount = complimentaryUnitIds?.Count ?? 0 // complimentary subset of the above
            };
        }

        public async Task<ResponseRateSectionDto> GetResponseRateSection(List<long> unitIds)
        {
            try
            {
                if (unitIds == null || !unitIds.Any())
                    return new ResponseRateSectionDto();

                var totalTeams = unitIds.Count;
                var today = DateTime.UtcNow.Date;

                var currentPeriodStart = today.AddDays(-30);
                var priorPeriodStart = today.AddDays(-60);

                var feedbacks = await _context.DailyFeedbacks
                    .Where(x => unitIds.Contains(x.BusinessUnitId)
                        && x.SubmissionDateTime.Date >= priorPeriodStart
                        && x.SubmissionDateTime.Date <= today)
                    .ToListAsync();

                // Current 30 days
                var currentCount = feedbacks
                    .Count(x => x.SubmissionDateTime.Date >= currentPeriodStart && x.SubmissionDateTime.Date <= today);

                double currentAvg = totalTeams > 0 ? currentCount / (double)totalTeams : 0;

                // Prior 30 days (day -60 to day -31)
                var priorCount = feedbacks
                    .Count(x => x.SubmissionDateTime.Date >= priorPeriodStart && x.SubmissionDateTime.Date < currentPeriodStart);

                double priorAvg = totalTeams > 0 ? priorCount / (double)totalTeams : 0;

                double change = currentAvg - priorAvg;

                return new ResponseRateSectionDto
                {
                    AvgResponsesPerTeam = Math.Round(currentAvg, 2),
                    TotalResponses = currentCount,
                    TotalTeams = totalTeams,
                    ChangeVsPrior = Math.Round(change, 2)
                };
            }
            catch
            {
                return new ResponseRateSectionDto();
            }
        }


        public async Task<EngagementSectionDto> GetEngagementSection(List<long> unitIds)
        {
            var today = DateTime.UtcNow.Date;

            if (unitIds == null || !unitIds.Any())
                return new EngagementSectionDto();

            var currentPeriodStart = today.AddDays(-30);
            var priorPeriodStart = today.AddDays(-60);

            var feedbacks = await _context.DailyFeedbacks
                .Where(x => unitIds.Contains(x.BusinessUnitId)
                    && x.SubmissionDateTime.Date >= priorPeriodStart
                    && x.SubmissionDateTime.Date <= today)
                .ToListAsync();

            double CalculateAverageScore(IEnumerable<DailyFeedback> items)
            {
                if (!items.Any()) return 0;

                return Math.Round(items
                    .Select(x =>
                        x.SentimentType switch
                        {
                            CloverBackoffice.Infrastructure.Models.DailyFeedbackSentimentType.Angry => 1,
                            CloverBackoffice.Infrastructure.Models.DailyFeedbackSentimentType.Sad => 34,
                            CloverBackoffice.Infrastructure.Models.DailyFeedbackSentimentType.Indifferent => 67,
                            CloverBackoffice.Infrastructure.Models.DailyFeedbackSentimentType.Happy => 100,
                            _ => 1
                        })
                    .Average(), 2);
            }

            // Current 30 days
            var currentFeedbacks = feedbacks
                .Where(x => x.SubmissionDateTime.Date >= currentPeriodStart && x.SubmissionDateTime.Date <= today);

            var currentScore = CalculateAverageScore(currentFeedbacks);

            // Prior 30 days (day -60 to day -31)
            var priorFeedbacks = feedbacks
                .Where(x => x.SubmissionDateTime.Date >= priorPeriodStart && x.SubmissionDateTime.Date < currentPeriodStart);

            var priorScore = CalculateAverageScore(priorFeedbacks);

            double change = Math.Round(currentScore - priorScore, 2);

            return new EngagementSectionDto
            {
                AvgTrendScore = currentScore,
                ChangeVsPrior = change
            };
        }

        public async Task<NeedsAttentionSectionDto> GetNeedsAttentionSection(List<long> unitIds)
        {
            if (unitIds == null || !unitIds.Any())
                return new NeedsAttentionSectionDto();

            var castUnitIds = unitIds.Select(id => (uint)id).ToList();

            // Get active manager assignments per unit
            var unitsWithManagers = await _context.UserTeams
                .Where(ut => castUnitIds.Contains(ut.UnitId) && !ut.IsUnAssigned)
                .Select(ut => ut.UnitId)
                .Distinct()
                .ToListAsync();

            var noManagerCount = castUnitIds.Count(id => !unitsWithManagers.Contains(id));

            // For units WITH a manager, check last feedback
            var unitsWithManagerIds = castUnitIds.Where(id => unitsWithManagers.Contains(id)).ToList();
            var cutoff48h = DateTime.UtcNow.AddHours(-48);

            var noResponseIn48hCount = 0;
            if (unitsWithManagerIds.Any())
            {
                var unitsWithRecentFeedback = await _context.DailyFeedbacks
                    .Where(df => unitsWithManagerIds.Contains(df.BusinessUnitId)
                        && df.SubmissionDateTime >= cutoff48h)
                    .Select(df => df.BusinessUnitId)
                    .Distinct()
                    .ToListAsync();

                noResponseIn48hCount = unitsWithManagerIds.Count(id => !unitsWithRecentFeedback.Contains(id));
            }

            // Action Hub inactivity (30+ days)
            var cutoff30d = DateTime.UtcNow.AddDays(-30);
            var unitsWithRecentActionHub = await _context.ActionHubs
                .Where(ah => !ah.IsDel && castUnitIds.Contains(ah.UnitId) && ah.UpdatedAt >= cutoff30d)
                .Select(ah => ah.UnitId)
                .Distinct()
                .ToListAsync();

            var noActionHubUpdateCount = castUnitIds.Count(id => !unitsWithRecentActionHub.Contains(id));

            return new NeedsAttentionSectionDto
            {
                NeedsAttentionCount = noManagerCount + noResponseIn48hCount + noActionHubUpdateCount,
                NoManagerCount = noManagerCount,
                NoResponseIn48hCount = noResponseIn48hCount,
                NoActionHubUpdateCount = noActionHubUpdateCount
            };
        }

        public async Task<User> GetUserByIdAsync(int userId)
        {
            return await _context.Users
                .Where(u => u.UserId == userId && u.IsActive && !u.IsDel)
                .FirstOrDefaultAsync();
        }

        public async Task<ActionHubActivitiesDto> GetActionHubActivities(User user, List<long> accessibleUnitIds)
        {
            // 1️ Load AuditLogs with relevant endpoints
            IQueryable<AuditLogs> query = _context.AuditLogs
                .Where(x =>
                    x.Endpoint == "/api/actionhub/upsert-action-event" ||
                    x.Endpoint == "/api/actionhub/upsert-action-hub" ||
                    x.Endpoint == "/api/teamhierarchy/upsert-team" ||
                    x.Endpoint.StartsWith("/api/teamhierarchy/team/") ||
                    x.Endpoint == "/api/teamhierarchy/assign-user-to-team"
                );

            // A RRIDA Admin reads every profile's logs; everyone else is narrowed to
            // the profiles behind the units they can reach.
            if (user.UserGroupId != (int)UserGroups.RridaAdmin)
            {
                var buProfileIds = await _context.BusinessUnits
                    .Where(x => accessibleUnitIds.Contains((long)x.Id))
                    .Select(x => (int)x.Profile.Id)
                    .Distinct()
                    .ToListAsync();

                var allowedUserIds = await _context.Users
                    .Where(u => u.ProfileId.HasValue && buProfileIds.Contains(u.ProfileId.Value))
                    .Select(u => u.UserId)
                    .ToListAsync();

                query = query.Where(x => allowedUserIds.Contains(x.UserId ?? 0));
            }

            // Load logs
            var rawLogs = await query
                .OrderByDescending(x => x.Timestamp)
                .Take(400)
                .ToListAsync();

            // Filter only successful logs
            var successLogs = rawLogs
                .Where(log =>
                {
                    if (string.IsNullOrWhiteSpace(log.ResponseData)) return false;
                    try
                    {
                        var json = JObject.Parse(log.ResponseData);
                        return json["isSuccess"]?.Value<bool>() ?? false;
                    }
                    catch { return false; }
                })
                .ToList();

            // 2️ ENRICH ActionHub logs
            var actionEventLogs = successLogs
                .Where(x => x.Endpoint == "/api/actionhub/upsert-action-event")
                .ToList();

            var actionHubIds = actionEventLogs
                .SelectMany(log =>
                {
                    try
                    {
                        var json = JObject.Parse(log.ResponseData);
                        var content = json["content"];
                        if (content != null && int.TryParse(content["actionHubId"]?.ToString(), out var ahid))
                            return new[] { ahid };
                    }
                    catch { }
                    return Array.Empty<int>();
                })
                .ToHashSet();

            var actionHubs = await _context.ActionHubs
                .Where(a => actionHubIds.Contains(a.Id))
                .Select(a => new { a.Id, a.ProfileId, a.UnitId })
                .ToListAsync();

            var profileIds = actionHubs.Select(a => a.ProfileId).Distinct().ToList();
            var unitIds = actionHubs.Select(a => a.UnitId).Distinct().ToList();

            var companyNames = await _context.Profiles
                .Where(c => profileIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.ProfileName);

            var unitNames = await _context.BusinessUnits
                .Where(b => unitIds.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id, b => b.Name);

            var actionHubInfo = actionHubs.ToDictionary(
                a => a.Id,
                a => new
                {
                    a.ProfileId,
                    CompanyName = companyNames.ContainsKey(a.ProfileId) ? companyNames[a.ProfileId] : null,
                    a.UnitId,
                    UnitName = unitNames.ContainsKey(a.UnitId) ? unitNames[a.UnitId] : null
                }
            );

            foreach (var log in actionEventLogs)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(log.ResponseData)) continue;

                    var json = JObject.Parse(log.ResponseData);
                    var content = json["content"] as JObject;
                    if (content == null) continue;

                    if (!int.TryParse(content["actionHubId"]?.ToString(), out var ahid) || ahid <= 0)
                        continue;

                    if (!actionHubInfo.TryGetValue(ahid, out var info))
                        continue;

                    content["companyId"] = info.ProfileId;
                    content["companyName"] = info.CompanyName ?? string.Empty;
                    content["unitId"] = info.UnitId;
                    content["unitName"] = info.UnitName ?? string.Empty;
                    log.ResponseData = json.ToString();
                }
                catch
                {
                    // Log error if needed, continue processing other logs
                    continue;
                }
            }
            // 3 ENRICH /upsert-action-hub logs (NO message overwrite)
            var upsertActionHubLogs = successLogs
                .Where(x => x.Endpoint == "/api/actionhub/upsert-action-hub")
                .ToList();

            foreach (var log in upsertActionHubLogs)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(log.ResponseData))
                        continue;

                    var json = JObject.Parse(log.ResponseData);

                    if (json["content"]?.Type != JTokenType.Object)
                        json["content"] = new JObject();

                    var content = json["content"] as JObject ?? new JObject();

                    // Fill missing company name
                    if (content["companyName"] == null && content["companyId"] != null)
                    {
                        int profileId = content["companyId"].Value<int>();
                        var companyName = await _context.Profiles
                            .Where(c => c.Id == profileId)
                            .Select(c => c.ProfileName)
                            .FirstOrDefaultAsync();

                        content["companyName"] = companyName ?? string.Empty;
                    }

                    // Fill missing unit name
                    if (content["unitName"] == null && content["unitId"] != null)
                    {
                        int unitId = content["unitId"].Value<int>();
                        var unitName = await _context.BusinessUnits
                            .Where(u => u.Id == unitId)
                            .Select(u => u.Name)
                            .FirstOrDefaultAsync();

                        content["unitName"] = unitName ?? string.Empty;
                    }

                    json["content"] = content;

                    log.ResponseData = json.ToString();
                }
                catch
                {
                    continue;
                }
            }



            // 4 ENRICH assign-user-to-team logs
            var assignLogs = successLogs
                .Where(x => x.Endpoint == "/api/teamhierarchy/assign-user-to-team")
                .ToList();

            var extractedUserIds = new HashSet<int>();
            var extractedTeamIds = new HashSet<uint>();
            foreach (var log in assignLogs)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(log.ResponseData)) continue;
                    var json = JObject.Parse(log.ResponseData);
                    var content = json["content"];
                    if (content != null)
                    {
                        if (int.TryParse(content["userId"]?.ToString(), out var uid)) extractedUserIds.Add(uid);
                        if (uint.TryParse(content["teamId"]?.ToString(), out var tid)) extractedTeamIds.Add(tid);
                    }
                }
                catch { }
            }

            var userNames = await _context.Users
                .Where(u => extractedUserIds.Contains(u.UserId))
                .ToDictionaryAsync(u => u.UserId, u => u.Name);

            var teamInfos = await _context.BusinessUnits
                .Where(b => extractedTeamIds.Contains(b.Id))
                .Select(b => new { b.Id, b.Name, ProfileId = (uint)b.Profile.Id })
                .ToListAsync();

            var companyDict = await _context.Profiles
                .Where(c => teamInfos.Select(t => t.ProfileId).Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.ProfileName);

            var teamDict = teamInfos.ToDictionary(
                t => t.Id,
                t => new
                {
                    Name = CleanTeamName(t.Name),
                    t.ProfileId,
                    CompanyName = companyDict.ContainsKey(t.ProfileId) ? companyDict[t.ProfileId] : string.Empty
                }
            );

            foreach (var log in assignLogs)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(log.ResponseData)) continue;

                    var json = JObject.Parse(log.ResponseData);
                    var content = json["content"] as JObject;
                    if (content == null) continue;

                    int.TryParse(content["userId"]?.ToString(), out var uid);
                    uint.TryParse(content["teamId"]?.ToString(), out var tid);

                    if (uid > 0 && userNames.TryGetValue(uid, out var userName))
                    {
                        content["userName"] = userName ?? string.Empty;
                    }

                    if (tid > 0 && teamDict.TryGetValue(tid, out var teamInfo))
                    {
                        content["teamName"] = teamInfo.Name ?? string.Empty;
                        content["companyId"] = teamInfo.ProfileId;
                        content["companyName"] = teamInfo.CompanyName ?? string.Empty;
                    }

                    log.ResponseData = json.ToString();
                }
                catch
                {
                    // Log error if needed, continue processing other logs
                    continue;
                }
            }

            // 5 ENRICH /team/* logs from endpoint
            var teamLogs = successLogs
                .Where(x => x.Endpoint.StartsWith("/api/teamhierarchy/team/"))
                .ToList();

            var unitIdsFromEndpoint = teamLogs
                .Select(log =>
                {
                    try
                    {
                        var parts = log.Endpoint.Split('/');
                        return parts.Length >= 5 && uint.TryParse(parts[4], out var uid) ? uid : (uint)0;
                    }
                    catch { return (uint)0; }
                })
                .Where(id => id > 0)
                .ToHashSet();

            var businessUnits = await _context.BusinessUnits
                .Where(b => unitIdsFromEndpoint.Contains(b.Id))
                .Select(b => new { b.Id, b.Name, ProfileId = (uint)b.Profile.Id })
                .ToListAsync();

            var companies = await _context.Profiles
                .Where(c => businessUnits.Select(b => b.ProfileId).Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.ProfileName);

            var unitInfo = businessUnits.ToDictionary(
                b => b.Id,
                b => new { Name = CleanTeamName(b.Name), b.ProfileId, CompanyName = companies.ContainsKey(b.ProfileId) ? companies[b.ProfileId] : string.Empty }
            );

            var teamLogUserIds = teamLogs
                .Where(x => x.UserId.HasValue)
                .Select(x => x.UserId ?? 0)
                .Where(id => id > 0)
                .ToHashSet();

            var teamLogUserNames = await _context.Users
                .Where(u => teamLogUserIds.Contains(u.UserId))
                .ToDictionaryAsync(u => u.UserId, u => u.Name);

            foreach (var log in teamLogs)
            {
                try
                {
                    var parts = log.Endpoint.Split('/');
                    if (parts.Length < 5) continue;
                    if (!uint.TryParse(parts[4], out var unitId)) continue;

                    JObject json;
                    if (string.IsNullOrWhiteSpace(log.ResponseData))
                    {
                        json = new JObject();
                    }
                    else
                    {
                        try { json = JObject.Parse(log.ResponseData); } catch { json = new JObject(); }
                    }

                    if (unitInfo.TryGetValue(unitId, out var unitVal))
                    {
                        if (json["content"]?.Type != JTokenType.Object)
                            json["content"] = new JObject();

                        var contentObj = json["content"] as JObject ?? new JObject();
                        contentObj["unitId"] = unitId;
                        contentObj["teamName"] = unitVal.Name ?? string.Empty;
                        contentObj["companyId"] = unitVal.ProfileId;
                        contentObj["companyName"] = unitVal.CompanyName ?? string.Empty;
                        json["content"] = contentObj;
                    }

                    if (log.UserId.HasValue && teamLogUserNames.TryGetValue(log.UserId.Value, out var userName))
                    {
                        if (json["content"]?.Type != JTokenType.Object)
                            json["content"] = new JObject();

                        var contentObj = json["content"] as JObject ?? new JObject();
                        contentObj["userId"] = log.UserId.Value;
                        contentObj["userName"] = userName ?? string.Empty;
                        json["content"] = contentObj;
                    }

                    log.ResponseData = json.ToString();
                }
                catch { }
            }

            // 6 ENRICH /upsert-team logs
            var upsertTeamLogs = successLogs
                .Where(x => x.Endpoint == "/api/teamhierarchy/upsert-team")
                .ToList();

            foreach (var log in upsertTeamLogs)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(log.ResponseData))
                        continue;

                    var json = JObject.Parse(log.ResponseData);

                    // Ensure content exists
                    if (json["content"]?.Type != JTokenType.Object)
                        json["content"] = new JObject();

                    var contentObj = json["content"] as JObject ?? new JObject();

                    // Enrich ONLY if missing
                    if (contentObj["companyName"] == null && contentObj["companyId"] != null)
                    {
                        uint profileId = contentObj["companyId"].Value<uint>();
                        string? companyName = await _context.Profiles
                            .Where(c => c.Id == profileId)
                            .Select(c => c.ProfileName)
                            .FirstOrDefaultAsync();

                        contentObj["companyName"] = companyName ?? string.Empty;
                    }

                    json["content"] = contentObj;

                    // IMPORTANT: Preserve original message
                    log.ResponseData = json.ToString();
                }
                catch
                {
                    continue;
                }
            }


            // 7 FINAL OUTPUT
            var lastActions = successLogs
                 .Where(x =>
                     x.Endpoint == "/api/actionhub/upsert-action-event" ||
                     x.Endpoint == "/api/actionhub/upsert-action-hub"
                 )
                 .OrderByDescending(x => x.Timestamp)
                 .Take(3)
                 .Select(x => new ActionHubActivityDto
                 {
                     ResponseDataJson = x.ResponseData
                 })
                 .ToList();


            var recentTeams = successLogs
                .Where(x =>
                    x.Endpoint == "/api/teamhierarchy/upsert-team" ||
                    x.Endpoint.StartsWith("/api/teamhierarchy/team/") ||
                    x.Endpoint == "/api/teamhierarchy/assign-user-to-team"
                )
                .OrderByDescending(x => x.Timestamp)
                .Take(3)
                .Select(x => new RecentTeamDto { ResponseDataJson = x.ResponseData })
                .ToList();

            return new ActionHubActivitiesDto
            {
                LastActionHubActivities = lastActions,
                RecentlyTeamsActivities = recentTeams
            };
        }

        public CategoryDetailDto GetCategoryDetail(List<DailyFeedbackReportRowDto> feedbackRows, List<long> unitIds, int cloverCategory, DateTime requestedFromDate, DateTime requestedToDate)
        {
            var categoryName = ((CloverBackoffice.Components.Enums.CloverCategory)cloverCategory).ToString();

            // Filter feedback to only the requested category
            var categoryFeedback = feedbackRows
                .Where(r => r.SpecificationCloverCategory.HasValue && r.SpecificationCloverCategory.Value == cloverCategory)
                .ToList();

            // Calculate trend chart for this category (reuses existing method)
            var trendChart = CalculateTrendChartFromFeedback(categoryFeedback, unitIds, requestedFromDate, requestedToDate);

            // Calculate category score (current period only)
            var analysisResults = CalculateFeedbackAnalysis(categoryFeedback, requestedFromDate, requestedToDate, unitIds);
            var currentPeriodResults = analysisResults
                .Where(x => x.AnalysisDate >= requestedFromDate && x.AnalysisDate <= requestedToDate)
                .ToList();

            double score = 0;
            if (currentPeriodResults.Any())
            {
                score = Math.Round(currentPeriodResults.Average(x =>
                    (x.AngryPercentage * 1 +
                     x.SadPercentage * 34 +
                     x.IndifferentPercentage * 67 +
                     x.HappyPercentage * 100) / 100.0), 2);
            }

            // Calculate response rate for this category (current vs previous period)
            var currentPeriodCount = categoryFeedback.Count(r =>
                unitIds.Contains((long)r.BusinessUnitId) &&
                r.SubmissionDateTime.UtcDateTime >= requestedFromDate &&
                r.SubmissionDateTime.UtcDateTime <= requestedToDate);

            var previousPeriodStart = requestedFromDate - (requestedToDate - requestedFromDate);
            var previousPeriodCount = categoryFeedback.Count(r =>
                unitIds.Contains((long)r.BusinessUnitId) &&
                r.SubmissionDateTime.UtcDateTime >= previousPeriodStart &&
                r.SubmissionDateTime.UtcDateTime < requestedFromDate);

            string changeStr = "N/A";
            string changeType = "neutral";

            if (previousPeriodCount == 0 && currentPeriodCount > 0)
            {
                changeStr = "+100% vs previous period";
                changeType = "positive";
            }
            else if (previousPeriodCount > 0)
            {
                var change = ((double)(currentPeriodCount - previousPeriodCount) / previousPeriodCount) * 100;
                var cappedChange = Math.Max(-100, Math.Min(100, change));
                changeStr = cappedChange >= 0
                    ? $"+{Math.Abs(cappedChange):0.0}% vs previous period"
                    : $"-{Math.Abs(cappedChange):0.0}% vs previous period";
                changeType = cappedChange >= 0 ? "positive" : "negative";
            }

            return new CategoryDetailDto
            {
                Category = categoryName,
                Score = score,
                Status = GetStatusFromScore(score),
                TrendChart = trendChart,
                ResponseRate = new CategoryResponseRateDto
                {
                    CurrentPeriodCount = currentPeriodCount,
                    PreviousPeriodCount = previousPeriodCount,
                    Change = changeStr,
                    ChangeType = changeType
                }
            };
        }

        // Helper to clean team name prefix
        private string CleanTeamName(string teamName)
        {
            if (string.IsNullOrWhiteSpace(teamName)) return teamName;
            var idx = teamName.IndexOf('_');
            if (idx < 0) return teamName;
            // Remove everything before last '_' in prefix format del_YYYYMMDD_
            var parts = teamName.Split('_');
            if (parts.Length >= 3)
            {
                return string.Join("_", parts.Skip(2));
            }
            return teamName;
        }
    }
}
