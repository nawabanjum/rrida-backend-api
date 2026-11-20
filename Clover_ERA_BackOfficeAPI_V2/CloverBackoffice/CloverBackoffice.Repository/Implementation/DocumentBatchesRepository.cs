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
    public class DocumentBatchesRepository : IDocumentBatchesRepository
    {
        private readonly ApplicationDbContext _context;
        private readonly FeedbackScoreThresholdsOptions _thresholds;
        private readonly IWebHostEnvironment _env;

        public DocumentBatchesRepository(ApplicationDbContext context, IOptions<FeedbackScoreThresholdsOptions> thresholds, IWebHostEnvironment env)
        {
            _context = context;
            _thresholds = thresholds.Value;
            _env = env;

        }

        public async Task<List<DailyFeedbackReportRowDto>> GetDailyFeedbackReportAsync2Embedding(long unitId, int days)
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

        public async Task<List<BatchJobDto>> GetBatchJobsWithInsightsByBusinessUnitIdAsyncEmbedding(int businessUnitId)
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

        public async Task UpdateBatchJobAsync(BatchJob batchJob)
        {
            _context.BatchJobs.Update(batchJob);
            await _context.SaveChangesAsync();
        }

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

        // below 3rd api 
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

        public async Task<List<string>> GetAllWorksheetFilesAsync()
        {
            return await _context.DocumentEmbeddings
                .Where(d => !string.IsNullOrEmpty(d.FileName))
                .Select(d => d.FileName)
                .Distinct()
                .ToListAsync();
        }

        public async Task<List<(string FileName, string Content)>> GetWorksheetFileContentsAsync()
        {
            var rows = await _context.DocumentEmbeddings
                .Where(d => !string.IsNullOrEmpty(d.FileName))
                .Select(d => new { d.FileName, d.Content, d.Title, d.ChunkIndex })
                .ToListAsync();

            return rows
                .GroupBy(r => r.FileName!, StringComparer.OrdinalIgnoreCase)
                .Select(g => (
                    FileName: g.Key,
                    Content: string.Join(" ",
                        g.OrderBy(r => r.ChunkIndex ?? 0)
                         .Select(r => $"{r.Title} {r.Content}"))
                ))
                .ToList();
        }

    }
}