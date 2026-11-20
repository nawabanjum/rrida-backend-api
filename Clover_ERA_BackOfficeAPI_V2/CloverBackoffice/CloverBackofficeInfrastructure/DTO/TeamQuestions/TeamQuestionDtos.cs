using System.Collections.Generic;

namespace CloverBackoffice.Infrastructure.DTO
{
    // ═══════════════════════════════════════════════════════════════════════════
    // Team Daily Questions — API contracts.
    //
    // These mirror the legacy Daily Question DTOs one for one, with two deliberate
    // differences:
    //
    //   1. CloverCategory is replaced by the three team fields (TeamElement,
    //      SubCategory, Variant), matching the schema.
    //   2. Reasons and micro-actions travel as OBJECTS, not as parallel delimited
    //      strings. Legacy sends "reason1,reason2" alongside "action1,action2" and
    //      pairs them by position, which breaks on any comma inside the text. The
    //      team workbook also carries a Team State per reason, which would need a
    //      third parallel string to survive that scheme.
    //
    // Everything is expressed with the workbook's own wording — "Goal Affinity",
    // "Direction and learning" — rather than enum names. See TeamQuestionEnumMapper.
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>One row of the team question bank listing.</summary>
    public class TeamQuestionListItemDto
    {
        public long QuestionId { get; set; }
        public string QuestionText { get; set; } = string.Empty;
        public long SpecificationId { get; set; }
        /// <summary>0 is the seeded "General" category — the question is served to everyone.</summary>
        public long CategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public string TeamElement { get; set; } = string.Empty;
        public string SubCategory { get; set; } = string.Empty;
        public string Variant { get; set; } = string.Empty;
        public int Ordinal { get; set; }
        public int PositiveReasonCount { get; set; }
        public int NegativeReasonCount { get; set; }
        public int MicroActionCount { get; set; }
    }

    /// <summary>A reason plus its single micro-action, as supplied by the caller.</summary>
    public class TeamReasonInputDto
    {
        public string Text { get; set; } = string.Empty;

        /// <summary>Cruise / Drift / Headwinds / Stall / External / Neutral. Optional.</summary>
        public string? TeamState { get; set; }

        /// <summary>The micro-action shown when this reason is chosen. Optional.</summary>
        public string? MicroAction { get; set; }

        /// <summary>
        /// Set on the free-text ("Other") option. Left alone the repository infers it from
        /// the reason text, exactly as the legacy import does.
        /// </summary>
        public bool? RequiresFreeTextFeedback { get; set; }
    }

    /// <summary>Body of the add endpoint.</summary>
    public class AddTeamQuestionDto
    {
        public string QuestionText { get; set; } = string.Empty;
        public string TeamElement { get; set; } = string.Empty;
        public string SubCategory { get; set; } = string.Empty;

        /// <summary>Defaults to Generic when not supplied.</summary>
        public string? Variant { get; set; }

        /// <summary>
        /// Optional grouping. Omitted or 0 stores the question under the seeded "General"
        /// category, which is how a question meant for everyone is expressed.
        /// </summary>
        public long? CategoryId { get; set; }

        /// <summary>
        /// Rotation position — the workbook's "Ask #". When omitted the next free slot for
        /// this sub-category and variant is used.
        /// </summary>
        public int? Ordinal { get; set; }

        public List<TeamReasonInputDto> PositiveReasons { get; set; } = new List<TeamReasonInputDto>();
        public List<TeamReasonInputDto> NegativeReasons { get; set; } = new List<TeamReasonInputDto>();
    }

    /// <summary>A persisted reason with its identifiers, returned after a write.</summary>
    public class TeamReasonResponseDto
    {
        public long ReasonId { get; set; }
        public string Text { get; set; } = string.Empty;
        public string? TeamState { get; set; }
        public bool RequiresFreeTextFeedback { get; set; }
        public long? MicroActionId { get; set; }
        public string? MicroAction { get; set; }
    }

