using System;
using System.Collections.Generic;

namespace CloverBackoffice.Infrastructure.DTO
{
    // ═══════════════════════════════════════════════════════════════════════════
    // Team Setup — API contracts.
    //
    // One request carries everything the five-step setup screen collects: the unit
    // itself (name, size, parent), the team type, the manager's context and the
    // team's goals. It is stored as one operation — the unit, its TeamSetups row,
    // its TeamGoals and the goal audit rows all land together or not at all.
    //
    // ProfileId and UnitId are named exactly that on purpose: CompanyTenantGuardFilter
    // finds them by name and refuses a request that reaches into another tenant.
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Body of the team-setup upsert endpoint.
    ///
    /// <c>UnitId</c> 0 creates the team; anything else edits the team with that id,
    /// which is the same convention <c>upsert-team</c> uses for its <c>Id</c>.
    /// </summary>
    public class UpsertTeamSetupDto
    {
        /// <summary>The team being set up. 0 creates a new one.</summary>
        public long UnitId { get; set; }

        /// <summary>The company the team belongs to. Required on create.</summary>
        public long ProfileId { get; set; }

        /// <summary>Step 1. Stored on the business unit, as it always has been.</summary>
        public string TeamName { get; set; } = string.Empty;

        /// <summary>
        /// Step 1. At least two: RRIDA only ever reports at team level, so a single
        /// response could never stay anonymous.
        /// </summary>
        public int? TeamSize { get; set; }

        /// <summary>
        /// Optional parent unit. Three states on an edit: omitted (or null) leaves the
        /// stored parent alone, 0 clears it, and any other value re-parents the team. The
        /// setup screen has no parent step, so an omitted field must not re-root the team.
        /// </summary>
        public long? ParentId { get; set; }

        /// <summary>
        /// Step 2. "Interdependent" or "IndependentContributors". The numeric enum value
        /// (1 or 2) is accepted too, so a client that stores the raw value still works.
        /// </summary>
        public string? TeamType { get; set; }

        /// <summary>Step 3. The manager's own words. At least 20 characters.</summary>
        public string? Context { get; set; }

        /// <summary>
        /// Step 4. Two to five goals. The list is the whole set: a goal that was stored
        /// before and is absent here is removed (soft-deleted) and audited as such.
        /// </summary>
        public List<TeamGoalInputDto> Goals { get; set; } = new();
    }

    /// <summary>One goal in the setup request.</summary>
    public class TeamGoalInputDto
    {
        /// <summary>
        /// 0 for a goal being added. An existing id edits that goal in place, which is
        /// what keeps its history on one thread rather than reading as delete-plus-add.
        /// </summary>
        public long TeamGoalId { get; set; }

        public string GoalText { get; set; } = string.Empty;

        /// <summary>
        /// Position in the list, 1-based. Left at 0, the position in the request is used,
        /// so a caller that just sends the goals in order gets the order it sent.
        /// </summary>
        public short DisplayOrder { get; set; }

        /// <summary>The date the goal is aimed at, when there is one.</summary>
        public DateOnly? TargetDate { get; set; }

        /// <summary>
        /// Whether the goal is still being worked towards. Defaults to true; a retired
        /// goal stays on the team and out of the check-in.
        /// </summary>
        public bool IsActive { get; set; } = true;
    }

    /// <summary>
    /// Outcome of a setup write. A validation failure carries the reason and no record;
    /// the controller turns that into a failed response without a 500.
    /// </summary>
    public class UpsertTeamSetupResultDto
    {
        public bool IsSuccess { get; set; }
        public string? ErrorMessage { get; set; }

        /// <summary>True when this request created the team, false when it edited one.</summary>
        public bool IsNewTeam { get; set; }

        /// <summary>
        /// The Team Manager this request made the manager of the new team — the person who
        /// set it up. Null on an edit, or when the caller does not hold the Team Manager role.
        /// </summary>
        public int? AssignedManagerUserId { get; set; }

        public TeamSetupRecordDto? TeamSetup { get; set; }
    }

