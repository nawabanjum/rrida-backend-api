using CloverBackoffice.Infrastructure.DTO;
using System.Threading;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Interfaces
{
    public interface IAIGatewayService
    {
        /// <summary>
        /// Send a chat completion request. Routes to the best model
        /// based on AIModelConfig rules with automatic fallback.
        /// </summary>
        Task<AIGatewayResponseDto> ChatCompletionAsync(
            string systemPrompt,
            string userContent,
            AIGatewayRequestDto? options = null,
            CancellationToken ct = default);

        /// <summary>
        /// Same as ChatCompletionAsync but enforces JSON output.
        /// Strips markdown wrappers. Retries with fallback on parse failure.
        /// </summary>
        Task<AIGatewayResponseDto> ChatCompletionWithJsonAsync(
            string systemPrompt,
            string userContent,
            AIGatewayRequestDto? options = null,
            CancellationToken ct = default);
    }
}
