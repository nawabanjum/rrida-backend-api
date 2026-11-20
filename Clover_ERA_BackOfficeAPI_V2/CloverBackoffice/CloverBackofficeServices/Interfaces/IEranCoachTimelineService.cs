using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;

namespace CloverBackoffice.Services.Interfaces
{
    public interface IEranCoachTimelineService
    {
        /// <summary>
        /// One page of the team's Eran thread, newest <paramref name="take"/> items older than
        /// <paramref name="before"/>, returned oldest first. Throws UnauthorizedAccessException
        /// when the team is not one this user may see.
        /// With <paramref name="cycleId"/>, only that cycle's engine messages (no chat), and
        /// InvalidOperationException when the team has no such cycle.
        /// </summary>
        Task<EranCoachTimelineDto> GetAsync(User actor, uint unitId, DateTime? before, int take, long? cycleId = null, CancellationToken ct = default);
    }
}
