using CloverBackoffice.Infrastructure.DTO;

namespace CloverBackoffice.Services.Interfaces
{
    public interface IResponseValidatorService
    {
        /// <summary>
        /// Runs all 7 PromptOps assertions against raw LLM JSON output.
        /// Uses default ratio (4 themes, 3 recommendations, 3 action steps).
        /// </summary>
        ValidationResultDto Validate(string rawJson);

        /// <summary>
        /// Runs all 7 PromptOps assertions against raw LLM JSON output.
        /// Parses ratio from systemPrompt if present (e.g. "4 themes, 3 recommendations, 3 action steps"),
        /// otherwise applies defaults (4/3/3).
        /// </summary>
        ValidationResultDto Validate(string rawJson, string? systemPrompt);
    }
}
