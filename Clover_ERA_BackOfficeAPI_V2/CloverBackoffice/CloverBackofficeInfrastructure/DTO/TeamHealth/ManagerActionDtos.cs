using System;
using System.Collections.Generic;

namespace CloverBackoffice.Infrastructure.DTO.TeamHealth
{
    /// <summary>
    /// POST api/actions. Actions are team-based: teamId and title are required. The creator is
    /// the logged-in user. startDate and dueDate are "YYYY-MM-DD" or "DD-MM-YYYY", any day (past,
    /// today, future). startDate is required, dueDate optional and never before startDate.
    /// </summary>
    public class CreateManagerActionDto
    {
        public uint? TeamId { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? StartDate { get; set; }
        public string? DueDate { get; set; }

        /// <summary>
        /// Optional: the ERAN suggestion this action follows, an engineActionId from
        /// GET api/actions/eran-suggestions. Sent alone it means suggestionChoice SUGGESTION.
        /// </summary>
        public long? EngineActionId { get; set; }

        /// <summary>
        /// Optional: SUGGESTION (with engineActionId), NONE or NONE_OF_THESE (without it).
        /// Neither field sent: no link, as before.
        /// </summary>
        public string? SuggestionChoice { get; set; }
    }

    /// <summary>
    /// PATCH api/actions/{id}. Only the fields sent change; "dueDate": "" and
    /// "description": "" clear them. status: OPEN, IN_PROGRESS, DONE or CANCELLED.
    /// engineActionId / suggestionChoice (manual actions only) re-pick the ERAN suggestion as
    /// on create; "suggestionChoice": "" clears it.
    /// </summary>
    public class UpdateManagerActionDto
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? DueDate { get; set; }
        public string? Status { get; set; }
        public long? EngineActionId { get; set; }
        public string? SuggestionChoice { get; set; }
    }

    /// <summary>
    /// GET api/actions/eran-suggestions: the dropdown on the Create Action form. The team's
    /// latest suggestions from ERAN (the deterministic engine), newest first, plus the two
    /// fixed choices.
    /// </summary>
    public class EranSuggestionListDto
    {
        public uint TeamId { get; set; }
        public List<EranSuggestionDto> Suggestions { get; set; } = new();
        public List<SuggestionOptionDto> OtherOptions { get; set; } = new();
    }

    public class EranSuggestionDto
    {
        /// <summary>Send this as engineActionId on POST / PATCH api/actions.</summary>
        public long EngineActionId { get; set; }

        /// <summary>ERAN's wording, as delivered.</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>"YYYY-MM-DD", the day ERAN suggested it.</summary>
        public string? SuggestedOn { get; set; }

        /// <summary>1 or 2: a rung two is the follow-up suggestion of the same cycle.</summary>
        public int Rung { get; set; }

        /// <summary>A manager took it up with "I'll do it".</summary>
        public bool TakenUp { get; set; }
    }

    /// <summary>A fixed dropdown choice: send value as suggestionChoice.</summary>
    public class SuggestionOptionDto
    {
        public string Value { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
    }

    /// <summary>The ERAN suggestion a manual action was linked to.</summary>
    public class LinkedSuggestionDto
    {
        /// <summary>The suggestion's current api/team-health/actions id (the one picked if it is gone).</summary>
        public long? EngineActionId { get; set; }

        /// <summary>ERAN's wording when it was picked.</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>"YYYY-MM-DD" or null.</summary>
        public string? SuggestedOn { get; set; }

        public int? Rung { get; set; }

        /// <summary>False when the engine no longer has that suggestion (a re-run dropped its cycle).</summary>
        public bool IsAvailable { get; set; }
    }

    /// <summary>
    /// POST api/actions/{id}/comments. A manual action takes a comment at any status, so a
    /// comment can be saved before Mark Done. An engine action ("engine-57") goes through the
    /// Team Health action comments, under their rules.
    /// </summary>
    public class AddManagerActionCommentDto
    {
        /// <summary>Required, at most 500 characters.</summary>
        public string? Text { get; set; }
    }

    /// <summary>GET api/actions filters. Everything optional; RRIDA Admin needs profileId or teamId.</summary>
    public class ManagerActionQueryDto
    {
        public uint? ProfileId { get; set; }
        public uint? TeamId { get; set; }
        public int? CreatedBy { get; set; }
        public string? Status { get; set; }
        public string? Source { get; set; }
        public int? Take { get; set; }
    }

    /// <summary>
    /// One team action, whichever way it came: created by a manager (source MANUAL, actionId
    /// "12") or recommended by the engine and taken up with "I'll do it" (source ENGINE,
    /// actionId "engine-57", where 57 is the api/team-health/actions id).
    /// </summary>
    public class ManagerActionDto
    {
        /// <summary>"12" for a manager's action, "engine-57" for an engine action.</summary>
        public string ActionId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }

        public uint ProfileId { get; set; }
        public uint TeamId { get; set; }
        public string? TeamName { get; set; }

        /// <summary>
        /// Who created it (the logged-in user). For an engine action, the manager who took it
        /// up ("I'll do it").
        /// </summary>
        public int? CreatedBy { get; set; }
        public string? CreatedByName { get; set; }

        /// <summary>Manual action: the start date set on create, "YYYY-MM-DD" or null. Engine action: null.</summary>
        public string? StartDate { get; set; }

        /// <summary>"YYYY-MM-DD" or null.</summary>
        public string? DueDate { get; set; }

        /// <summary>Due date passed and still OPEN / IN_PROGRESS.</summary>
        public bool IsOverdue { get; set; }

        /// <summary>MANUAL (created by a manager) or ENGINE (recommended by the engine).</summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>
        /// OPEN, IN_PROGRESS, DONE or CANCELLED. An engine action is IN_PROGRESS while in flight
        /// and DONE once closed.
        /// </summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>A manual action: when it was created. An engine action: when it was taken up.</summary>
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }

        /// <summary>
        /// Engine action: its api/team-health/actions id. Manual action: the ERAN suggestion it
        /// is linked to (same id space), null when not linked. source stays MANUAL either way.
        /// </summary>
        public long? EngineActionId { get; set; }

        /// <summary>Manual action: SUGGESTION, NONE, NONE_OF_THESE or null (not picked).</summary>
        public string? SuggestionChoice { get; set; }

        /// <summary>Manual action linked to an ERAN suggestion: its details; otherwise null.</summary>
        public LinkedSuggestionDto? LinkedSuggestion { get; set; }

        /// <summary>Engine action only: running, closed, failed or cannot_attribute (the engine's own outcome).</summary>
        public string? EngineOutcome { get; set; }

        /// <summary>Its comments, oldest first: the same shape as an engine action's comments.</summary>
        public List<TeamActionCommentDto> Comments { get; set; } = new();
        public int CommentCount { get; set; }
    }

    public class ManagerActionListDto
    {
        public int Total { get; set; }
        public List<ManagerActionDto> Actions { get; set; } = new();
    }
}
