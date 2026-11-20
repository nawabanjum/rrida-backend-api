using System;
using System.ComponentModel.DataAnnotations;

namespace CloverBackoffice.Infrastructure.DTO
{
    // Productivity / time-allocation row for a unit's daily reflections.
    // Populated via raw SQL (FeedbackRepository.GetWorkAllocationDataAsync):
    // DailyReflections ⋈ TimeAllocation ⋈ WorkCategories.
    // Fed into the Clovera personal snapshot alongside engagement feedback.
    public class WorkAllocationResultDto
    {
        // One row per TimeAllocation; its PK is the natural key for this projection.
        [Key]
        public long TimeAllocationId { get; set; }

        public long DailyReflectionId { get; set; }
        public DateTime ReflectionLocalDate { get; set; }

        public decimal Hours { get; set; }

        public long WorkCategoryId { get; set; }
        public string? Category { get; set; }
        public string? Title { get; set; }
        public string? Subtitle { get; set; }
    }
}
