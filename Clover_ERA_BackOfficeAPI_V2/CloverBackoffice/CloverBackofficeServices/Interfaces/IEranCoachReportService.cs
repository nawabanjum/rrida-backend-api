using System.Threading;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Interfaces
{
    /// <summary>
    /// Backs the EranCoach report tools. Resolves the signed-in caller, enforces role-based unit
    /// scoping (Manager / ProfileAdmin, via <see cref="IUnitAccessService"/>),
    /// and returns the stored Top Themes / Recommendations / Actions as plain text the model reads
    /// back as a tool_result. Every string returned is safe to feed to the model verbatim.
    /// </summary>
    public interface IEranCoachReportService
    {
        /// <summary>
        /// Top Themes / Recommendations / Actions for a team's report. Team is resolved in order:
        /// explicit <paramref name="requestedUnitId"/>, then <paramref name="teamName"/> matched against
        /// the caller's accessible teams, then <paramref name="fallbackUnitId"/> (the unit the chat
        /// request carried), then the caller's only team. When still ambiguous, returns a team
        /// pick-list or name-match shortlist instead of insights. <paramref name="reportId"/> null
        /// means the latest report.
        /// </summary>
        Task<string> GetReportInsightsAsync(long? requestedUnitId, long? reportId, string? teamName, long? fallbackUnitId, CancellationToken ct = default);

        /// <summary>
        /// Lists what the caller can pull: the teams they can access, or a single team's reports
        /// (id + date range) when the team is unambiguous, supplied by id, or matched by name.
        /// </summary>
        Task<string> ListReportsAsync(long? requestedUnitId, string? teamName, long? fallbackUnitId, CancellationToken ct = default);

        /// <summary>
        /// Resolves a manager-typed team name against the teams they can access, returning the
        /// match(es) with their unit ids so the model can then pull the right report.
        /// </summary>
        Task<string> FindTeamAsync(string teamName, CancellationToken ct = default);
    }
}
