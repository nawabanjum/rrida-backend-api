using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CloverBackoffice.Infrastructure.Models.PlanManagement
{
    /// <summary>
    /// One row of the plan comparison table, e.g. "Team-of-teams Portfolio View", shared by all
    /// plans. What each plan gives for it is a <see cref="ServicePlanFeature"/>.
    ///
    /// <see cref="Key"/> is what code checks ("PORTFOLIO_VIEW", "ERAN_COACH"), so it never changes
    /// once created; title, description and the per-plan values stay freely editable. Features are
    /// never deleted, only deactivated.
    /// </summary>
    [Table("PlanFeatures", Schema = "public")]
    public class PlanFeature
    {
        public const int KeyMaxLength = 60;
        public const int TitleMaxLength = 150;
        public const int DescriptionMaxLength = 500;
        public const int LimitUnitMaxLength = 50;
        public const int FootnoteMaxLength = 500;

        public int Id { get; init; }

        /// <summary>UPPER_SNAKE_CASE, unique, immutable.</summary>
        [MaxLength(KeyMaxLength)]
        public required string Key { get; set; }

        [MaxLength(TitleMaxLength)]
        public required string Title { get; set; }

        [MaxLength(DescriptionMaxLength)]
        public string? Description { get; set; }

        /// <summary><see cref="PlanFeatureValueTypes"/>.</summary>
        [MaxLength(20)]
        public required string ValueType { get; set; }

        /// <summary>LIMIT features: what is counted, e.g. "teams", "chats".</summary>
        [MaxLength(LimitUnitMaxLength)]
        public string? LimitUnit { get; set; }

        /// <summary>LIMIT features: <see cref="PlanFeatureLimitPeriods"/>.</summary>
        [MaxLength(20)]
        public string? LimitPeriod { get; set; }

        /// <summary>Note printed under the table for this row, e.g. "* Clover Framework includes …".</summary>
        [MaxLength(FootnoteMaxLength)]
        public string? Footnote { get; set; }

        /// <summary>
        /// Seeded feature whose key the product code relies on: its value type cannot change and
        /// it cannot be deactivated.
        /// </summary>
        public bool IsSystem { get; set; }

        /// <summary>
        /// Shown as a row of the comparison table. False for a feature that is enforced but
        /// shown as part of another row (CLOVER_RESOURCES = "*with Clover Resources").
        /// </summary>
        public bool ShowInComparison { get; set; } = true;

        public int SortOrder { get; set; }

        public bool IsActive { get; set; }

        public int CreatedBy { get; set; }
        public int? UpdatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public static class PlanFeatureValueTypes
    {
        /// <summary>Included or not (✓ / –).</summary>
        public const string Included = "INCLUDED";

        /// <summary>A number per plan; null = Unlimited (e.g. 10 chats per month).</summary>
        public const string Limit = "LIMIT";

        /// <summary>Text per plan only (e.g. "First reading walk-through").</summary>
        public const string Text = "TEXT";

        public static readonly string[] All = [Included, Limit, Text];
    }

    public static class PlanFeatureLimitPeriods
    {
        /// <summary>A standing limit, e.g. 3 teams.</summary>
        public const string None = "NONE";

        /// <summary>Resets every calendar month, e.g. 10 chats per month.</summary>
        public const string Month = "MONTH";

        public static readonly string[] All = [None, Month];
    }
}