    /// <summary>What add / import hand back for one question.</summary>
    public class TeamQuestionResponseDto
    {
        public long TeamDailyQuestionId { get; set; }
        public string QuestionText { get; set; } = string.Empty;
        public long SpecificationId { get; set; }
        /// <summary>0 is the seeded "General" category — the question is served to everyone.</summary>
        public long CategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public string TeamElement { get; set; } = string.Empty;
        public string SubCategory { get; set; } = string.Empty;
        public string Variant { get; set; } = string.Empty;
        public int Ordinal { get; set; }
        public List<TeamReasonResponseDto> PositiveReasons { get; set; } = new List<TeamReasonResponseDto>();
        public List<TeamReasonResponseDto> NegativeReasons { get; set; } = new List<TeamReasonResponseDto>();
    }

    // ── Read one question ───────────────────────────────────────────────────

    public class TeamQuestionInfoResponseDto
    {
        public long QuestionId { get; set; }
        public string QuestionText { get; set; } = string.Empty;
        public TeamQuestionSpecificationInfoDto Specification { get; set; } = new TeamQuestionSpecificationInfoDto();
    }

    public class TeamQuestionSpecificationInfoDto
    {
        public long SpecificationId { get; set; }
        /// <summary>0 is the seeded "General" category — the question is served to everyone.</summary>
        public long CategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public string TeamElement { get; set; } = string.Empty;
        public string SubCategory { get; set; } = string.Empty;
        public string Variant { get; set; } = string.Empty;
        public int Ordinal { get; set; }
        public List<TeamReasonInfoDto> Reasons { get; set; } = new List<TeamReasonInfoDto>();
    }

    public class TeamReasonInfoDto
    {
        public long ReasonId { get; set; }
        public string Text { get; set; } = string.Empty;
        public string SentimentType { get; set; } = string.Empty;
        public string? TeamState { get; set; }
        public bool RequiresFreeTextFeedback { get; set; }
        public List<TeamActionInfoDto> Actions { get; set; } = new List<TeamActionInfoDto>();
    }

    public class TeamActionInfoDto
    {
        public long ActionId { get; set; }
        public string Text { get; set; } = string.Empty;
    }

    // ── Update one question ─────────────────────────────────────────────────

    public class UpdateTeamQuestionInfoDto
    {
        public long QuestionId { get; set; }
        public string QuestionText { get; set; } = string.Empty;
        public UpdateTeamQuestionSpecificationInfoDto Specification { get; set; } = new UpdateTeamQuestionSpecificationInfoDto();
    }

    public class UpdateTeamQuestionSpecificationInfoDto
    {
        public long SpecificationId { get; set; }
        public string TeamElement { get; set; } = string.Empty;
        public string SubCategory { get; set; } = string.Empty;
        public string? Variant { get; set; }

        /// <summary>Left null the existing rotation position is kept.</summary>
        public int? Ordinal { get; set; }

        /// <summary>Left null the existing category is kept. 0 moves it to "General".</summary>
        public long? CategoryId { get; set; }

        public List<UpdateTeamReasonInfoDto> Reasons { get; set; } = new List<UpdateTeamReasonInfoDto>();
    }

    public class UpdateTeamReasonInfoDto
    {
        /// <summary>Null / 0 creates a new reason.</summary>
        public long? ReasonId { get; set; }
        public string Text { get; set; } = string.Empty;
        public string SentimentType { get; set; } = string.Empty;
        public string? TeamState { get; set; }
        public bool RequiresFreeTextFeedback { get; set; }

        /// <summary>
        /// True removes the reason and its micro-actions. Unlike legacy — where the soft-delete
        /// flag was never added to the table and the branch does nothing — this deletes the row.
        /// A reason that has already been answered is refused rather than deleted, so the
        /// historical feedback stays readable.
        /// </summary>
        public bool? IsDel { get; set; }

        public List<UpdateTeamActionInfoDto> Actions { get; set; } = new List<UpdateTeamActionInfoDto>();
    }

    public class UpdateTeamActionInfoDto
    {
        /// <summary>Null / 0 creates a new micro-action.</summary>
        public long? ActionId { get; set; }
        public string Text { get; set; } = string.Empty;
        public bool? IsDel { get; set; }
    }

    public class UpdateTeamQuestionInfoResultDto
    {
        public bool IsSuccess { get; set; }
        public string? ErrorMessage { get; set; }
        public TeamQuestionInfoResponseDto? QuestionInfo { get; set; }
    }

    // ── Import ──────────────────────────────────────────────────────────────

    public class ImportTeamQuestionResponseDto
    {
        public int TotalRows { get; set; }
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public List<ImportTeamQuestionRowResultDto> Results { get; set; } = new List<ImportTeamQuestionRowResultDto>();
    }

