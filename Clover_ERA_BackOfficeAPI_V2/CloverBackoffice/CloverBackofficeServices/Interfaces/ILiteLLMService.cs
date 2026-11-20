using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Interfaces
{
    public interface ILiteLLMService
    {
        Task<string> GetChatCompletionAsync(string userPrompt, CancellationToken ct = default);
        Task<string> GetChatCompletionAsync(string systemPrompt, string userPrompt, CancellationToken ct = default);
        Task<string> GetChatCompletionAsync(object requestData, CancellationToken ct = default);
        Task<bool> TestConnectionAsync(CancellationToken ct = default);
        // Add this new method for dashboard testing
        Task<string> TestDashboardConnectionAsync(CancellationToken ct = default);
        Task<string> DebugRequestAsync();
    }
}
