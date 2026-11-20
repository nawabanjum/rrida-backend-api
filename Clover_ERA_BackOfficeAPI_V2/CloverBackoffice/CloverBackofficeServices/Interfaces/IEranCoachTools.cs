using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Interfaces
{
    /// <summary>
    /// The Claude-native retrieval tools for EranCoach: get_worksheet, get_micro_action,
    /// search_clover. <see cref="Definitions"/> is the Anthropic tool schema sent with the
    /// request; <see cref="RunAsync"/> executes a requested tool and returns the text that
    /// is fed back as the tool_result. Ported from the reference tools.ts.
    /// </summary>
    public interface IEranCoachTools
    {
        IReadOnlyList<object> Definitions { get; }

        /// <summary>
        /// The business unit this chat turn is scoped to (from the request), used as the default
        /// team for the report tools when the model doesn't name one. Set per request before the
        /// tool loop runs; null when the request carried no unit.
        /// </summary>
        long? CurrentUnitId { get; set; }

        // Set per request from the manager's plan: false when it has no Clover Resources (Light),
        // so get_worksheet refuses and search_clover leaves worksheets out.
        bool WorksheetsAllowed { get; set; }

        Task<string> RunAsync(string toolName, string inputJson, CancellationToken ct = default);
    }
}