    public class ImportTeamQuestionRowResultDto
    {
        public int RowNumber { get; set; }
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = string.Empty;
        public string QuestionText { get; set; } = string.Empty;
        public TeamQuestionResponseDto? QuestionData { get; set; }
    }

    public class ImportTeamQuestionSummaryDto
    {
        public int TotalRows { get; set; }
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public List<ImportTeamQuestionErrorDto> Errors { get; set; } = new List<ImportTeamQuestionErrorDto>();
    }

    public class ImportTeamQuestionErrorDto
    {
        public int RowNumber { get; set; }
        public string Message { get; set; } = string.Empty;
        public string QuestionText { get; set; } = string.Empty;
    }

    // ── Bulk update from the workbook ───────────────────────────────────────

    public class UpdateTeamQuestionsFromExcelResponseDto
    {
        public int TotalRowsInExcel { get; set; }
        public int MatchedQuestions { get; set; }
        public int UpdatedQuestions { get; set; }
        public int UpdatedPositiveReasons { get; set; }
        public int UpdatedNegativeReasons { get; set; }
        public int UpdatedPositiveActions { get; set; }
        public int UpdatedNegativeActions { get; set; }
        public int UpdatedTeamStates { get; set; }
        public int SkippedRows { get; set; }
        public int TotalUpdatedReasons => UpdatedPositiveReasons + UpdatedNegativeReasons;
        public int TotalUpdatedActions => UpdatedPositiveActions + UpdatedNegativeActions;
        public List<TeamExcelUpdateRowResultDto> Results { get; set; } = new List<TeamExcelUpdateRowResultDto>();
    }

    public class TeamExcelUpdateRowResultDto
    {
        public int RowNumber { get; set; }
        public string QuestionText { get; set; } = string.Empty;
        public bool IsMatched { get; set; }
        public bool HasChanges { get; set; }
        public string Message { get; set; } = string.Empty;
        public List<string> ChangeDetails { get; set; } = new List<string>();
    }

    // ── Question bank report ────────────────────────────────────────────────

    /// <summary>
    /// The state of the team question bank: totals, how the questions spread across the
    /// three groups and eight conditions, and which questions are still incomplete.
    /// </summary>
    public class TeamQuestionBankReportDto
    {
        public int TotalQuestions { get; set; }
        public int TotalSpecifications { get; set; }
        public int TotalReasons { get; set; }
        public int TotalMicroActions { get; set; }
        public int PositiveReasons { get; set; }
        public int NegativeReasons { get; set; }
        public int ReasonsWithoutMicroAction { get; set; }
        public int ReasonsWithTeamState { get; set; }

        public List<TeamQuestionGroupCountDto> ByCategory { get; set; } = new List<TeamQuestionGroupCountDto>();
        public List<TeamQuestionGroupCountDto> ByTeamElement { get; set; } = new List<TeamQuestionGroupCountDto>();
        public List<TeamQuestionGroupCountDto> BySubCategory { get; set; } = new List<TeamQuestionGroupCountDto>();
        public List<TeamQuestionGroupCountDto> ByVariant { get; set; } = new List<TeamQuestionGroupCountDto>();
        public List<TeamQuestionCoverageDto> Questions { get; set; } = new List<TeamQuestionCoverageDto>();
    }

    public class TeamQuestionGroupCountDto
    {
        public string Name { get; set; } = string.Empty;
        public int QuestionCount { get; set; }
        public int ReasonCount { get; set; }
        public int MicroActionCount { get; set; }
    }

    public class TeamQuestionCoverageDto
    {
        public long QuestionId { get; set; }
        public string QuestionText { get; set; } = string.Empty;
        /// <summary>0 is the seeded "General" category — the question is served to everyone.</summary>
        public long CategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public string TeamElement { get; set; } = string.Empty;
        public string SubCategory { get; set; } = string.Empty;
        public string Variant { get; set; } = string.Empty;
        public int Ordinal { get; set; }
        public int PositiveReasonCount { get; set; }
        public int NegativeReasonCount { get; set; }
        public int MicroActionCount { get; set; }
        public bool HasFreeTextOption { get; set; }

        /// <summary>Populated only when something is missing — empty means the question is complete.</summary>
        public List<string> Gaps { get; set; } = new List<string>();
    }
}
