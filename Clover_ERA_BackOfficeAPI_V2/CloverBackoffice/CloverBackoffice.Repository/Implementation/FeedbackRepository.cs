using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SendGrid.Helpers.Mail;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;

namespace CloverBackoffice.Repository.Implementation
{
    public class FeedbackRepository : IFeedbackRepository
    {
        private readonly ApplicationDbContext _context;
        private readonly FeedbackScoreThresholdsOptions _thresholds;
        private readonly IWebHostEnvironment _env;

        public FeedbackRepository(ApplicationDbContext context, IOptions<FeedbackScoreThresholdsOptions> thresholds, IWebHostEnvironment env)
        {
            _context = context;
            _thresholds = thresholds.Value;
            _env = env;

        }

        public async Task<DashboardSummaryDto> GetDailyFeedbackReportAsync(long unitId, DateTimeOffset fromDate, DateTimeOffset toDate)
        {
            if (fromDate != null && toDate != null)
            {
                fromDate = fromDate.UtcDateTime;
                toDate = toDate.UtcDateTime;
            }

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
             LEFT JOIN ""DailyQuestions"" dq 
                 ON df.""DailyQuestionId"" = dq.""Id""
             LEFT JOIN ""DailyReasons"" dr 
                 ON df.""DailyReasonId"" = dr.""Id""
             LEFT JOIN ""DailyQuestionSpecifications"" dqs 
                 ON dr.""SpecificationId"" = dqs.""Id""
             WHERE 
                 df.""BusinessUnitId"" = @UnitId
                 AND df.""SubmissionDateTime"" BETWEEN @FromDate AND @ToDate;
             ";

            var result = await _context.DailyFeedbackReportRowDtos
                .FromSqlRaw(query,
                    new Npgsql.NpgsqlParameter("@UnitId", unitId),
                    new Npgsql.NpgsqlParameter("@FromDate", fromDate),
                    new Npgsql.NpgsqlParameter("@ToDate", toDate))
                .ToListAsync();
            var resp = await AnalyzeAndSaveFeedbackInternal(result, unitId);
            return resp;
        }


        public async Task<List<DailyFeedbackReportRowDto>> GetDailyFeedbackReportAsync2(long unitId, int days)
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
            LEFT JOIN ""DailyQuestions"" dq 
                ON df.""DailyQuestionId"" = dq.""Id""
            LEFT JOIN ""DailyReasons"" dr 
                ON df.""DailyReasonId"" = dr.""Id""
            LEFT JOIN ""DailyQuestionSpecifications"" dqs 
                ON dr.""SpecificationId"" = dqs.""Id""
            WHERE 
                df.""BusinessUnitId"" = @UnitId
                AND df.""SubmissionDateTime"" >= CURRENT_DATE - (@Days * INTERVAL '1 day');
             ";

            var result = await _context.DailyFeedbackReportRowDtos
                .FromSqlRaw(query,
                    new Npgsql.NpgsqlParameter("@UnitId", unitId),
                    new Npgsql.NpgsqlParameter("@Days", days)
                )
                .ToListAsync();

            return result;
        }

        public async Task SaveTopThemesAsync(IEnumerable<TopTheme> themes)
        {
            await _context.TopThemes.AddRangeAsync(themes);
            await _context.SaveChangesAsync();
        }

        public async Task SaveTopRecommendationsAsync(IEnumerable<TopRecommendation> recommendations)
        {
            await _context.TopRecommendations.AddRangeAsync(recommendations);
            await _context.SaveChangesAsync();
        }
        public async Task SaveTopActionsAsync(IEnumerable<TopAction> actions)
        {
            await _context.TopActions.AddRangeAsync(actions);
            await _context.SaveChangesAsync();
        }
        public async Task<List<DailyFeedbackReportRowDto>> GetDailyFeedbackReportAsyncRuntime(long unitId, DateTimeOffset fromDate, DateTimeOffset toDate)
        {
            if (fromDate != null && toDate != null)
            {
                fromDate = fromDate.UtcDateTime;
                toDate = toDate.UtcDateTime;
            }

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
        LEFT JOIN ""DailyQuestions"" dq 
            ON df.""DailyQuestionId"" = dq.""Id""
        LEFT JOIN ""DailyReasons"" dr 
            ON df.""DailyReasonId"" = dr.""Id""
        LEFT JOIN ""DailyQuestionSpecifications"" dqs 
            ON dr.""SpecificationId"" = dqs.""Id""
        WHERE 
            df.""BusinessUnitId"" = @UnitId
            AND df.""SubmissionDateTime"" BETWEEN @FromDate AND @ToDate;
    ";

            var result = await _context.DailyFeedbackReportRowDtos
                .FromSqlRaw(query,
                    new Npgsql.NpgsqlParameter("@UnitId", unitId),
                    new Npgsql.NpgsqlParameter("@FromDate", fromDate),
                    new Npgsql.NpgsqlParameter("@ToDate", toDate))
                .ToListAsync();
            await AnalyzeAndSaveFeedbackInternal(result, unitId);
            return result;
        }

        private async Task<DashboardSummaryDto> AnalyzeAndSaveFeedbackInternal(List<DailyFeedbackReportRowDto> feedbackRows, long unitId)
        {
            var analysisDate = DateTime.UtcNow;
            var submissionDate = feedbackRows.FirstOrDefault() != null ? feedbackRows.First().SubmissionDateTime.UtcDateTime : DateTime.UtcNow;
            var analysisResults = CalculateFeedbackAnalysis(feedbackRows, analysisDate, submissionDate, (uint)unitId);
            var dashboard = await GetDashboardSummaryFromAnalysisAsync(unitId, analysisResults, feedbackRows);
            return dashboard;
        }
        public List<FeedbackAnalysisResult> CalculateFeedbackAnalysis(
        List<DailyFeedbackReportRowDto> feedbackRows, DateTime analysisDate, DateTime submissionDate, uint businessUnitId)
        {
            var analysisResults = new List<FeedbackAnalysisResult>();

            // Group by category (SpecificationCloverCategory) - this is int? key
            var groupedByCategory = feedbackRows
                .GroupBy(r => r.SpecificationCloverCategory)
                .ToList();

            foreach (var categoryGroup in groupedByCategory)
            {
                var category = categoryGroup.Key; // int?

                if (!category.HasValue || !Enum.IsDefined(typeof(CloverCategory), category.Value))
                {
                    continue;
                }
                // Safe to assign here
                string categoryName = ((CloverCategory)category.Value).ToString();

                var totalCount = categoryGroup.Count();
                if (totalCount == 0) continue;

                double angry = categoryGroup.Count(r => r.SentimentType == (int)DailyFeedbackSentimentType.Angry);
                double sad = categoryGroup.Count(r => r.SentimentType == (int)DailyFeedbackSentimentType.Sad);
                double indifferent = categoryGroup.Count(r => r.SentimentType == (int)DailyFeedbackSentimentType.Indifferent);
                double happy = categoryGroup.Count(r => r.SentimentType == (int)DailyFeedbackSentimentType.Happy);

                analysisResults.Add(new FeedbackAnalysisResult
                {
                    AnalysisDate = analysisDate,
                    SubmissionDate = submissionDate,
                    CategoryName = categoryName,
                    AngryPercentage = Math.Round((angry / totalCount) * 100, 2),
                    SadPercentage = Math.Round((sad / totalCount) * 100, 2),
                    IndifferentPercentage = Math.Round((indifferent / totalCount) * 100, 2),
                    HappyPercentage = Math.Round((happy / totalCount) * 100, 2),
                    BusinessUnitId = businessUnitId
                });
            }

            // Add missing categories with 0% if no feedback was present
            foreach (CloverCategory cat in Enum.GetValues(typeof(CloverCategory)))
            {
                string catName = cat.ToString();
                if (analysisResults.All(r => r.CategoryName != catName))
                {
                    analysisResults.Add(new FeedbackAnalysisResult
                    {
                        AnalysisDate = analysisDate,
                        SubmissionDate = submissionDate,
                        CategoryName = catName,
                        AngryPercentage = 0,
                        SadPercentage = 0,
                        IndifferentPercentage = 0,
                        HappyPercentage = 0,
                        BusinessUnitId = businessUnitId
                    });
                }
            }

            return analysisResults;
        }

