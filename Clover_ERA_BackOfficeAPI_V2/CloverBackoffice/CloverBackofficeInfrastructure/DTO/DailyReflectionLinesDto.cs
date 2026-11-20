using System;
using System.ComponentModel.DataAnnotations;

namespace CloverBackoffice.Infrastructure.DTO
{
    // Productivity reflection lines for a unit's daily reflections.
    // Populated via raw SQL (FeedbackRepository.GetDailyReflectionLinesAsync) from
    // the DailyReflections table's text[] columns. Fed into the Clovera personal
    // snapshot's read-time stats (blockers / threads landed / threads total) and the
    // Output tab's completed / pending item lists — no LLM, no persistence.
    public class DailyReflectionLinesDto
    {
        // One row per DailyReflection; its PK is the natural key for this projection.
        [Key]
        public long Id { get; set; }

        public DateTime ReflectionLocalDate { get; set; }

        // Postgres text[] columns → string[]. May be null when the user logged nothing.
        public string[]? AchievementsLines { get; set; }
        public string[]? NotDoneLines { get; set; }
        public string[]? BlockersLines { get; set; }
    }
}