    /// <summary>
    /// A team and everything its setup holds. The read endpoint and the write endpoint
    /// both return this, so the screen can render straight from either.
    /// </summary>
    public class TeamSetupRecordDto
    {
        // ── The unit ──
        public long UnitId { get; set; }
        public long ProfileId { get; set; }
        public string TeamName { get; set; } = string.Empty;
        public int? TeamSize { get; set; }
        public long? ParentId { get; set; }

        /// <summary>
        /// The team's invite code — the one the last step of the wizard shows. Already
        /// on the business unit; no second code is minted for setup.
        /// </summary>
        public string TeamCode { get; set; } = string.Empty;

        // ── The setup row ──

        /// <summary>Null when the team exists but has never been through setup.</summary>
        public long? TeamSetupId { get; set; }

        /// <summary>"Interdependent" or "IndependentContributors". Null before setup.</summary>
        public string? TeamType { get; set; }

        public string? Context { get; set; }

        /// <summary>
        /// False when the team predates setup, or was created by the older team endpoint.
        /// The setup row existing is what this means — there is no separate flag.
        /// </summary>
        public bool IsSetupComplete { get; set; }

        public int? CreatedBy { get; set; }
        public int? UpdatedBy { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        // ── The goals ──
        public List<TeamGoalRecordDto> Goals { get; set; } = new();
    }

    /// <summary>One stored goal.</summary>
    public class TeamGoalRecordDto
    {
        public long TeamGoalId { get; set; }
        public long UnitId { get; set; }
        public long ProfileId { get; set; }
        public string GoalText { get; set; } = string.Empty;
        public short DisplayOrder { get; set; }
        public DateOnly? TargetDate { get; set; }
        public bool IsActive { get; set; }
        public int? CreatedBy { get; set; }
        public int? UpdatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>
    /// Outcome of the read. A team that does not exist is a failed result with the
    /// reason; a team that exists but has never been set up is a SUCCESS carrying the
    /// unit, an empty goal list and <c>IsSetupComplete = false</c> — the screen needs
    /// to tell those two apart.
    /// </summary>
    public class TeamSetupResultDto
    {
        public bool IsSuccess { get; set; }
        public string? ErrorMessage { get; set; }
        public TeamSetupRecordDto? TeamSetup { get; set; }
    }

    /// <summary>
    /// Body of <c>PUT api/team-setup/team-attributes</c> — the dashboard's "Edit
    /// attributes" modal. Changes the team's name, size and type ONLY: the context and
    /// the goals are left exactly as stored, unlike upsert-team-setup which rewrites all.
    /// </summary>
    public class UpdateTeamAttributesDto
    {
        /// <summary>The team being edited. Must already exist.</summary>
        public long UnitId { get; set; }

        /// <summary>The company the team belongs to.</summary>
        public long ProfileId { get; set; }

        public string TeamName { get; set; } = string.Empty;

        /// <summary>At least two. Null leaves the stored size unchanged.</summary>
        public int? TeamSize { get; set; }

        /// <summary>
        /// "Interdependent" or "IndependentContributors" (the modal's two choices), or the
        /// numeric enum value 1 / 2.
        /// </summary>
        public string? TeamType { get; set; }
    }

    /// <summary>
    /// Body of <c>PUT api/team-setup/team-goals</c> — the dashboard's "Edit team goals"
    /// modal. Changes the goals ONLY; the team's name, size, type and context are left
    /// as stored.
    /// </summary>
    public class UpdateTeamGoalsDto
    {
        /// <summary>The team being edited. Must already exist.</summary>
        public long UnitId { get; set; }

        /// <summary>The company the team belongs to.</summary>
        public long ProfileId { get; set; }

        /// <summary>
        /// The COMPLETE list after the edit, two to five goals. <c>teamGoalId</c> 0 adds a
        /// goal, an existing id edits it in place, and a stored goal left out is removed
        /// (soft-deleted, audited).
        /// </summary>
        public List<TeamGoalInputDto> Goals { get; set; } = new();
    }
}
