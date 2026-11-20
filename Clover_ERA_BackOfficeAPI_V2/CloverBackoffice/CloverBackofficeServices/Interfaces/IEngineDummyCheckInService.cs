using CloverBackoffice.Infrastructure.DTO;

namespace CloverBackoffice.Services.Interfaces
{
    /// <summary>
    /// TEST DATA: answers today's engine check-in for the teams configured under
    /// "EngineDummyCheckIn", with answers picked at random from the test-data workbook.
    /// </summary>
    public interface IEngineDummyCheckInService
    {
        /// <summary>
        /// One run for today. Does nothing when the feature is disabled or no team is
        /// configured. Safe to repeat: a member who already answered today is skipped.
        /// </summary>
        Task<EngineDummyCheckInResultDto> RunAsync(CancellationToken ct = default);
    }
}
