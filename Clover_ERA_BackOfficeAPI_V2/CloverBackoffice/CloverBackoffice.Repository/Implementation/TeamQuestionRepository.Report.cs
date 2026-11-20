using CloverBackoffice.Infrastructure.BusinessModel;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models.TeamQuestions;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CloverBackoffice.Repository.Implementation
{
    /// <summary>
    /// Team Daily Questions — the question bank report.
    ///
    /// This reports on the CONTENT of the bank: how many questions exist, how they spread over
    /// the three groups and eight conditions, and which questions are still missing something.
    /// It is the report the import screen needs to answer "did the workbook land correctly?".
    /// Response analytics over TeamDailyFeedback are a separate concern and are not here.
    /// </summary>
    public partial class TeamQuestionRepository
    {
        public async Task<TeamQuestionBankReportDto> GetTeamQuestionBankReportAsync()
        {
            var specifications = await _context.TeamDailyQuestionSpecifications
                .AsNoTracking()
                .Include(s => s.Question)
                .Include(s => s.Category)
                .Include(s => s.Reasons)
                .OrderBy(s => s.CategoryId)
                .ThenBy(s => s.SubCategory)
                .ThenBy(s => s.Ordinal)
                .ThenBy(s => s.Variant)
                .ToListAsync();

            var reasonIds = specifications.SelectMany(s => s.Reasons).Select(r => r.Id).ToList();

            var actionCountByReasonId = await _context.TeamDailyMicroActions
                .AsNoTracking()
                .Where(a => reasonIds.Contains(a.TeamDailyReasonId))
                .GroupBy(a => a.TeamDailyReasonId)
                .Select(g => new { ReasonId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.ReasonId, x => x.Count);

            var allReasons = specifications.SelectMany(s => s.Reasons).ToList();

            var report = new TeamQuestionBankReportDto
            {
                TotalQuestions = await _context.TeamDailyQuestions.CountAsync(),
                TotalSpecifications = specifications.Count,
                TotalReasons = allReasons.Count,
                TotalMicroActions = actionCountByReasonId.Values.Sum(),
                PositiveReasons = allReasons.Count(r => r.TeamDailyReasonSentimentType == TeamDailyReasonSentimentType.Positive),
                NegativeReasons = allReasons.Count(r => r.TeamDailyReasonSentimentType == TeamDailyReasonSentimentType.Negative),
                ReasonsWithTeamState = allReasons.Count(r => r.TeamState != null),

                // The free-text option never has a micro-action by design, so counting it as a
                // gap would report every question as incomplete.
                ReasonsWithoutMicroAction = allReasons.Count(r =>
                    !r.RequiresFreeTextFeedback && actionCountByReasonId.GetValueOrDefault(r.Id, 0) == 0)
            };

            int ActionsFor(TeamDailyQuestionSpecification s) =>
                s.Reasons.Sum(r => actionCountByReasonId.GetValueOrDefault(r.Id, 0));

            report.ByCategory = specifications
                .GroupBy(s => new { s.CategoryId, Name = s.Category?.Name ?? string.Empty })
                .OrderBy(g => g.Key.CategoryId)
                .Select(g => new TeamQuestionGroupCountDto
                {
                    Name = string.IsNullOrEmpty(g.Key.Name) ? $"Category {g.Key.CategoryId}" : g.Key.Name,
                    QuestionCount = g.Count(),
                    ReasonCount = g.Sum(s => s.Reasons.Count),
                    MicroActionCount = g.Sum(ActionsFor)
                })
                .ToList();

            report.ByTeamElement = specifications
                .GroupBy(s => s.TeamElement)
                .OrderBy(g => g.Key)
                .Select(g => new TeamQuestionGroupCountDto
                {
                    Name = TeamQuestionEnumMapper.ToLabel(g.Key),
                    QuestionCount = g.Count(),
                    ReasonCount = g.Sum(s => s.Reasons.Count),
                    MicroActionCount = g.Sum(ActionsFor)
                })
                .ToList();

            report.BySubCategory = specifications
                .GroupBy(s => s.SubCategory)
                .OrderBy(g => g.Key)
                .Select(g => new TeamQuestionGroupCountDto
                {
                    Name = TeamQuestionEnumMapper.ToLabel(g.Key),
                    QuestionCount = g.Count(),
                    ReasonCount = g.Sum(s => s.Reasons.Count),
                    MicroActionCount = g.Sum(ActionsFor)
                })
                .ToList();

            report.ByVariant = specifications
                .GroupBy(s => s.Variant)
                .OrderBy(g => g.Key)
                .Select(g => new TeamQuestionGroupCountDto
                {
                    Name = TeamQuestionEnumMapper.ToLabel(g.Key),
                    QuestionCount = g.Count(),
                    ReasonCount = g.Sum(s => s.Reasons.Count),
                    MicroActionCount = g.Sum(ActionsFor)
                })
                .ToList();

            report.Questions = specifications
                .Select(s => BuildCoverage(s, actionCountByReasonId))
                .ToList();

            return report;
        }

        private static TeamQuestionCoverageDto BuildCoverage(
            TeamDailyQuestionSpecification specification,
            Dictionary<uint, int> actionCountByReasonId)
        {
            var positive = specification.Reasons
                .Where(r => r.TeamDailyReasonSentimentType == TeamDailyReasonSentimentType.Positive)
                .ToList();

            var negative = specification.Reasons
                .Where(r => r.TeamDailyReasonSentimentType == TeamDailyReasonSentimentType.Negative)
                .ToList();

            var coverage = new TeamQuestionCoverageDto
            {
                QuestionId = specification.TeamDailyQuestionId,
                QuestionText = specification.Question?.Text ?? string.Empty,
                CategoryId = specification.CategoryId,
                CategoryName = specification.Category?.Name ?? string.Empty,
                TeamElement = TeamQuestionEnumMapper.ToLabel(specification.TeamElement),
                SubCategory = TeamQuestionEnumMapper.ToLabel(specification.SubCategory),
                Variant = TeamQuestionEnumMapper.ToLabel(specification.Variant),
                Ordinal = specification.Ordinal,
                PositiveReasonCount = positive.Count,
                NegativeReasonCount = negative.Count,
                MicroActionCount = specification.Reasons.Sum(r => actionCountByReasonId.GetValueOrDefault(r.Id, 0)),
                HasFreeTextOption = specification.Reasons.Any(r => r.RequiresFreeTextFeedback)
            };

            if (positive.Count == 0)
            {
                coverage.Gaps.Add("No good reasons.");
            }

            if (negative.Count == 0)
            {
                coverage.Gaps.Add("No bad reasons.");
            }

            var missingActions = specification.Reasons
                .Count(r => !r.RequiresFreeTextFeedback && actionCountByReasonId.GetValueOrDefault(r.Id, 0) == 0);

            if (missingActions > 0)
            {
                coverage.Gaps.Add($"{missingActions} reason(s) without a micro-action.");
            }

            if (!coverage.HasFreeTextOption)
            {
                coverage.Gaps.Add("No free-text option.");
            }

            return coverage;
        }
    }
}
