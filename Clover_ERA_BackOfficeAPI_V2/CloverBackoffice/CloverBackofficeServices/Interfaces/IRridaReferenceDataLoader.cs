using CloverBackoffice.Infrastructure.DTO;

namespace CloverBackoffice.Services.Interfaces
{
    /// <summary>
    /// Loads the vendored kit's content into the Ref* tables as one new immutable version.
    ///
    /// These tables do not feed the engine — it reads kit/data itself. They exist so the
    /// mobile app can be served a question with its chip sets, and so a stored id can be
    /// resolved for display months later, against the version that was live at the time.
    /// </summary>
    public interface IRridaReferenceDataLoader
    {
        /// <summary>
        /// Writes a new version and makes it the active one, in a single transaction. The
        /// previous version keeps every row it had. Refuses to write if what was loaded
        /// disagrees with the counts the kit declares.
        /// </summary>
        Task<RefDataLoadResultDto> LoadAsync(int? actorUserId, CancellationToken ct = default);

        /// <summary>The active version with its counts, or null when nothing is loaded.</summary>
        Task<RefDataVersionDto?> GetActiveAsync(CancellationToken ct = default);

        /// <summary>A question of the active version with its three chip sets, in authored order.</summary>
        Task<RefQuestionDto?> GetQuestionAsync(int questionKey, CancellationToken ct = default);
    }
}
