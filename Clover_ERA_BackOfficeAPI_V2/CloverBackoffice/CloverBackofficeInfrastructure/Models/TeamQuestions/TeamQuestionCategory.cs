using System;
using System.ComponentModel.DataAnnotations;

namespace CloverBackoffice.Infrastructure.Models.TeamQuestions
{
    /// <summary>
    /// A grouping a team question can be authored for — an industry, a sector, a customer
    /// segment. Optional by design: a question that applies to everyone carries
    /// <see cref="DefaultCategoryId" />.
    ///
    /// Why the default is a real row rather than a null:
    /// the requirement is that an uncategorised question stores CategoryId = 0, and a plain
    /// nullable column with no row behind it would leave the specification pointing at a
    /// value the database cannot vouch for. Seeding one row at Id 0 keeps the foreign key
    /// honest AND keeps the stored value 0, so both hold at once. The identity sequence
    /// starts at 1, so nothing else can ever take that Id.
    /// </summary>
    public class TeamQuestionCategory
    {
        /// <summary>
        /// The general / default category. Questions here are served to everyone.
        /// Seeded by the AddTeamQuestionCategories migration and protected from deletion.
        /// </summary>
        public const uint DefaultCategoryId = 0;

        public uint Id { get; set; }

        [MaxLength(200)]
        public required string Name { get; set; }

        [MaxLength(1000)]
        public string? Description { get; set; }

        /// <summary>
        /// False hides the category from the authoring screens without touching the
        /// questions already assigned to it.
        /// </summary>
        public bool IsActive { get; set; } = true;

        public int? CreatedBy { get; set; }
        public int? UpdatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public bool IsDel { get; set; }
    }
}
