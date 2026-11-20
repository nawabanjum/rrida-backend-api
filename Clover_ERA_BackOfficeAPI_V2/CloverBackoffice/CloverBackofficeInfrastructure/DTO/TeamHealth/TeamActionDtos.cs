namespace CloverBackoffice.Infrastructure.DTO.TeamHealth
{
    // Team Health → Action Management: the action details panel behind an action dot
    // on the timeline. An action is one rung of one engine cycle (EngineCycleDeliveries);
    // actionId is that row's id. Edits, status and comments are the manager's own record
    // and never change what the engine reads or decides.

    /// <summary>
    /// Body of PATCH api/team-health/actions/{actionId}. Only the fields sent change.
    /// Fields are plain strings so a wrong type reaches the service's own validation.
    /// </summary>
    public class UpdateTeamActionDto
    {
        /// <summary>The manager's wording, at most 1000 characters. Absent/null = unchanged.</summary>
        public string? Text { get; set; }

        /// <summary>"YYYY-MM-DD" or "DD-MM-YYYY", on or after the action's date. "" clears it; absent/null = unchanged.</summary>
        public string? DueDate { get; set; }

        /// <summary>The version the caller last read. When sent and stale, the update is refused (409).</summary>
        public int? Version { get; set; }
    }

    /// <summary>Body of PATCH api/team-health/actions/{actionId}/status.</summary>
    public class UpdateTeamActionStatusDto
    {
        /// <summary>CLOSED (mark as closed) or IN_FLIGHT (reopen).</summary>
        public string? Status { get; set; }

        /// <summary>The version the caller last read. When sent and stale, the update is refused (409).</summary>
        public int? Version { get; set; }
    }

    /// <summary>Body of POST api/team-health/actions/{actionId}/comments.</summary>
    public class AddTeamActionCommentDto
    {
        /// <summary>At most 500 characters.</summary>
        public string? Text { get; set; }
    }

    /// <summary>One action in flight on the timeline: enough to draw its dot.</summary>
    public class TimelineActionDto
    {
        public long ActionId { get; set; }

        /// <summary>EngineCycles.Id — the cycle this action belongs to (as weekly-flags cycleId).</summary>
        public long CycleId { get; set; }

        /// <summary>1 or 2: the first move, or the structural one after it.</summary>
        public int Rung { get; set; }

        /// <summary>The day a manager took it up ("I'll do it"), "YYYY-MM-DD".</summary>
        public string Date { get; set; } = string.Empty;

        /// <summary>The manager's wording when edited, otherwise the text as delivered.</summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>IN_FLIGHT or CLOSED — which dot to draw (open ring or tick).</summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>The manager's due date, "YYYY-MM-DD", or null.</summary>
        public string? DueDate { get; set; }

        /// <summary>running, closed, failed or cannot_attribute — the engine's own outcome.</summary>
        public string EngineOutcome { get; set; } = string.Empty;

        /// <summary>When and by whom it was taken up ("I'll do it").</summary>
        public DateTime AcknowledgedAt { get; set; }
        public TeamActionUserDto? AcknowledgedBy { get; set; }

        public int CommentCount { get; set; }

        /// <summary>The action's comments, oldest first.</summary>
        public List<TeamActionCommentDto> Comments { get; set; } = new();
    }

    /// <summary>GET api/team-health/actions?teamId=&amp;from=&amp;to= — a team's actions, oldest first.</summary>
    public class TeamActionListDto
    {
        public string TeamId { get; set; } = string.Empty;

        /// <summary>The range asked for, "YYYY-MM-DD"; null when not sent.</summary>
        public string? From { get; set; }
        public string? To { get; set; }

        public List<TimelineActionDto> Actions { get; set; } = new();
    }

    /// <summary>The action details panel.</summary>
    public class TeamActionDto
    {
        public long ActionId { get; set; }
        public string TeamId { get; set; } = string.Empty;

        /// <summary>EngineCycles.Id — the cycle this action belongs to (as weekly-flags cycleId).</summary>
        public long CycleId { get; set; }

        /// <summary>1 or 2: the first move, or the structural one after it.</summary>
        public int Rung { get; set; }

        /// <summary>The day a manager took it up ("I'll do it"), "YYYY-MM-DD".</summary>
        public string Date { get; set; } = string.Empty;

        /// <summary>The manager's wording when edited, otherwise the text as delivered.</summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>The text exactly as the engine delivered it. Never changes.</summary>
        public string OriginalText { get; set; } = string.Empty;

        public bool IsEdited { get; set; }

        /// <summary>The condition the manager confirms against, as delivered.</summary>
        public string? WorkedIf { get; set; }

        /// <summary>"YYYY-MM-DD", or null when none is set.</summary>
        public string? DueDate { get; set; }

        /// <summary>IN_FLIGHT or CLOSED.</summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>"engine" while it follows the engine's outcome; "manager" once a manager has closed or reopened it.</summary>
        public string StatusSource { get; set; } = string.Empty;

        /// <summary>running, closed, failed or cannot_attribute.</summary>
        public string EngineOutcome { get; set; } = string.Empty;

        /// <summary>When the manager confirmed it was done (engine confirmation), "YYYY-MM-DD".</summary>
        public string? ConfirmedOn { get; set; }

        /// <summary>When and by whom it was taken up ("I'll do it").</summary>
        public DateTime? AcknowledgedAt { get; set; }
        public TeamActionUserDto? AcknowledgedBy { get; set; }

        /// <summary>Set while a manager has it CLOSED.</summary>
        public DateTime? ClosedAt { get; set; }
        public TeamActionUserDto? ClosedBy { get; set; }

        public bool CanEdit { get; set; }
        public bool CanClose { get; set; }
        public bool CanReopen { get; set; }

        /// <summary>Send back on PATCH to refuse a stale write. 0 until the first manager change.</summary>
        public int Version { get; set; }

        /// <summary>Oldest first.</summary>
        public List<TeamActionCommentDto> Comments { get; set; } = new();
    }

    public class TeamActionCommentDto
    {
        public long CommentId { get; set; }
        public string Text { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public TeamActionUserDto? CreatedBy { get; set; }
    }

    public class TeamActionUserDto
    {
        public int UserId { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>The engine outcome words the API returns.</summary>
    public static class TeamActionEngineOutcomes
    {
        public const string Running = "running";
    }
}
