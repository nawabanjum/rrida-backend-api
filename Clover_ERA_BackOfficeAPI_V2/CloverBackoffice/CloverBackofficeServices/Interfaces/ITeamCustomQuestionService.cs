using CloverBackoffice.Infrastructure.DTO.TeamHealth;

namespace CloverBackoffice.Services.Interfaces
{
    /// <summary>"Schedule a custom question" on the Team Health page.</summary>
    public interface ITeamCustomQuestionService
    {
        /// <summary>
        /// The team's scheduled and previously asked custom questions, with the slot count.
        /// <paramref name="from"/> / <paramref name="to"/> (optional, inclusive) narrow the
        /// previously asked list to questions asked in that range.
        /// </summary>
        Task<TeamMilestoneResult<TeamCustomQuestionsDto>> GetAsync(string? teamId, int actorUserId, string? from = null, string? to = null, CancellationToken ct = default);

        /// <summary>Schedules a one-off question; returns the team's updated lists.</summary>
        Task<TeamMilestoneResult<TeamCustomQuestionsDto>> ScheduleAsync(ScheduleCustomQuestionDto? dto, int actorUserId, CancellationToken ct = default);

        /// <summary>Removes (cancels) a scheduled question; returns the team's updated lists.</summary>
        Task<TeamMilestoneResult<TeamCustomQuestionsDto>> RemoveAsync(uint customQuestionId, int actorUserId, CancellationToken ct = default);
    }
}
