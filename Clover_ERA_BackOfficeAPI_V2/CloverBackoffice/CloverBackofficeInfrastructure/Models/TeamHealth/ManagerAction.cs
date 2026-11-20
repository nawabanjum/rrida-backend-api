using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CloverBackoffice.Infrastructure.Models.TeamHealth
{
    /// <summary>
    /// An action a manager decided on themselves for a team, independent of any weekly report
    /// or engine suggestion: "Run a retro on the release", maybe due on a day. Actions are
    /// team-based only; whoever created it (CreatedBy, from the login) is recorded with it.
    ///
    /// Eran is given the open ones of a team when it writes that team's daily messages and
    /// answers in chat, and the engine's weekly and monthly messages report them as activity.
    ///
    /// Engine actions (EngineCycleDeliveries + TeamActionStates) and the old report Action Hub
    /// (ActionHubs) are separate and untouched; <see cref="Source"/> names where a row came from,
    /// so a system-generated action can live here later without a new table.
    /// </summary>
    [Table("ManagerActions", Schema = "public")]
    public class ManagerAction
    {
        public const int TitleMaxLength = 200;
        public const int DescriptionMaxLength = 2000;
        public const int SuggestionTextMaxLength = 1000;

        public long Id { get; init; }

        /// <summary>The company the team belongs to.</summary>
        [ForeignKey(nameof(Profile))]
        public required uint ProfileId { get; set; }

        public virtual Profile? Profile { get; set; }

        /// <summary>The team the action is for.</summary>
        [ForeignKey(nameof(BusinessUnit))]
        public required uint UnitId { get; set; }

        public virtual BusinessUnit? BusinessUnit { get; set; }

        [MaxLength(TitleMaxLength)]
        public required string Title { get; set; }

        [MaxLength(DescriptionMaxLength)]
        public string? Description { get; set; }

        /// <summary>The day the manager starts on it, set on create. Null for older rows.</summary>
        public DateOnly? StartDate { get; set; }

        public DateOnly? DueDate { get; set; }

        /// <summary><see cref="ManagerActionSources"/>: MANUAL for everything the API creates.</summary>
        [MaxLength(30)]
        public required string Source { get; set; }

        /// <summary><see cref="ManagerActionStatuses"/>.</summary>
        [MaxLength(20)]
        public required string Status { get; set; }

        /// <summary>When it was marked DONE; cleared when it is reopened.</summary>
        public DateTime? CompletedAt { get; set; }

        // ── The ERAN suggestion the manager linked it to (optional) ──
        // The action stays MANUAL and the manager's own: nothing is written to the engine's
        // tables, so it never becomes an engine action or a timeline dot. The engine rebuilds
        // its deliveries on every run, so the link is kept by the engine's natural key
        // (cycle key, rung, action key) beside the id, with ERAN's wording as it was picked.

        /// <summary>
        /// <see cref="ManagerActionSuggestionChoices"/>: SUGGESTION (linked to one), NONE or
        /// NONE_OF_THESE. Null when the manager was not asked or left it empty.
        /// </summary>
        [MaxLength(20)]
        public string? SuggestionChoice { get; set; }

        /// <summary>The suggestion's EngineCycleDeliveries id when it was picked (api/team-health/actions id).</summary>
        public long? EngineDeliveryId { get; set; }

        [MaxLength(20)]
        public string? EngineCycleKey { get; set; }

        public int? EngineRung { get; set; }

        [MaxLength(30)]
        public string? EngineActionKey { get; set; }

        /// <summary>ERAN's wording of the suggestion when it was picked.</summary>
        [MaxLength(SuggestionTextMaxLength)]
        public string? EngineSuggestionText { get; set; }

        /// <summary>The day ERAN suggested it.</summary>
        public DateOnly? EngineSuggestedOn { get; set; }

        // ── House audit / soft-delete convention ──
        /// <summary>The logged-in user who created it.</summary>
        public int CreatedBy { get; set; }
        public int? UpdatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public bool IsDel { get; set; }
    }

    /// <summary>
    /// A manager's comment on one of their own actions (POST api/actions/{id}/comments), at any
    /// status. Comments on engine actions stay in TeamActionComments.
    /// </summary>
    [Table("ManagerActionComments", Schema = "public")]
    public class ManagerActionComment
    {
        /// <summary>The same limit as a comment on an engine action.</summary>
        public const int TextMaxLength = TeamActionComment.TextMaxLength;

        public long Id { get; init; }

        [ForeignKey(nameof(ManagerAction))]
        public required long ManagerActionId { get; set; }

        public virtual ManagerAction? ManagerAction { get; set; }

        [MaxLength(TextMaxLength)]
        public required string Text { get; set; }

        /// <summary>The logged-in user who wrote it.</summary>
        public int CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsDel { get; set; }
    }

    public static class ManagerActionSources
    {
        /// <summary>Created by a manager through POST api/actions.</summary>
        public const string Manual = "MANUAL";

        /// <summary>Reserved for an action generated from a weekly report.</summary>
        public const string ReportGenerated = "REPORT_GENERATED";

        /// <summary>Reserved for an action generated by the system (AI).</summary>
        public const string System = "SYSTEM";

        /// <summary>
        /// Listing only, never stored here: an engine action taken up with "I'll do it", read
        /// from the engine's own rows (EngineCycleDeliveries + TeamActionStates).
        /// </summary>
        public const string Engine = "ENGINE";

        public static readonly string[] All = [Manual, ReportGenerated, System, Engine];
    }

    /// <summary>The "ERAN suggestion" dropdown on a manual action.</summary>
    public static class ManagerActionSuggestionChoices
    {
        /// <summary>Linked to one of ERAN's suggestions (engineActionId).</summary>
        public const string Suggestion = "SUGGESTION";

        /// <summary>"None": the action is not related to a suggestion.</summary>
        public const string None = "NONE";

        /// <summary>"None of these": the manager saw the suggestions and none fitted.</summary>
        public const string NoneOfThese = "NONE_OF_THESE";

        public static readonly string[] All = [Suggestion, None, NoneOfThese];
    }

    public static class ManagerActionStatuses
    {
        public const string Open = "OPEN";
        public const string InProgress = "IN_PROGRESS";
        public const string Done = "DONE";
        public const string Cancelled = "CANCELLED";

        public static readonly string[] All = [Open, InProgress, Done, Cancelled];

        /// <summary>Still to be done: what Eran is told about.</summary>
        public static readonly string[] Active = [Open, InProgress];
    }
}
