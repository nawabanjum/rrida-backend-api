using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;

namespace CloverBackoffice.Services.Interfaces
{
    /// <summary>
    /// The manager's side of the deterministic layer, and the note the team sees.
    ///
    /// Reads come from the stored results. Writes record a fact and then replay the team,
    /// so the manager sees what their confirmation changed straight away.
    /// </summary>
    public interface IEngineManagerService
    {
        Task<EngineTeamStateDto> GetStateAsync(User actor, uint businessUnitId, CancellationToken ct = default);

        /// <summary>The one active action, as delivered. Null when nothing is running.</summary>
        Task<EngineActiveActionDto?> GetActiveActionAsync(User actor, uint businessUnitId, CancellationToken ct = default);

        /// <summary>One cycle's action as delivered, open or resolved. Null when the team has no such cycle.</summary>
        Task<EngineActiveActionDto?> GetCycleActionAsync(User actor, uint businessUnitId, long cycleId, CancellationToken ct = default);

        Task<List<EngineFeedItemDto>> GetFeedAsync(User actor, uint businessUnitId, int take, CancellationToken ct = default);

        /// <summary>D31: the outward condition on the team's newest daily message, or null when it carries none.</summary>
        Task<EngineOutwardConditionDto?> GetOutwardConditionAsync(User actor, uint businessUnitId, CancellationToken ct = default) =>
            Task.FromResult<EngineOutwardConditionDto?>(null);

        /// <summary>The 72-hour confirmation: one tap, plus whether the condition was met.</summary>
        Task<EngineActiveActionDto?> ConfirmAsync(User actor, EngineConfirmRequestDto dto, CancellationToken ct = default);

        /// <summary>Declining a stated contraindication. Recorded as a failed cycle with its reason.</summary>
        Task<EngineActiveActionDto?> DeclineAsync(User actor, EngineDeclineRequestDto dto, CancellationToken ct = default);

        /// <summary>"I'll do it". Recorded once per rung; it confirms nothing and runs nothing.</summary>
        Task<EngineActiveActionDto?> AcknowledgeAsync(User actor, EngineAcknowledgeRequestDto dto, CancellationToken ct = default);

        /// <summary>What a team member sees: the notes, and nothing else.</summary>
        Task<List<EngineTeamNoteDto>> GetTeamNotesAsync(uint businessUnitId, int take, CancellationToken ct = default);
    }
}
