using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Services.Interfaces;
using System;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Implementation
{
    /// <summary>Pass-through to <see cref="ITeamSetupRepository" />.</summary>
    public class TeamSetupService : ITeamSetupService
    {
        private readonly ITeamSetupRepository _repository;
        private readonly ITeamOnboardingNotificationService _onboardingNotification;

        public TeamSetupService(
            ITeamSetupRepository repository,
            ITeamOnboardingNotificationService onboardingNotification)
        {
            _repository = repository;
            _onboardingNotification = onboardingNotification;
        }

        public async Task<UpsertTeamSetupResultDto> UpsertTeamSetupAsync(UpsertTeamSetupDto dto, int? actorUserId)
        {
            var result = await _repository.UpsertTeamSetupAsync(dto, actorUserId);

            // A manager who set the team up themselves does not need a "a team has been
            // assigned to you" email about it. Marked the same way create-manager marks it,
            // so the onboarding job's catch-up pass does not send it either.
            if (result.IsSuccess && result.AssignedManagerUserId.HasValue && result.TeamSetup != null)
            {
                try
                {
                    await _onboardingNotification.SuppressAssignmentNotificationAsync(
                        result.AssignedManagerUserId.Value, (uint)result.TeamSetup.UnitId,
                        "manager set this team up themselves; no assignment email needed");
                }
                catch
                {
                    // Swallowed by design; the setup is committed. Worst case is one extra email.
                }
            }

            return result;
        }

        public Task<TeamSetupResultDto> GetTeamSetupByUnitIdAsync(long unitId) =>
            _repository.GetTeamSetupByUnitIdAsync(unitId);

        public Task<UpsertTeamSetupResultDto> UpdateTeamAttributesAsync(UpdateTeamAttributesDto dto, int? actorUserId) =>
            _repository.UpdateTeamAttributesAsync(dto, actorUserId);

        public Task<UpsertTeamSetupResultDto> UpdateTeamGoalsAsync(UpdateTeamGoalsDto dto, int? actorUserId) =>
            _repository.UpdateTeamGoalsAsync(dto, actorUserId);
    }
}
