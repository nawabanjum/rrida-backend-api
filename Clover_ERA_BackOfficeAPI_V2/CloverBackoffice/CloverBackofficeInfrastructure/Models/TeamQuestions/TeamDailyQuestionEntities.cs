using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CloverBackoffice.Infrastructure.Models.TeamQuestions
{
    // ═════════════════════════════════════════════════════════════════════════
    // RRIDA Team Questions.
    //
    // Deliberately a column-for-column mirror of the legacy CLOVER chain, so the
    // existing patterns, repositories and workflows transfer across:
    //
    //   DailyQuestions              -> TeamDailyQuestions
    //   DailyQuestionSpecifications -> TeamDailyQuestionSpecifications  (+3 fields)
    //   DailyReasons                -> TeamDailyReasons                 (+1 field)
    //   DailyMicroActions           -> TeamDailyMicroActions
    //   DailyFeedback               -> TeamDailyFeedback
    //
    // Only four columns exist here that legacy does not have: TeamElement,
    // SubCategory and Variant on the specification, and TeamState on the reason.
    //
    // Like legacy, the categorisation values are plain int enums rather than
    // lookup tables — CloverCategory sets that precedent.
    //
    // Nothing in the legacy chain is referenced or modified.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>The workbook's "Team Element" — the top-level grouping.</summary>
    public enum TeamElement
    {
        DirectionAndLearning = 1,
        DesignAndMechanics = 2,
        WhatTheTeamBelieves = 3
    }

    /// <summary>The eight team conditions. Team equivalent of CloverCategory.</summary>
    public enum TeamSubCategory
    {
        GoalAffinity = 1,
        Results = 2,
        ImprovementLoop = 3,
        Structure = 4,
        CoordinationQuality = 5,
        Accountability = 6,
        Potency = 7,
        Belonging = 8
    }

    /// <summary>
    /// Generic wording, or the goal-anchored restatement shown when the team has a
    /// goal registered. Goal Affinity is the only condition with both.
    /// </summary>
    public enum TeamQuestionVariant
    {
        Generic = 1,
        GoalAnchored = 2
    }

    /// <summary>
    /// The Team State a reason points to. The four chart quadrants plus the two
    /// non-quadrant signals seen in the workbook.
    /// </summary>
    public enum TeamState
    {
        Cruise = 1,
        Drift = 2,
        Headwinds = 3,
        Stall = 4,
        External = 5,
        Neutral = 6
    }

    /// <summary>Good reason vs bad reason. Mirrors DailyReasonSentimentType.</summary>
    public enum TeamDailyReasonSentimentType
    {
        Positive = 1,
        Negative = 2
    }

    /// <summary>Mirrors DailyFeedbackSentimentType.</summary>
    public enum TeamDailyFeedbackSentimentType
    {
        Angry = 1,
        Sad = 2,
        Indifferent = 3,
        Happy = 4
    }

    /// <summary>
    /// Mirrors DailyQuestion exactly: the words on screen and the language they are in.
    /// Nothing else — categorisation lives on the specification, as it does in legacy.
    /// </summary>
    public class TeamDailyQuestion
    {
        public uint Id { get; init; }

        [MaxLength(1000)]
        public required string Text { get; set; }

        [ForeignKey(nameof(ContentCulture))]
        public required uint ContentCultureId { get; set; }

        public virtual ContentCulture? ContentCulture { get; set; }
    }

    /// <summary>
    /// Mirrors DailyQuestionSpecification. Where legacy carries CloverCategory, this
    /// carries the three team-specific fields; Ordinal, ContentCultureId and the
    /// question link work exactly as they do in legacy.
    ///
    /// There is no set-group or set link: team questions are not organised into
    /// versioned sets, so those two columns have no meaning here.
    /// </summary>
    public class TeamDailyQuestionSpecification
    {
        public uint Id { get; init; }

        /// <summary>Team-specific. The workbook's "Team Element".</summary>
        public required TeamElement TeamElement { get; set; }

        /// <summary>Team-specific. The condition being measured — the team analogue of CloverCategory.</summary>
        public required TeamSubCategory SubCategory { get; set; }

        /// <summary>Team-specific. Generic or goal-anchored wording.</summary>
        public required TeamQuestionVariant Variant { get; set; }

        /// <summary>
        /// Optional grouping — an industry, a sector, a segment. Defaults to
        /// <see cref="TeamQuestionCategory.DefaultCategoryId" /> (0), which is the seeded
        /// "General" category holding the questions that are served to everyone. Not
        /// nullable: the requirement is that an uncategorised question stores 0, and the
        /// seeded row is what lets the foreign key stand behind that value.
        /// </summary>
        public uint CategoryId { get; set; } = TeamQuestionCategory.DefaultCategoryId;

        [ForeignKey(nameof(CategoryId))]
        public virtual TeamQuestionCategory? Category { get; set; }

        /// <summary>Position in the rotation, as in legacy.</summary>
        public required ushort Ordinal { get; set; }

        [ForeignKey(nameof(ContentCulture))]
        public required uint ContentCultureId { get; set; }

        public virtual ContentCulture? ContentCulture { get; set; }

        [ForeignKey(nameof(Question))]
        public required uint TeamDailyQuestionId { get; set; }

        public virtual TeamDailyQuestion? Question { get; set; }

        public virtual ICollection<TeamDailyReason> Reasons { get; set; } = new List<TeamDailyReason>();
    }

    /// <summary>
    /// Mirrors DailyReason, including hanging off the SPECIFICATION rather than the
    /// question — that is how legacy does it. The single addition is TeamState.
    /// </summary>
    public class TeamDailyReason
    {
        public uint Id { get; init; }

        public required TeamDailyReasonSentimentType TeamDailyReasonSentimentType { get; set; }

        [MaxLength(1000)]
        public required string Text { get; set; }

        public required bool RequiresFreeTextFeedback { get; set; }

        /// <summary>
        /// Team-specific. The Team State this reason indicates — a good reason and a
        /// bad reason on the same question point at different states. Nullable because
        /// the requirement workbooks leave most of these cells unfilled.
        /// </summary>
        public TeamState? TeamState { get; set; }

        [ForeignKey(nameof(Specification))]
        public required uint SpecificationId { get; set; }

        public virtual TeamDailyQuestionSpecification? Specification { get; set; }

        [ForeignKey(nameof(ContentCulture))]
        public required uint ContentCultureId { get; set; }

        public virtual ContentCulture? ContentCulture { get; set; }
    }

    /// <summary>Mirrors DailyMicroAction: one piece of text hanging off a reason.</summary>
    public class TeamDailyMicroAction
    {
        public uint Id { get; init; }

        [MaxLength(1000)]
        public required string Text { get; set; }

        [ForeignKey(nameof(ContentCulture))]
        public required uint ContentCultureId { get; set; }

        public virtual ContentCulture? ContentCulture { get; set; }

        [ForeignKey(nameof(Reason))]
        public required uint TeamDailyReasonId { get; set; }

        public virtual TeamDailyReason? Reason { get; set; }
    }

    /// <summary>
    /// Mirrors DailyFeedback. The only changes are that the question and reason links
    /// point at the team tables. As in legacy there is no UserId — responses are
    /// anonymous, and the response rate is rows against BusinessUnit.TeamSize.
    /// </summary>
    public class TeamDailyFeedback
    {
        public uint Id { get; init; }

        public required DateTime SubmissionDateTime { get; init; }

        [ForeignKey(nameof(BusinessUnit))]
        public required uint BusinessUnitId { get; set; }

        public virtual BusinessUnit? BusinessUnit { get; set; }

        public TeamDailyFeedbackSentimentType SentimentType { get; init; }

        [ForeignKey(nameof(Question))]
        public uint TeamDailyQuestionId { get; init; }

        public virtual TeamDailyQuestion? Question { get; set; }

        [ForeignKey(nameof(Reason))]
        public uint TeamDailyReasonId { get; init; }

        public virtual TeamDailyReason? Reason { get; set; }

        [MaxLength(896)]
        public string? FreeText { get; init; }
    }
}
