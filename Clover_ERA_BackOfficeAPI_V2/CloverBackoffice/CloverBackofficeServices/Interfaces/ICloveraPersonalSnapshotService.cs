using System.Threading;
using System.Threading.Tasks;
using CloverBackoffice.Infrastructure.Models;

namespace CloverBackoffice.Services.Interfaces
{
    public interface ICloveraPersonalSnapshotService
    {
        /// <summary>
        /// Builds the snapshot from the unit/date-range already stored on
        /// <paramref name="snapshot"/>, selects the default active Search-model,
        /// calls Claude with the hardcoded snapshot prompt, parses the JSON, and
        /// writes the parsed fields + telemetry back onto the entity. Does NOT set
        /// Status or persist — the background service owns the run lifecycle and
        /// SaveChanges. Throws on a hard failure (no model, no data, LLM error,
        /// unparseable response).
        /// </summary>
        Task GenerateAsync(CloveraPersonalSnapshot snapshot, CancellationToken ct = default);
    }
}