        public async Task<DashboardSummaryDto> GetDashboardSummaryAsync(long unitId, int daysBack)
        {
            var result = new DashboardSummaryDto();

            using var conn = _context.Database.GetDbConnection();
            await conn.OpenAsync();

            // QUERY 1: Overall score
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
     SELECT 
         ROUND(AVG((
             ""AngryPercentage"" * 1 +
             ""SadPercentage"" * 34 +
             ""IndifferentPercentage"" * 67 +
             ""HappyPercentage"" * 100
         ) / 100.0)::numeric, 2) AS Value
     FROM public.""FeedbackAnalysisResults""
     WHERE 
         ""AnalysisDate"" >= date_trunc('month', CURRENT_DATE) - INTERVAL '5 months'
         AND ""BusinessUnitId"" = @unitId";
                AddParameter(cmd, "@unitId", unitId);


                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    double value = reader.IsDBNull(0) ? 0 : reader.GetDouble(0);
                    if (value > 0)   // only add if not zero
                    {
                        result.OverallScore = new OverallScoreDto
                        {
                            Value = value,
                            Status = GetStatusFromScore(value)
                        };
                    }
                }
            }

            // QUERY 2: Trend chart
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
 SELECT 
     TO_CHAR(date_trunc('month', ""AnalysisDate""), 'Mon') AS Name,
     ROUND(
         AVG((
             ""AngryPercentage"" * 1 +
             ""SadPercentage"" * 34 +
             ""IndifferentPercentage"" * 67 +
             ""HappyPercentage"" * 100
         ) / 100.0)::numeric, 2
     ) AS Score
 FROM public.""FeedbackAnalysisResults""
 WHERE 
     ""AnalysisDate"" >= date_trunc('month', CURRENT_DATE) - INTERVAL '5 months'
     AND ""BusinessUnitId"" = @unitId
 GROUP BY date_trunc('month', ""AnalysisDate"")
 ORDER BY date_trunc('month', ""AnalysisDate"")";

                AddParameter(cmd, "@unitId", unitId);

                using var reader = await cmd.ExecuteReaderAsync();
                var trends = new List<TrendChartEntryDto>();
                while (await reader.ReadAsync())
                {
                    trends.Add(new TrendChartEntryDto
                    {
                        Name = reader.GetString(0),
                        Score = reader.GetDouble(1)
                    });
                }

                // Only add TrendChart if there is at least one record AND not all scores are 0
                if (trends.Count > 0 && trends.Any(t => t.Score > 0))
                {
                    string monthlyChange = "N/A";
                    string sixMonthChange = "N/A";

                    if (trends.Count >= 2)
                    {
                        double lastScore = trends[^1].Score;
                        double prevScore = trends[^2].Score;
                        var change = lastScore - prevScore;
                        var percent = prevScore != 0 ? (change / prevScore) * 100 : 0;
                        monthlyChange = change >= 0
                            ? $"↗ {Math.Abs(percent):0.#}% vs last period"
                            : $"↘ {Math.Abs(percent):0.#}% vs last period";
                    }

                    if (trends.Count >= 2)
                    {
                        double lastScore = trends[^1].Score;
                        double firstScore = trends[0].Score;
                        var change = lastScore - firstScore;
                        var percent = firstScore != 0 ? (change / firstScore) * 100 : 0;
                        sixMonthChange = change >= 0
                            ? $"↗ {Math.Abs(percent):0.#}% vs first period"
                            : $"↘ {Math.Abs(percent):0.#}% vs first period";
                    }

                    result.TrendChart = new TrendChartDto
                    {
                        Data = trends,
                        PreviousData = new List<TrendChartEntryDto>(),
                        CurrentData = trends,
                        MonthlyChange = monthlyChange,
                        SixMonthChange = sixMonthChange
                    };
                }
            }


            // QUERY 3: Health Scores
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $@"
         SELECT 
             ""CategoryName"" AS Label,
             ROUND(AVG((
                 ""AngryPercentage"" * 1 + 
                 ""SadPercentage"" * 34 + 
                 ""IndifferentPercentage"" * 67 + 
                 ""HappyPercentage"" * 100
             ) / 100.0)::numeric, 2) AS Score
         FROM public.""FeedbackAnalysisResults""
         WHERE 
             ""AnalysisDate"" >= CURRENT_DATE - INTERVAL '{daysBack} days'
             AND ""BusinessUnitId"" = @unitId
         GROUP BY ""CategoryName""
         ORDER BY ""CategoryName"";";

                var unitIdParam = cmd.CreateParameter();
                unitIdParam.ParameterName = "@unitId";
                unitIdParam.Value = unitId;
                cmd.Parameters.Add(unitIdParam);

                using var reader = await cmd.ExecuteReaderAsync();
                var healthScores = new List<HealthScoreDto>();
                while (await reader.ReadAsync())
                {
                    var score = reader.GetDouble(1);
                    healthScores.Add(new HealthScoreDto
                    {
                        Label = reader.GetString(0),
                        Score = score,
                        Status = GetStatusFromScore(score)
                    });
                }
                if (healthScores.Any(h => h.Score > 0))   //at least one non-zero
                {
                    // Order by enum numeric value
                    healthScores = healthScores
                    .OrderBy(h => (int)Enum.Parse<CloverCategory>(h.Label))
                    .ToList();
                }
                result.HealthScores = healthScores;
            }

            // QUERY 4: Dynamic Metrics - Response Rate
            var metrics = new List<MetricDto>();

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $@"
         WITH feedback_counts AS (
             SELECT 
                 COUNT(*) FILTER (
                     WHERE date_trunc('month', ""SubmissionDateTime"") = date_trunc('month', CURRENT_DATE)
                 ) AS current_month_count,
                 COUNT(*) FILTER (
                     WHERE date_trunc('month', ""SubmissionDateTime"") = date_trunc('month', CURRENT_DATE - INTERVAL '1 month')
                 ) AS previous_month_count
             FROM public.""DailyFeedback""
             WHERE ""BusinessUnitId"" = @unitId
         ),
         daily_count AS (
             SELECT COUNT(*) AS recent_days_count
             FROM public.""DailyFeedback""
             WHERE 
                 ""BusinessUnitId"" = @unitId
                 AND ""SubmissionDateTime"" >= CURRENT_DATE - INTERVAL '{daysBack} days'
         )
         SELECT 
             d.recent_days_count,
             f.current_month_count,
             f.previous_month_count
         FROM feedback_counts f, daily_count d;";

                // Add unitId as a parameter (safe!)
                var unitIdParam = cmd.CreateParameter();
                unitIdParam.ParameterName = "@unitId";
                unitIdParam.Value = unitId;
                cmd.Parameters.Add(unitIdParam);

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    int recentDaysCount = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
                    int current = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                    int previous = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);

                    if (recentDaysCount > 0)   //only add metric if >0
                    {
                        string changeStr = "N/A";
                        string changeType = "neutral";
                        string statusLabel = "No Data";

                        if (previous == 0 && current > 0)
                        {
                            changeStr = "+∞% vs last month";
                            changeType = "positive";
                            statusLabel = "On Target";
                        }
                        else if (previous > 0)
                        {
                            double change = ((double)(current - previous) / previous) * 100;
                            changeStr = change >= 0
                                ? $"+{Math.Abs(change):0.0}% vs last month"
                                : $"-{Math.Abs(change):0.0}% vs last month";

                            changeType = change >= 0 ? "positive" : "negative";
                            statusLabel = change >= 0 ? "On Target" : "Needs Attention";
                        }

                        metrics.Add(new MetricDto
                        {
                            Title = "Responses",
                            Value = recentDaysCount,
                            Change = changeStr,
                            ChangeType = changeType,
                            Status = new MetricStatusDto { Label = statusLabel }
                        });
                    }
                }
            }

            result.Metrics = metrics;
            // if no metrics or all metrics have value = 0 → force default 0-values response
            if (result.Metrics == null || !result.Metrics.Any() || result.Metrics.All(m => m.Value == 0))
            {
                // Overall Score with 0
                result.OverallScore = new OverallScoreDto
                {
                    Value = 0,
                    Status = "Needs Work"
                };

                // TrendChart with 0
                var emptyTrendData = new List<TrendChartEntryDto>
                {
                    new TrendChartEntryDto
                    {
                        Name = DateTime.UtcNow.ToString("MMM yyyy"),
                        Score = 0
                    }
                };
                result.TrendChart = new TrendChartDto
                {
                    Data = emptyTrendData,
                    PreviousData = new List<TrendChartEntryDto>(),
                    CurrentData = emptyTrendData,
                    MonthlyChange = "N/A",
                    SixMonthChange = "N/A"
                };

                // HealthScores with 0
                result.HealthScores = new List<HealthScoreDto>
         {
             new HealthScoreDto { Label = "Communication", Score = 0, Status = "Excellent" },
             new HealthScoreDto { Label = "Learning", Score = 0, Status = "Excellent" },
             new HealthScoreDto { Label = "Opportunities", Score = 0, Status = "Urgent Attention" },
             new HealthScoreDto { Label = "Vulnerability", Score = 0, Status = "Good" },
             new HealthScoreDto { Label = "Enablement", Score = 0, Status = "Urgent Attention" },
             new HealthScoreDto { Label = "Reflection", Score = 0, Status = "Excellent" }
         };

                // Metrics with 0 / "No Data"
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

        public async Task<DashboardSummaryDto> GetDashboardDateRangeAsync(long unitId, DateTime fromDate, DateTime toDate)
        {
            var result = new DashboardSummaryDto();

            using var conn = _context.Database.GetDbConnection();
            await conn.OpenAsync();

            // QUERY 1: Overall score
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
 SELECT 
     ROUND(AVG((
         ""AngryPercentage"" * 1 +
         ""SadPercentage"" * 34 +
         ""IndifferentPercentage"" * 67 +
         ""HappyPercentage"" * 100
     ) / 100.0)::numeric, 2) AS Value
 FROM public.""FeedbackAnalysisResults""
 WHERE 
     ""AnalysisDate"" BETWEEN @fromDate AND @toDate
     AND ""BusinessUnitId"" = @unitId";

                AddParameter(cmd, "@unitId", unitId);
                AddParameter(cmd, "@fromDate", fromDate);
                AddParameter(cmd, "@toDate", toDate);

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    double value = reader.IsDBNull(0) ? 0 : reader.GetDouble(0);
                    if (value > 0)   // only add if not zero
                    {
                        result.OverallScore = new OverallScoreDto
                        {
                            Value = value,
                            Status = GetStatusFromScore(value)
                        };
                    }
                }
            }

            // QUERY 2: Trend chart
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
 SELECT 
     TO_CHAR(date_trunc('month', ""AnalysisDate""), 'Mon') AS Name,
     ROUND(
         AVG((
             ""AngryPercentage"" * 1 +
             ""SadPercentage"" * 34 +
             ""IndifferentPercentage"" * 67 +
             ""HappyPercentage"" * 100
         ) / 100.0)::numeric, 2
     ) AS Score
 FROM public.""FeedbackAnalysisResults""
 WHERE 
     ""AnalysisDate"" BETWEEN @fromDate AND @toDate
     AND ""BusinessUnitId"" = @unitId
 GROUP BY date_trunc('month', ""AnalysisDate"")
 ORDER BY date_trunc('month', ""AnalysisDate"")";

                AddParameter(cmd, "@unitId", unitId);
                AddParameter(cmd, "@fromDate", fromDate);
                AddParameter(cmd, "@toDate", toDate);

                using var reader = await cmd.ExecuteReaderAsync();
                var trends = new List<TrendChartEntryDto>();
                while (await reader.ReadAsync())
                {
                    trends.Add(new TrendChartEntryDto
                    {
                        Name = reader.GetString(0),
                        Score = reader.GetDouble(1)
                    });
                }

                if (trends.Count > 0 && trends.Any(t => t.Score > 0))
                {
                    string monthlyChange = "N/A";
                    string sixMonthChange = "N/A";

                    if (trends.Count >= 2)
                    {
                        double lastScore = trends[^1].Score;
                        double prevScore = trends[^2].Score;
                        var change = lastScore - prevScore;
                        var percent = prevScore != 0 ? (change / prevScore) * 100 : 0;
                        monthlyChange = change >= 0
                            ? $"↗ {Math.Abs(percent):0.#}% vs last period"
                            : $"↘ {Math.Abs(percent):0.#}% vs last period";
                    }

                    if (trends.Count >= 2)
                    {
                        double lastScore = trends[^1].Score;
                        double firstScore = trends[0].Score;
                        var change = lastScore - firstScore;
                        var percent = firstScore != 0 ? (change / firstScore) * 100 : 0;
                        sixMonthChange = change >= 0
                            ? $"↗ {Math.Abs(percent):0.#}% vs first period"
                            : $"↘ {Math.Abs(percent):0.#}% vs first period";
                    }

                    result.TrendChart = new TrendChartDto
                    {
                        Data = trends,
                        PreviousData = new List<TrendChartEntryDto>(),
                        CurrentData = trends,
                        MonthlyChange = monthlyChange,
                        SixMonthChange = sixMonthChange
                    };
                }
            }

            // QUERY 3: Health Scores
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
 SELECT 
     ""CategoryName"" AS Label,
     ROUND(AVG((
         ""AngryPercentage"" * 1 + 
         ""SadPercentage"" * 34 + 
         ""IndifferentPercentage"" * 67 + 
         ""HappyPercentage"" * 100
     ) / 100.0)::numeric, 2) AS Score
 FROM public.""FeedbackAnalysisResults""
 WHERE 
     ""AnalysisDate"" BETWEEN @fromDate AND @toDate
     AND ""BusinessUnitId"" = @unitId
 GROUP BY ""CategoryName""
 ORDER BY ""CategoryName"";";

                AddParameter(cmd, "@unitId", unitId);
                AddParameter(cmd, "@fromDate", fromDate);
                AddParameter(cmd, "@toDate", toDate);

                using var reader = await cmd.ExecuteReaderAsync();
                var healthScores = new List<HealthScoreDto>();
                while (await reader.ReadAsync())
                {
                    var score = reader.GetDouble(1);
                    healthScores.Add(new HealthScoreDto
                    {
                        Label = reader.GetString(0),
                        Score = score,
                        Status = GetStatusFromScore(score)
                    });
                }

                if (healthScores.Any(h => h.Score > 0))   // only keep if some >0
                {
                    healthScores = healthScores
                        .OrderBy(h => (int)Enum.Parse<CloverCategory>(h.Label))
                        .ToList();
                    result.HealthScores = healthScores;
                }
            }

            // QUERY 4: Metrics
            var metrics = new List<MetricDto>();

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
     WITH feedback_counts AS (
         SELECT 
             COUNT(*) FILTER (
                 WHERE date_trunc('month', ""SubmissionDateTime"") = date_trunc('month', CURRENT_DATE)
             ) AS current_month_count,
             COUNT(*) FILTER (
                 WHERE date_trunc('month', ""SubmissionDateTime"") = date_trunc('month', CURRENT_DATE - INTERVAL '1 month')
             ) AS previous_month_count
         FROM public.""DailyFeedback""
         WHERE ""BusinessUnitId"" = @unitId
     ),
     daily_count AS (
         SELECT COUNT(*) AS recent_days_count
         FROM public.""DailyFeedback""
         WHERE 
             ""BusinessUnitId"" = @unitId
             AND ""SubmissionDateTime"" BETWEEN @fromDate AND @toDate
     )
     SELECT 
         d.recent_days_count,
         f.current_month_count,
         f.previous_month_count
     FROM feedback_counts f, daily_count d;";

                AddParameter(cmd, "@unitId", unitId);
                AddParameter(cmd, "@fromDate", fromDate);
                AddParameter(cmd, "@toDate", toDate);

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    int recentDaysCount = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
                    int current = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                    int previous = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);

                    if (recentDaysCount > 0)   // only add metric if >0
                    {
                        string changeStr = "N/A";
                        string changeType = "neutral";
                        string statusLabel = "No Data";

                        if (previous == 0 && current > 0)
                        {
                            changeStr = "+∞% vs last month";
                            changeType = "positive";
                            statusLabel = "On Target";
                        }
                        else if (previous > 0)
                        {
                            double change = ((double)(current - previous) / previous) * 100;
                            changeStr = change >= 0
                                ? $"+{Math.Abs(change):0.0}% vs last month"
                                : $"-{Math.Abs(change):0.0}% vs last month";

                            changeType = change >= 0 ? "positive" : "negative";
                            statusLabel = change >= 0 ? "On Target" : "Needs Attention";
                        }

                        metrics.Add(new MetricDto
                        {
                            Title = "Responses",
                            Value = recentDaysCount,
                            Change = changeStr,
                            ChangeType = changeType,
                            Status = new MetricStatusDto { Label = statusLabel }
                        });
                    }
                }
            }

            result.Metrics = metrics;

            // Fallback defaults
            if (result.Metrics == null || !result.Metrics.Any() || result.Metrics.All(m => m.Value == 0))
            {
                result.OverallScore = new OverallScoreDto { Value = 0, Status = "Needs Work" };
                var emptyTrendData = new List<TrendChartEntryDto>
                {
                    new TrendChartEntryDto { Name = DateTime.UtcNow.ToString("MMM yyyy"), Score = 0 }
                };
                result.TrendChart = new TrendChartDto
                {
                    Data = emptyTrendData,
                    PreviousData = new List<TrendChartEntryDto>(),
                    CurrentData = emptyTrendData,
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
        private void AddParameter(DbCommand cmd, string name, object value)
        {
            var param = cmd.CreateParameter();
            param.ParameterName = name;
            param.Value = value ?? DBNull.Value;
            cmd.Parameters.Add(param);
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
        public async Task<bool> BusinessUnitExistsAsync(long unitId)
        {
            return await _context.BusinessUnits.AnyAsync(bu => bu.Id == unitId);
        }
        public async Task<List<TopRecommendation>> GetTopRecommendationsByUnitIdAsync(long unitId)
        {
            return await _context.TopRecommendations
                .Where(r => r.UnitId == unitId && r.ReportId > 0)
                .OrderByDescending(r => r.CreatedAt)
                .Take(3)
                .ToListAsync();
        }

        public async Task<List<TopTheme>> GetTopThemesByUnitIdAsync(long unitId)
        {
            var themes = await _context.TopThemes
                .Where(t => t.UnitId == unitId && t.ReportId > 0)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync(); // fetch to memory

            return themes
                .GroupBy(t => t.Theme)
                .Select(g => g.First()) // now safe in memory
                .OrderByDescending(t => t.CreatedAt)
                .Take(5)
                .ToList();
        }

        public async Task<List<TopAction>> GetTopActionsByUnitIdAsync(long unitId)
        {
            return await _context.TopActions
                .Where(a => a.UnitId == unitId && a.ReportId > 0)
                .OrderByDescending(a => a.CreatedAt)
                .Take(3)
                .ToListAsync();
        }
        public async Task<List<CompanyUnitDto>> GetAllCompanyUnitPairsAsync()
        {
            return await _context.Profiles
            .Include(c => c.BusinessUnits)
            .SelectMany(c => c.BusinessUnits!.DefaultIfEmpty(), (c, bu) => new CompanyUnitDto
            {
                ProfileId = (int)c.Id,
                CompanyName = c.ProfileName,
                BusinessUnitId = bu != null ? (int?)bu.Id : null,
                Email = c.DomainName
            })
            .ToListAsync();

        }

        public async Task<bool> CompanyLinkExistsAsync(int profileId, int businessUnitId)
        {
            return await _context.CompanyLinks.AnyAsync(cl =>
                cl.ProfileId == profileId &&
                cl.BussinessUnitId == businessUnitId &&
                cl.UserId != 0);
        }

        public async Task<Profile> GetCompanyByIdAsync(int profileId)
        {
            return await _context.Profiles.FirstOrDefaultAsync(c => c.Id == profileId);
        }
        public async Task<User> GetUserByEmailAsync(string email)
        {
            return await _context.Users.FirstOrDefaultAsync(u => u.Username == email);
        }
        public async Task<uint> CreateBussinessUnitAsync(BusinessUnit unit)
        {
            _context.BusinessUnits.Add(unit);
            await _context.SaveChangesAsync();
            return unit.Id;
        }

        public async Task<int> CreateUserAsync(User user)
        {
            _context.Users.Add(user);
            await _context.SaveChangesAsync();
            return user.UserId;
        }

        public async Task CreateCompanyLinkAsync(CompanyLink link)
        {
            _context.CompanyLinks.Add(link);
            await _context.SaveChangesAsync();
        }

        // A report is visible to its team only once it has been PUBLISHED — i.e.
        // it has an InsightPublishLog row with OperationType Insert/Update. That
        // is the same marker the workbench screens read as InsightsPublished.
        //
        // This filter is what makes the admin review gate real. The scheduler now
        // writes reports without a publish row, so an unreviewed report is not
        // merely un-emailed: it does not appear in the team's report list at all
        // until an admin approves it. Rejected reports carry a "Rejected" row and
        // are excluded by the same test, so they stay invisible for good.
        private IQueryable<LLMReport> PublishedReports =>
            _context.LLMReports.Where(r =>
                _context.InsightPublishLogs.Any(l =>
                    l.ReportId == r.Id &&
                    (l.OperationType == "Insert" || l.OperationType == "Update")));

        public async Task<IEnumerable<LLMReport>> GetReportsByUnitAsync(long unitId)
        {
            return await PublishedReports
                .Where(r => r.UnitId == unitId)
                .OrderByDescending(r => r.FromDate) // most recent report dates first
                .ToListAsync();
        }

        public async Task<LLMReport> GetReportsByUnitAndReportIdAsync(long id, long unitId)
        {
            return await PublishedReports
                .FirstOrDefaultAsync(r => r.UnitId == unitId && r.Id == id);

        }

        public async Task<LLMReport> CreateReportAsync(LLMReport report)
        {
            _context.LLMReports.Add(report);
            await _context.SaveChangesAsync();
            return report;
        }

        public async Task<List<TopRecommendation>> GetTopRecommendationsByUnitIdAndReportIdAsync(long ReportId, long unitId)
        {
            return await _context.TopRecommendations
                .Where(r => r.UnitId == unitId && r.ReportId == ReportId)
                .OrderByDescending(r => r.CreatedAt)
                .Take(3)
                .ToListAsync();
        }

        public async Task<List<TopTheme>> GetTopThemesByUnitIdAndReportAsync(long ReportId, long unitId)
        {
            var themes = await _context.TopThemes
                .Where(t => t.UnitId == unitId && t.ReportId == ReportId)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync(); // fetch to memory

            return themes
                .GroupBy(t => t.Theme)
                .Select(g => g.First()) // now safe in memory
                .OrderByDescending(t => t.CreatedAt)
                .Take(5)
                .ToList();
        }

        public async Task<List<TopAction>> GetTopActionsByUnitIdAndReportAsync(long ReportId, long unitId)
        {
            return await _context.TopActions
                .Where(a => a.UnitId == unitId && a.ReportId == ReportId)
                .OrderByDescending(a => a.CreatedAt)
                .Take(3)
                .ToListAsync();
        }
        public async Task<DashboardSummaryDto> GetDashboardSummaryByDateRangeAsync(long unitId, DateTime fromDate, DateTime toDate)
        {
            var result = new DashboardSummaryDto();

            using var conn = _context.Database.GetDbConnection();
            await conn.OpenAsync();

            // QUERY 1: Overall score
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
            SELECT 
                ROUND(AVG((
                    ""AngryPercentage"" * 1 +
                    ""SadPercentage"" * 34 +
                    ""IndifferentPercentage"" * 67 +
                    ""HappyPercentage"" * 100
                ) / 100.0)::numeric, 2) AS Value
            FROM public.""FeedbackAnalysisResults""
            WHERE ""AnalysisDate"" BETWEEN @fromDate AND @toDate
              AND ""BusinessUnitId"" = @unitId";

                AddParameter(cmd, "@unitId", unitId);
                AddParameter(cmd, "@fromDate", fromDate);
                AddParameter(cmd, "@toDate", toDate);

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    double value = reader.IsDBNull(0) ? 0 : reader.GetDouble(0);
                    result.OverallScore = new OverallScoreDto
                    {
                        Value = value,
                        Status = GetStatusFromScore(value)
                    };
                }
            }

            // QUERY 2: Trend chart (grouped monthly)
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
            SELECT 
                TO_CHAR(date_trunc('month', ""AnalysisDate""), 'Mon') AS Name,
                ROUND(AVG((
                    ""AngryPercentage"" * 1 +
                    ""SadPercentage"" * 34 +
                    ""IndifferentPercentage"" * 67 +
                    ""HappyPercentage"" * 100
                ) / 100.0)::numeric, 2) AS Score
            FROM public.""FeedbackAnalysisResults""
            WHERE ""AnalysisDate"" BETWEEN @fromDate AND @toDate
              AND ""BusinessUnitId"" = @unitId
            GROUP BY date_trunc('month', ""AnalysisDate"")
            ORDER BY date_trunc('month', ""AnalysisDate"")";

                AddParameter(cmd, "@unitId", unitId);
                AddParameter(cmd, "@fromDate", fromDate);
                AddParameter(cmd, "@toDate", toDate);

                using var reader = await cmd.ExecuteReaderAsync();
                var trends = new List<TrendChartEntryDto>();
                while (await reader.ReadAsync())
                {
                    trends.Add(new TrendChartEntryDto
                    {
                        Name = reader.GetString(0),
                        Score = reader.GetDouble(1)
                    });
                }

                string monthlyChange = "N/A";
                if (trends.Count >= 2)
                {
                    double lastScore = trends[^1].Score;
                    double prevScore = trends[^2].Score;
                    var change = lastScore - prevScore;
                    var percent = prevScore != 0 ? (change / prevScore) * 100 : 0;
                    monthlyChange = change >= 0
                        ? $"↗ {Math.Abs(percent):0.#}% vs last period"
                        : $"↘ {Math.Abs(percent):0.#}% vs last period";
                }

                string sixMonthChange = "N/A";
                if (trends.Count >= 2)
                {
                    double lastScore = trends[^1].Score;
                    double firstScore = trends[0].Score;
                    var change = lastScore - firstScore;
                    var percent = firstScore != 0 ? (change / firstScore) * 100 : 0;
                    sixMonthChange = change >= 0
                        ? $"↗ {Math.Abs(percent):0.#}% vs first period"
                        : $"↘ {Math.Abs(percent):0.#}% vs first period";
                }

                result.TrendChart = new TrendChartDto
                {
                    Data = trends,
                    PreviousData = new List<TrendChartEntryDto>(),
                    CurrentData = trends,
                    MonthlyChange = monthlyChange,
                    SixMonthChange = sixMonthChange
                };
            }

            // QUERY 3: Health Scores
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
            SELECT 
                ""CategoryName"" AS Label,
                ROUND(AVG((
                    ""AngryPercentage"" * 1 + 
                    ""SadPercentage"" * 34 + 
                    ""IndifferentPercentage"" * 67 + 
                    ""HappyPercentage"" * 100
                ) / 100.0)::numeric, 2) AS Score
            FROM public.""FeedbackAnalysisResults""
            WHERE ""AnalysisDate"" BETWEEN @fromDate AND @toDate
              AND ""BusinessUnitId"" = @unitId
            GROUP BY ""CategoryName""
            ORDER BY ""CategoryName""";

                AddParameter(cmd, "@unitId", unitId);
                AddParameter(cmd, "@fromDate", fromDate);
                AddParameter(cmd, "@toDate", toDate);

                using var reader = await cmd.ExecuteReaderAsync();
                var healthScores = new List<HealthScoreDto>();
                while (await reader.ReadAsync())
                {
                    var score = reader.GetDouble(1);
                    healthScores.Add(new HealthScoreDto
                    {
                        Label = reader.GetString(0),
                        Score = score,
                        Status = GetStatusFromScore(score)
                    });
                }

                result.HealthScores = healthScores;
                // Order by enum numeric value
                healthScores = healthScores
                    .OrderBy(h => (int)Enum.Parse<CloverCategory>(h.Label))
                    .ToList();
                result.HealthScores = healthScores;
            }

            // QUERY 4: Metrics - Response Rate
            var metrics = new List<MetricDto>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
            WITH feedback_counts AS (
                SELECT 
                    COUNT(*) FILTER (
                        WHERE date_trunc('month', ""SubmissionDateTime"") = date_trunc('month', CURRENT_DATE)
                    ) AS current_month_count,
                    COUNT(*) FILTER (
                        WHERE date_trunc('month', ""SubmissionDateTime"") = date_trunc('month', CURRENT_DATE - INTERVAL '1 month')
                    ) AS previous_month_count
                FROM public.""DailyFeedback""
                WHERE ""BusinessUnitId"" = @unitId
            ),
            selected_days AS (
                SELECT COUNT(*) AS selected_range_count
                FROM public.""DailyFeedback""
                WHERE ""BusinessUnitId"" = @unitId
                  AND ""SubmissionDateTime"" BETWEEN @fromDate AND @toDate
            )
            SELECT s.selected_range_count, f.current_month_count, f.previous_month_count
            FROM feedback_counts f, selected_days s;";

                AddParameter(cmd, "@unitId", unitId);
                AddParameter(cmd, "@fromDate", fromDate);
                AddParameter(cmd, "@toDate", toDate);

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    int selectedRangeCount = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
                    int current = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                    int previous = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);

                    string changeStr = "N/A";
                    string changeType = "neutral";
                    string statusLabel = "No Data";

                    if (previous == 0 && current > 0)
                    {
                        changeStr = "+∞% vs last month";
                        changeType = "positive";
                        statusLabel = "On Target";
                    }
                    else if (previous > 0)
                    {
                        double change = ((double)(current - previous) / previous) * 100;
                        changeStr = change >= 0
                            ? $"+{Math.Abs(change):0.0}% vs last month"
                            : $"-{Math.Abs(change):0.0}% vs last month";

                        changeType = change >= 0 ? "positive" : "negative";
                        statusLabel = change >= 0 ? "On Target" : "Needs Attention";
                    }

                    metrics.Add(new MetricDto
                    {
                        Title = "Responses",
                        Value = result.OverallScore.Value > 0 ? selectedRangeCount : 0,
                        Change = changeStr,
                        ChangeType = changeType,
                        Status = new MetricStatusDto { Label = statusLabel }
                    });
                }
            }

            result.Metrics = metrics;

            if (result.Metrics == null || !result.Metrics.Any() || result.Metrics.All(m => m.Value == 0))
            {
                result.OverallScore = new OverallScoreDto { Value = 0, Status = "Needs Work" };
                var emptyTrendData = new List<TrendChartEntryDto>
                {
                    new TrendChartEntryDto { Name = DateTime.UtcNow.ToString("MMM yyyy"), Score = 0 }
                };
                result.TrendChart = new TrendChartDto
                {
                    Data = emptyTrendData,
                    PreviousData = new List<TrendChartEntryDto>(),
                    CurrentData = emptyTrendData,
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
        public async Task<LLMReport> AddReportAsync(LLMReport report)
        {
            _context.LLMReports.Add(report);
            await _context.SaveChangesAsync();
            return report;
        }
        public async Task<LLMReport> GetReportsByDateRange(DateTime FromDate, DateTime ToDate)
        {
            return await _context.LLMReports
                .FirstOrDefaultAsync(r => r.FromDate == FromDate && r.ToDate == ToDate);

        }

        public async Task DeleteRecommendationsAsync(IEnumerable<TopRecommendation> recommendations)
        {
            _context.TopRecommendations.RemoveRange(recommendations);
            await _context.SaveChangesAsync();
        }
        public async Task DeleteThemesAsync(IEnumerable<TopTheme> themes)
        {
            _context.TopThemes.RemoveRange(themes);
            await _context.SaveChangesAsync();
        }
        public async Task DeleteActionsAsync(IEnumerable<TopAction> actions)
        {
            _context.TopActions.RemoveRange(actions);
            await _context.SaveChangesAsync();
        }
        public async Task DeleteReportAsync(LLMReport report)
        {
            _context.LLMReports.Remove(report);
            await _context.SaveChangesAsync();
        }
        public Task<List<FeedbackDataResultDto>> GetFeedbackDataAsyncRuntime(long unitId, DateTimeOffset fromDate, DateTimeOffset toDate)
            => GetFeedbackDataAsyncRuntime(0, unitId, fromDate, toDate);

        public async Task<List<FeedbackDataResultDto>> GetFeedbackDataAsyncRuntime(long profileId, long unitId, DateTimeOffset fromDate, DateTimeOffset toDate)
        {
            string query = @"
        SELECT DISTINCT ON (df.""Id"")
        df.""Id"" AS ""FeedbackId"",
        df.""SubmissionDateTime"",
        df.""BusinessUnitId"",
        COALESCE(bu.""Name"", '') AS ""BusinessUnitName"",
        COALESCE(com.""Name"", '') AS ""CompanyName"",
        dq.""Id"" AS ""QuestionId"",
        COALESCE(dq.""Text"", '') AS ""QuestionText"",

        dqs.""CloverCategory"" AS ""CategoryId"",
        CASE dqs.""CloverCategory""
            WHEN 1 THEN 'Communication'
            WHEN 2 THEN 'Learning'
            WHEN 3 THEN 'Opportunities'
            WHEN 4 THEN 'Vulnerability'
            WHEN 5 THEN 'Enablement'
            WHEN 6 THEN 'Reflection'
            ELSE 'Unknown'
        END AS ""Category"",

        dr.""Id"" AS ""ReasonId"",
        COALESCE(dr.""Text"", '') AS ""ReasonText"",

        df.""SentimentType"" AS ""SentimentTypeId"",
        CASE df.""SentimentType""
            WHEN 1 THEN 'Angry'
            WHEN 2 THEN 'Sad'
            WHEN 3 THEN 'Indifferent'
            WHEN 4 THEN 'Happy'
            ELSE 'Unknown'
        END AS ""SentimentType"",

        COALESCE(df.""FreeText"", '') AS ""FreeText""
    FROM ""DailyFeedback"" df
     LEFT JOIN ""DailyQuestions"" dq
        ON df.""DailyQuestionId"" = dq.""Id""
     LEFT JOIN ""DailyReasons"" dr
        ON df.""DailyReasonId"" = dr.""Id""
     LEFT JOIN ""DailyQuestionSpecifications"" dqs
        ON dr.""SpecificationId"" = dqs.""Id""
     JOIN ""BusinessUnits"" bu
        ON df.""BusinessUnitId"" = bu.""Id""
     JOIN ""Profiles"" com
        ON bu.""ProfileId"" = com.""Id""
    WHERE
        df.""BusinessUnitId"" = @unitId
        AND (@profileId = 0 OR bu.""ProfileId"" = @profileId)
        AND df.""SubmissionDateTime"" BETWEEN @fromDate AND @toDate;
    ";

            // Npgsql requires DateTimeOffset bound to timestamptz to be UTC (Offset = 0).
            var fromDateUtc = fromDate.ToUniversalTime();
            var toDateUtc = toDate.ToUniversalTime();

            var result = await _context.FeedbackDataResultDtos
                .FromSqlRaw(query,
                    new Npgsql.NpgsqlParameter("@unitId", unitId),
                    new Npgsql.NpgsqlParameter("@companyId", profileId),
                    new Npgsql.NpgsqlParameter("@fromDate", fromDateUtc),
                    new Npgsql.NpgsqlParameter("@toDate", toDateUtc))
                .ToListAsync();

            return result;
        }

        public async Task<List<WorkAllocationResultDto>> GetWorkAllocationDataAsync(long unitId, DateTimeOffset fromDate, DateTimeOffset toDate)
        {
            // Productivity / time-allocation rows for the unit, scoped to the snapshot window.
            // WorkCategories is referenced unquoted on purpose — the physical table/columns
            // are lower-case; quoting them would make Postgres look for "WorkCategories".
            // Output columns are aliased to match WorkAllocationResultDto property names.
            string query = @"
        SELECT
            dr.""Id""                  AS ""DailyReflectionId"",
            dr.""ReflectionLocalDate"" AS ""ReflectionLocalDate"",
            ta.""Id""                  AS ""TimeAllocationId"",
            ta.""Hours""               AS ""Hours"",
            wc.id                      AS ""WorkCategoryId"",
            wc.category                AS ""Category"",
            wc.title                   AS ""Title"",
            wc.subtitle                AS ""Subtitle""
        FROM ""DailyReflections"" dr
        JOIN ""TimeAllocation"" ta
            ON ta.""DailyReflectionId"" = dr.""Id""
        JOIN WorkCategories wc
            ON wc.id = ta.""OptionId""
        WHERE dr.""BusinessUnitId"" = @unitId
            AND dr.""ReflectionLocalDate"" BETWEEN @fromDate AND @toDate;
    ";

            // ReflectionLocalDate is a local date column; compare on the date portion.
            var fromDateUtc = fromDate.ToUniversalTime().Date;
            var toDateUtc = toDate.ToUniversalTime().Date;

            var result = await _context.WorkAllocationResultDtos
                .FromSqlRaw(query,
                    new Npgsql.NpgsqlParameter("@unitId", unitId),
                    new Npgsql.NpgsqlParameter("@fromDate", fromDateUtc),
                    new Npgsql.NpgsqlParameter("@toDate", toDateUtc))
                .ToListAsync();

            return result;
        }

        // Productivity reflection lines (achievements / not-done / blockers) for the unit,
        // scoped to the snapshot window. text[] columns map straight to string[]. Deduped
        // to one row per local date (latest reflection wins via DISTINCT ON … ORDER BY Id DESC)
        // so identical re-submissions for the same day are not double-counted.
        public async Task<List<DailyReflectionLinesDto>> GetDailyReflectionLinesAsync(long unitId, DateTimeOffset fromDate, DateTimeOffset toDate)
        {
            string query = @"
        SELECT DISTINCT ON (dr.""ReflectionLocalDate"")
            dr.""Id""                  AS ""Id"",
            dr.""ReflectionLocalDate"" AS ""ReflectionLocalDate"",
            dr.""AchievementsLines""   AS ""AchievementsLines"",
            dr.""NotDoneLines""        AS ""NotDoneLines"",
            dr.""BlockersLines""       AS ""BlockersLines""
        FROM ""DailyReflections"" dr
        WHERE dr.""BusinessUnitId"" = @unitId
            AND (dr.""IsDel"" IS NOT TRUE)
            AND dr.""ReflectionLocalDate"" BETWEEN @fromDate AND @toDate
        ORDER BY dr.""ReflectionLocalDate"", dr.""Id"" DESC;
    ";

            // ReflectionLocalDate is a local date column; compare on the date portion.
            var fromDateUtc = fromDate.ToUniversalTime().Date;
            var toDateUtc = toDate.ToUniversalTime().Date;

            var result = await _context.DailyReflectionLinesDtos
                .FromSqlRaw(query,
                    new Npgsql.NpgsqlParameter("@unitId", unitId),
                    new Npgsql.NpgsqlParameter("@fromDate", fromDateUtc),
                    new Npgsql.NpgsqlParameter("@toDate", toDateUtc))
                .ToListAsync();

            return result;
        }

        public async Task<long?> GetBusinessUnitIdByNameAsync(string name)
        {
            return await _context.BusinessUnits
                .Where(b => b.Name == name)
                .Select(b => (long?)b.Id)
                .FirstOrDefaultAsync();
        }

        public async Task<long?> GetQuestionIdByTextAsync(string questionText)
        {
            return await _context.DailyQuestions
                .Where(q => q.Text == questionText)
                .Select(q => (long?)q.Id)
                .FirstOrDefaultAsync();
        }

        public async Task<long?> GetReasonIdByTextAsync(string reasonText)
        {
            return await _context.DailyReasons
                .Where(r => r.Text == reasonText)
                .Select(r => (long?)r.Id)
                .FirstOrDefaultAsync();
        }

        public async Task AddFeedbackAsync(DailyFeedback feedback)
        {
            await _context.DailyFeedbacks.AddAsync(feedback);
        }

        public async Task SaveChangesAsync()
        {
            await _context.SaveChangesAsync();
        }
        public async Task<List<QuestionDto>> GetQuestionListAsync()
        {
            // Get all question list
            var question = await _context.DailyQuestions
                .AsNoTracking()
                .OrderByDescending(qu => qu.Id)
                .Select(qu => new QuestionDto
                {
                    Id = qu.Id,
                    Text = qu.Text,
                })
                .ToListAsync();

            return question;
        }

        public async Task<DashboardSummaryDto> GetDashboardSummaryFromAnalysisAsync(
long unitId,
List<FeedbackAnalysisResult> analysisResults,
List<DailyFeedbackReportRowDto> rawFeedback // needed for ResponseRate metrics
)
        {
            var result = new DashboardSummaryDto();

            // 1️ Overall Score (last 5 months)
            var overallScores = analysisResults
                .Where(x => x.BusinessUnitId == unitId &&
                            x.AnalysisDate >= DateTime.UtcNow.AddMonths(-5))
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

            // 2 Trend Chart (month by month, last 5 months)
            var trendData = analysisResults
                .Where(x => x.BusinessUnitId == unitId &&
                            x.AnalysisDate >= DateTime.UtcNow.AddMonths(-5))
                .GroupBy(x => new { x.AnalysisDate.Year, x.AnalysisDate.Month })
                .Select(g => new TrendChartEntryDto
                {
                    Name = new DateTime(g.Key.Year, g.Key.Month, 1).ToString("MMM"),
                    Score = Math.Round(g.Average(x =>
                        (x.AngryPercentage * 1 +
                         x.SadPercentage * 34 +
                         x.IndifferentPercentage * 67 +
                         x.HappyPercentage * 100) / 100.0), 2)
                })
                .OrderBy(t => DateTime.ParseExact(t.Name, "MMM", null))
                .ToList();

            if (trendData.Count > 0 && trendData.Any(t => t.Score > 0))
            {
                string monthlyChange = "N/A";
                string sixMonthChange = "N/A";

                if (trendData.Count >= 2)
                {
                    var last = trendData[^1].Score;
                    var prev = trendData[^2].Score;
                    var change = last - prev;
                    var percent = prev != 0 ? (change / prev) * 100 : 0;
                    monthlyChange = change >= 0
                        ? $"↗ {Math.Abs(percent):0.#}% vs last period"
                        : $"↘ {Math.Abs(percent):0.#}% vs last period";
                }

                if (trendData.Count >= 2)
                {
                    var last = trendData[^1].Score;
                    var first = trendData[0].Score;
                    var change = last - first;
                    var percent = first != 0 ? (change / first) * 100 : 0;
                    sixMonthChange = change >= 0
                        ? $"↗ {Math.Abs(percent):0.#}% vs first period"
                        : $"↘ {Math.Abs(percent):0.#}% vs first period";
                }

                result.TrendChart = new TrendChartDto
                {
                    Data = trendData,
                    PreviousData = new List<TrendChartEntryDto>(),
                    CurrentData = trendData,
                    MonthlyChange = monthlyChange,
                    SixMonthChange = sixMonthChange
                };
            }

            // 3 Health Scores (just average all by CategoryName)
            var healthScores = analysisResults
                .Where(x => x.BusinessUnitId == unitId)
                .GroupBy(x => x.CategoryName)
                .Select(g =>
                {
                    var avg = Math.Round(g.Average(x =>
                        (x.AngryPercentage * 1 +
                         x.SadPercentage * 34 +
                         x.IndifferentPercentage * 67 +
                         x.HappyPercentage * 100) / 100.0), 2);
                    return new HealthScoreDto
                    {
                        Label = g.Key,
                        Score = avg,
                        Status = GetStatusFromScore(avg)
                    };
                })
                .OrderBy(h => (int)Enum.Parse<CloverCategory>(h.Label))
                .ToList();

            result.HealthScores = healthScores;

            // 4️⃣ Response Rate (based on raw feedback already passed in)
            var metrics = new List<MetricDto>();

            var recentCount = rawFeedback.Count();

            var currentMonthCount = rawFeedback.Count(r =>
                r.BusinessUnitId == unitId &&
                r.SubmissionDateTime.Month == DateTime.UtcNow.Month);
            var prevMonthCount = rawFeedback.Count(r =>
                r.BusinessUnitId == unitId &&
                r.SubmissionDateTime.Month == DateTime.UtcNow.AddMonths(-1).Month);

            if (recentCount > 0 && result.OverallScore.Value > 0)
            {
                string changeStr = "N/A";
                string changeType = "neutral";
                string statusLabel = "No Data";

                if (prevMonthCount == 0 && currentMonthCount > 0)
                {
                    changeStr = "+∞% vs last month";
                    changeType = "positive";
                    statusLabel = "On Target";
                }
                else if (prevMonthCount > 0)
                {
                    var change = ((double)(currentMonthCount - prevMonthCount) / prevMonthCount) * 100;
                    changeStr = change >= 0
                        ? $"+{Math.Abs(change):0.0}% vs last month"
                        : $"-{Math.Abs(change):0.0}% vs last month";

                    changeType = change >= 0 ? "positive" : "negative";
                    statusLabel = change >= 0 ? "On Target" : "Needs Attention";
                }

                metrics.Add(new MetricDto
                {
                    Title = "Responses",
                    Value = recentCount,
                    Change = changeStr,
                    ChangeType = changeType,
                    Status = new MetricStatusDto { Label = statusLabel }
                });
            }

            result.Metrics = metrics;
            if (result.Metrics == null || !result.Metrics.Any() || result.Metrics.All(m => m.Value == 0))
            {
                result.OverallScore = new OverallScoreDto { Value = 0, Status = "Needs Work" };
                var emptyTrendData = new List<TrendChartEntryDto>
                {
                    new TrendChartEntryDto { Name = DateTime.UtcNow.ToString("MMM yyyy"), Score = 0 }
                };
                result.TrendChart = new TrendChartDto
                {
                    Data = emptyTrendData,
                    PreviousData = new List<TrendChartEntryDto>(),
                    CurrentData = emptyTrendData,
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


        //Create the Report Feedback Analysies
        public async Task<List<DailyFeedbackReportRowDto>> CreateDailyFeedbackReportAsync(long unitId, DateTimeOffset fromDate, DateTimeOffset toDate)
        {
            if (fromDate != null && toDate != null)
            {
                fromDate = fromDate.UtcDateTime;
                toDate = toDate.UtcDateTime;
            }

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
            LEFT JOIN ""DailyQuestions"" dq 
                ON df.""DailyQuestionId"" = dq.""Id""
            LEFT JOIN ""DailyReasons"" dr 
                ON df.""DailyReasonId"" = dr.""Id""
            LEFT JOIN ""DailyQuestionSpecifications"" dqs 
                ON dr.""SpecificationId"" = dqs.""Id""
            WHERE 
                df.""BusinessUnitId"" = @UnitId
                AND df.""SubmissionDateTime"" BETWEEN @FromDate AND @ToDate;
        ";

            var result = await _context.DailyFeedbackReportRowDtos
                .FromSqlRaw(query,
                    new Npgsql.NpgsqlParameter("@UnitId", unitId),
                    new Npgsql.NpgsqlParameter("@FromDate", fromDate),
                    new Npgsql.NpgsqlParameter("@ToDate", toDate))
                .ToListAsync();
            await CreateAnalyzeAndSaveFeedbackInternal(result, unitId);
            return result;
        }
        private async Task CreateAnalyzeAndSaveFeedbackInternal(List<DailyFeedbackReportRowDto> feedbackRows, long unitId)
        {
            var analysisDate = DateTime.UtcNow;
            var submissionDate = feedbackRows.FirstOrDefault() != null ? feedbackRows.First().SubmissionDateTime.UtcDateTime : DateTime.UtcNow;


            var analysisResults = CreateCalculateFeedbackAnalysis(feedbackRows, analysisDate, submissionDate, (uint)unitId);

            foreach (var analysis in analysisResults)
            {
                // First: check if an entry exists with same BusinessUnitId, CategoryName, and SubmissionDate
                var exactMatch = await _context.FeedbackAnalysisResults
                    .FirstOrDefaultAsync(x =>
                        x.BusinessUnitId == analysis.BusinessUnitId &&
                        x.CategoryName == analysis.CategoryName &&
                        x.SubmissionDate.Date == analysis.SubmissionDate.Date);

                if (exactMatch != null)
                {
                    // Exact match found → update existing entry
                    exactMatch.AnalysisDate = analysis.AnalysisDate;
                    exactMatch.SadPercentage = analysis.SadPercentage;
                    exactMatch.HappyPercentage = analysis.HappyPercentage;
                    exactMatch.IndifferentPercentage = analysis.IndifferentPercentage;
                    exactMatch.AngryPercentage = analysis.AngryPercentage;
                }
                else
                {
                    // No exact match on date → check if same BusinessUnitId + CategoryName exists
                    var partialMatch = await _context.FeedbackAnalysisResults
                        .FirstOrDefaultAsync(x =>
                            x.BusinessUnitId == analysis.BusinessUnitId &&
                            x.CategoryName == analysis.CategoryName);

                    if (partialMatch != null)
                    {
                        // Same BusinessUnitId + CategoryName but different date → add new entry
                        _context.FeedbackAnalysisResults.Add(analysis);
                    }
                    else
                    {
                        // No match at all → add new entry
                        _context.FeedbackAnalysisResults.Add(analysis);
                    }

                }
            }

            await _context.SaveChangesAsync();

        }

        public List<FeedbackAnalysisResult> CreateCalculateFeedbackAnalysis(
        List<DailyFeedbackReportRowDto> feedbackRows, DateTime analysisDate, DateTime submissionDate, uint businessUnitId)
        {
            var analysisResults = new List<FeedbackAnalysisResult>();

            // Group by category (SpecificationCloverCategory) - this is int? key
            var groupedByCategory = feedbackRows
                .GroupBy(r => r.SpecificationCloverCategory)
                .ToList();

            foreach (var categoryGroup in groupedByCategory)
            {
                var category = categoryGroup.Key; // int?

                if (!category.HasValue || !Enum.IsDefined(typeof(CloverCategory), category.Value))
                {
                    continue;
                }
                // Safe to assign here
                string categoryName = ((CloverCategory)category.Value).ToString();

                var totalCount = categoryGroup.Count();
                if (totalCount == 0) continue;

                double angry = categoryGroup.Count(r => r.SentimentType == (int)DailyFeedbackSentimentType.Angry);
                double sad = categoryGroup.Count(r => r.SentimentType == (int)DailyFeedbackSentimentType.Sad);
                double indifferent = categoryGroup.Count(r => r.SentimentType == (int)DailyFeedbackSentimentType.Indifferent);
                double happy = categoryGroup.Count(r => r.SentimentType == (int)DailyFeedbackSentimentType.Happy);

                analysisResults.Add(new FeedbackAnalysisResult
                {
                    AnalysisDate = analysisDate,
                    SubmissionDate = submissionDate,
                    CategoryName = categoryName,
                    AngryPercentage = Math.Round((angry / totalCount) * 100, 2),
                    SadPercentage = Math.Round((sad / totalCount) * 100, 2),
                    IndifferentPercentage = Math.Round((indifferent / totalCount) * 100, 2),
                    HappyPercentage = Math.Round((happy / totalCount) * 100, 2),
                    BusinessUnitId = businessUnitId
                });
            }

            // Add missing categories with 0% if no feedback was present
            foreach (CloverCategory cat in Enum.GetValues(typeof(CloverCategory)))
            {
                string catName = cat.ToString();
                if (analysisResults.All(r => r.CategoryName != catName))
                {
                    analysisResults.Add(new FeedbackAnalysisResult
                    {
                        AnalysisDate = analysisDate,
                        SubmissionDate = submissionDate,
                        CategoryName = catName,
                        AngryPercentage = 0,
                        SadPercentage = 0,
                        IndifferentPercentage = 0,
                        HappyPercentage = 0,
                        BusinessUnitId = businessUnitId
                    });
                }
            }

            return analysisResults;
        }

        public async Task<List<BatchJobDto>> GetBatchJobsWithInsightsByBusinessUnitIdAsync(int businessUnitId)
        {
            var batchJobs = await _context.BatchJobs
                .Where(b => b.BusinessUnitId == businessUnitId)
                .Include(b => b.EngagementInsights).OrderByDescending(a => a.SubmittedAt)
                .ToListAsync();

            return batchJobs.Select(b => new BatchJobDto
            {
                BatchJobId = b.Id,
                OpenAIJobId = b.OpenAIJobId,
                Status = b.Status,
                RequestType = b.RequestType,
                SubmittedAt = b.SubmittedAt,
                CompletedAt = b.CompletedAt,
                InputFileUrl = b.InputFileUrl,
                OutputFileUrl = b.OutputFileUrl,
                ErrorMessage = b.ErrorMessage,
                MetadataJson = b.MetadataJson,
                EngagementInsights = b.EngagementInsights
                    .Select(ei => new EngagementInsightDto
                    {
                        Id = ei.Id,
                        GeneratedAt = ei.GeneratedAt,
                        PeriodStart = ei.PeriodStart,
                        PeriodEnd = ei.PeriodEnd,
                        OverallScore = ei.OverallScore,
                        CategoryScoresJson = ei.CategoryScoresJson,
                        TopThemesJson = ei.TopThemesJson,
                        RecommendationsJson = ei.RecommendationsJson,
                        StatisticalSummaryJson = ei.StatisticalSummaryJson,
                        CreatedBy = ei.CreatedBy,
                        TopActionsJson = ei.TopActionsJson
                    }).ToList()
            }).ToList();
        }
        //Hangfile logic
        public async Task<BatchJob?> GetLatestByBusinessUnitAsync(uint businessUnitId)
        {
            return await _context.BatchJobs
                .Where(b => b.BusinessUnitId == businessUnitId)
                .OrderByDescending(b => b.SubmittedAt)
                .FirstOrDefaultAsync();
        }

        public async Task AddAsync(BatchJob job)
        {
            await _context.BatchJobs.AddAsync(job);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(BatchJob job)
        {
            _context.BatchJobs.Update(job);
            await _context.SaveChangesAsync();
        }
        public async Task<List<BusinessUnit>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            return await _context.BusinessUnits
                .AsNoTracking()
                .OrderBy(b => b.Id)
                .ToListAsync(cancellationToken);
        }

        public async Task<List<DailyFeedbackReportRowDto>> GetDailyFeedbackReportAzureResults(long unitId, DateTimeOffset fromDate, DateTimeOffset toDate)
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
            LEFT JOIN ""DailyQuestions"" dq 
                ON df.""DailyQuestionId"" = dq.""Id""
            LEFT JOIN ""DailyReasons"" dr 
                ON df.""DailyReasonId"" = dr.""Id""
            LEFT JOIN ""DailyQuestionSpecifications"" dqs 
                ON dr.""SpecificationId"" = dqs.""Id""
            WHERE 
                df.""BusinessUnitId"" = @UnitId
                AND df.""SubmissionDateTime"" >= @FromDate
                AND df.""SubmissionDateTime"" <= @ToDate;
             ";

            var result = await _context.DailyFeedbackReportRowDtos
                .FromSqlRaw(query,
                    new Npgsql.NpgsqlParameter("@UnitId", unitId),
                    new Npgsql.NpgsqlParameter("@FromDate", fromDate),
                    new Npgsql.NpgsqlParameter("@ToDate", toDate)
                )
                .ToListAsync();

            return result;
        }
        ///...
        public async Task<BatchJob?> GetBatchJobByIdAsync(Guid id)
        {
            return await _context.BatchJobs.FirstOrDefaultAsync(b => b.Id == id);
        }

        public async Task<EngagementInsights?> GetEngagementInsightByBatchJobIdAsync(Guid batchJobId)
        {
            return await _context.EngagementInsights.FirstOrDefaultAsync(e => e.BatchJobsId == batchJobId);
        }

        public async Task UpdateEngagementInsightAsync(EngagementInsights entity)
        {
            _context.EngagementInsights.Update(entity);
            await _context.SaveChangesAsync();
        }
        ////....
        public async Task UpdateBatchJobAsync(BatchJob batchJob)
        {
            _context.BatchJobs.Update(batchJob);
            await _context.SaveChangesAsync();
        }
        public async Task<LLMReport?> GetReportByUnitAndDatesAsync(uint unitId, DateTime fromDate, DateTime toDate)
        {
            return await _context.LLMReports.FirstOrDefaultAsync(r => r.UnitId == unitId && r.FromDate == fromDate && r.ToDate == toDate);
        }

        public async Task DeleteReportAsync(int reportId)
        {
            var rpt = await _context.LLMReports.FirstOrDefaultAsync(r => r.Id == reportId);
            if (rpt != null)
            {
                var latest = await _context.InsightPublishLogs
                    .Where(x => x.ReportId == rpt.Id)
                    .OrderByDescending(x => x.Id)
                    .FirstOrDefaultAsync();

                if (latest != null)
                {
                    _context.InsightPublishLogs.Add(new InsightPublishLog
                    {
                        ReportId           = latest.ReportId,
                        RunId              = latest.RunId,
                        UserId             = latest.UserId,
                        DateFrom           = latest.DateFrom,
                        DateTo             = latest.DateTo,
                        TopThemes          = latest.TopThemes,
                        TopActions         = latest.TopActions,
                        TopRecommendations = latest.TopRecommendations,
                        OperationType      = "Delete",
                        CreatedAt          = DateTime.UtcNow
                    });
                    await _context.SaveChangesAsync();
                }

                _context.LLMReports.Remove(rpt);
                await _context.SaveChangesAsync();
            }
        }

        public async Task DeleteTopThemesByUnitAndReportAsync(uint unitId, int reportId)
        {
            var entities = _context.TopThemes.Where(x => x.UnitId == unitId && x.ReportId == reportId);
            _context.TopThemes.RemoveRange(entities);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteTopRecommendationsByUnitAndReportAsync(uint unitId, int reportId)
        {
            var entities = _context.TopRecommendations.Where(x => x.UnitId == unitId && x.ReportId == reportId);
            _context.TopRecommendations.RemoveRange(entities);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteTopActionsByUnitAndReportAsync(uint unitId, int reportId)
        {
            var entities = _context.TopActions.Where(x => x.UnitId == unitId && x.ReportId == reportId);
            _context.TopActions.RemoveRange(entities);
            await _context.SaveChangesAsync();
        }

        // transaction helper
        public async Task<bool> ExecuteInTransactionAsync(Func<Task> action)
        {
            // Use EF transaction to ensure atomicity
            using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                await action();
                await tx.CommitAsync();
                return true;
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return false;
            }
        }

        public async Task<LLMReport?> GetReportByIdAsync(long id)
        {
            return await _context.LLMReports.FindAsync(id);
        }

        public async Task<WorkbenchRunStatus?> GetWorkbenchRunStatusByIdAsync(int runId)
        {
            return await _context.WorkbenchRunStatuses.FindAsync(runId);
        }

        public async Task<InsightPublishLog?> GetInsightPublishLogByRunIdAsync(int runId)
        {
            return await _context.InsightPublishLogs
                .Where(x => x.RunId == runId)
                .OrderBy(x => x.Id)
                .FirstOrDefaultAsync();
        }

        public async Task SaveInsightPublishLogAsync(InsightPublishLog log)
        {
            var earliest = await _context.InsightPublishLogs
                .Where(x => x.ReportId == log.ReportId)
                .OrderBy(x => x.Id)
                .FirstOrDefaultAsync();

            if (earliest != null)
            {
                log.DateFrom = earliest.DateFrom;
                log.DateTo   = earliest.DateTo;
                log.OperationType = "Update";
            }
            else
            {
                log.OperationType = "Insert";
            }

            log.CreatedAt = DateTime.UtcNow;
            _context.InsightPublishLogs.Add(log);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteReportManuallyAsync(LLMReport report)
        {
            var latest = await _context.InsightPublishLogs
                .Where(x => x.ReportId == report.Id)
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync();

            if (latest != null)
            {
                _context.InsightPublishLogs.Add(new InsightPublishLog
                {
                    ReportId           = latest.ReportId,
                    RunId              = latest.RunId,
                    UserId             = latest.UserId,
                    DateFrom           = latest.DateFrom,
                    DateTo             = latest.DateTo,
                    TopThemes          = latest.TopThemes,
                    TopActions         = latest.TopActions,
                    TopRecommendations = latest.TopRecommendations,
                    OperationType      = "Delete",
                    CreatedAt          = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();
            }

            _context.LLMReports.Remove(report);
            await _context.SaveChangesAsync();
        }
        public async Task DeleteLinkedReportItemsAsync(long reportId)
        {
            var topActions = _context.TopActions.Where(x => x.ReportId == reportId);
            var topRecommendations = _context.TopRecommendations.Where(x => x.ReportId == reportId);
            var topThemes = _context.TopThemes.Where(x => x.ReportId == reportId);

            _context.TopActions.RemoveRange(topActions);
            _context.TopRecommendations.RemoveRange(topRecommendations);
            _context.TopThemes.RemoveRange(topThemes);

            await _context.SaveChangesAsync();
        }
        public Task<bool> CheckFileExistsAsync(string fileName)
        {
            string folderPath = Path.Combine(_env.WebRootPath, "WorkSheetFile");

            if (!Directory.Exists(folderPath))
                return Task.FromResult(false);

            // Normalize input (trim spaces)
            fileName = fileName.Trim();

            // If user passed exact file with extension
            string directPath = Path.Combine(folderPath, fileName);
            if (File.Exists(directPath))
                return Task.FromResult(true);

            // If user passed without extension → search for matching files
            string fileWithoutExt = Path.GetFileNameWithoutExtension(fileName);

            var matchingFiles = Directory.GetFiles(folderPath)
                                         .Where(f =>
                                             Path.GetFileNameWithoutExtension(f)
                                                 .Equals(fileWithoutExt, StringComparison.OrdinalIgnoreCase))
                                         .ToList();

            return Task.FromResult(matchingFiles.Any());
        }
        public async Task<DateTime?> GetLatestSubmissionDateTimeAsync(uint businessUnitId)
        {
            var latestFeedback = await _context.DailyFeedbacks
                .Where(df => df.BusinessUnitId == (uint)businessUnitId)
                .OrderByDescending(df => df.SubmissionDateTime)
                .FirstOrDefaultAsync();

            return latestFeedback?.SubmissionDateTime;
        }

        public async Task BulkInsertFeedbackAsync(List<DailyFeedback> feedbackList)
        {
            if (feedbackList == null || !feedbackList.Any())
                return;

            await _context.DailyFeedbacks.AddRangeAsync(feedbackList);
            await _context.SaveChangesAsync();
        }
    }
}