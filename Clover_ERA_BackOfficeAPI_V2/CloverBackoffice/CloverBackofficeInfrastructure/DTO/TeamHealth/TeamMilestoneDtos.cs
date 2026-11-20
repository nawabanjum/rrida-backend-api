namespace CloverBackoffice.Infrastructure.DTO.TeamHealth
{
    /// <summary>
    /// Body of POST api/team-health/milestones (upsert). <c>Id</c> 0 or absent adds a
    /// milestone; an existing id edits that milestone. <c>UnitId</c> names the team; the
    /// global tenant guard reads it from the body and refuses another company's team
    /// before the action runs.
    ///
    /// Fields are plain strings on purpose: a wrong type or a missing field reaches the
    /// service's own validation and comes back as a 400 in the house envelope, rather
    /// than as a framework ProblemDetails.
    /// </summary>
    public class UpsertTeamMilestoneDto
    {
        /// <summary>0 / absent = add; the milestone's id = edit it.</summary>
        public uint Id { get; set; }

        /// <summary>The team (business unit) the milestone belongs to.</summary>
        public uint UnitId { get; set; }

        /// <summary>Short title, at most 40 characters.</summary>
        public string? Title { get; set; }

        /// <summary>What changed, and for whom. At most 200 characters.</summary>
        public string? Description { get; set; }

        /// <summary>
        /// The day it happened: "YYYY-MM-DD" (what a date input sends) or "DD-MM-YYYY".
        /// Stored and returned as "YYYY-MM-DD". Past dates are allowed, future ones are not.
        /// </summary>
        public string? Date { get; set; }
    }

    /// <summary>One milestone as the Add Milestone modal and the timeline read it.</summary>
    public class TeamMilestoneDto
    {
        public uint Id { get; set; }
        public uint UnitId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        /// <summary>"YYYY-MM-DD".</summary>
        public string Date { get; set; } = string.Empty;

        public int? CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>Why a milestone call was refused; the controller maps it to a status code.</summary>
    public enum TeamMilestoneError
    {
        None = 0,
        BadRequest,
        NotFound,
        Forbidden,

        /// <summary>The record changed since the caller read it (409).</summary>
        Conflict,

        /// <summary>The request was fine but the work could not be done (500).</summary>
        ServerError
    }

    /// <summary>Outcome of a milestone service call.</summary>
    public class TeamMilestoneResult<T>
    {
        public T? Data { get; init; }
        public TeamMilestoneError Error { get; init; }
        public string Message { get; init; } = string.Empty;

        public bool IsSuccess => Error == TeamMilestoneError.None;

        public static TeamMilestoneResult<T> Ok(T data) => new() { Data = data };

        public static TeamMilestoneResult<T> Fail(TeamMilestoneError error, string message) =>
            new() { Error = error, Message = message };
    }
}
