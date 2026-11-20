using CloverBackoffice.Infrastructure.DTO;

namespace CloverBackoffice.Services.Interfaces
{
    /// <summary>
    /// Capture for the deterministic layer: enrolling a member, telling them what they are
    /// asked today, and recording the answer. Everything written is a fact the engine is
    /// replayed from.
    /// </summary>
    public interface IEngineCheckInService
    {
        /// <summary>Enrol a member on a team by its join code. Enrolling twice is not an error.</summary>
        Task<EngineEnrolResultDto> EnrolAsync(EngineEnrolRequestDto dto, CancellationToken ct = default);

        /// <summary>
        /// Remove a member from a team, as the app does when they are taken off it. Ends their
        /// membership today; leaving twice is not an error.
        /// </summary>
        Task<EngineLeaveResultDto> LeaveAsync(EngineLeaveRequestDto dto, CancellationToken ct = default);

        /// <summary>Today's questions with their chip sets, and whether this member has answered yet.</summary>
        Task<EngineTodayDto> TodayAsync(string memberToken, uint businessUnitId, CancellationToken ct = default);

        /// <summary>
        /// <see cref="TodayAsync"/> for a client that has the team's short code rather than its
        /// id. The code is looked up as enrol looks it up; the answer is the same. Read-only.
        /// </summary>
        Task<EngineTodayDto> TodayByCodeAsync(string memberToken, string businessUnitShortCode, CancellationToken ct = default);

        /// <summary>
        /// The team's next asked days after today, up to five: each with its questions and
        /// chip sets and that day's custom question, in the same shape as
        /// <see cref="TodayAsync"/>. Days the team is not asked are skipped. Read-only.
        /// </summary>
        Task<EngineNextDto> NextAsync(string memberToken, uint businessUnitId, CancellationToken ct = default);

        /// <summary>
        /// What a team is asked, for a caller with no member: the same questions
        /// <see cref="TodayAsync"/> gives on that day. Today when the team is asked today,
        /// otherwise its next asked day; check <c>Date</c>. Null when the engine is off for
        /// the team. Sets up a team that has no engine settings yet, as enrol does.
        /// </summary>
        Task<EngineTodayDto?> TeamDayAsync(uint businessUnitId, CancellationToken ct = default);

        /// <summary>Record one answer. One per member per track per day; a reason is compulsory.</summary>
        Task<EngineAnswerResultDto> SubmitAsync(EngineAnswerRequestDto dto, CancellationToken ct = default);
    }
}
