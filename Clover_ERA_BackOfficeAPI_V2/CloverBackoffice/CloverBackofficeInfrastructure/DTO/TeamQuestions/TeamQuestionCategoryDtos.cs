using System;

namespace CloverBackoffice.Infrastructure.DTO
{
    // ═══════════════════════════════════════════════════════════════════════════
    // Team question categories — an optional grouping for the question bank, so a
    // set of questions can be authored for one industry, sector or segment.
    //
    // Category 0 ("General") is seeded and always exists. It holds the questions
    // that are served to everyone, and it is what an uncategorised question is
    // assigned to. It cannot be renamed away, deactivated or deleted.
    // ═══════════════════════════════════════════════════════════════════════════

    public class CreateTeamQuestionCategoryDto
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }

        /// <summary>Defaults to true when omitted.</summary>
        public bool? IsActive { get; set; }
    }

    public class UpdateTeamQuestionCategoryDto
    {
        public long CategoryId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }

        /// <summary>Left null the current value is kept.</summary>
        public bool? IsActive { get; set; }
    }

    public class TeamQuestionCategoryDto
    {
        public long CategoryId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsActive { get; set; }

        /// <summary>True only for the seeded general category (Id 0).</summary>
        public bool IsDefault { get; set; }

        /// <summary>How many team questions are currently assigned to this category.</summary>
        public int QuestionCount { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>
    /// Outcome of a category write. A rule the caller broke — duplicate name, editing the
    /// default category — comes back as a failed result carrying the reason, not a 500.
    /// </summary>
    public class TeamQuestionCategoryResultDto
    {
        public bool IsSuccess { get; set; }
        public string? ErrorMessage { get; set; }
        public TeamQuestionCategoryDto? Category { get; set; }
    }
}
