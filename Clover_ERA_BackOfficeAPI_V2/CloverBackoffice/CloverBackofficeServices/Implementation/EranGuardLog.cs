using Microsoft.Extensions.Logging;

namespace CloverBackoffice.Services.Implementation
{
    /// <summary>
    /// One event per guard catch (the brief, section 2): at, team, day, kind, attempt, outcome,
    /// rule labels and model. Never the reply, the prompt, the matched words, a name or an answer.
    /// </summary>
    public static class EranGuardLog
    {
        public const string Passed = "passed";
        public const string Retried = "retried";
        public const string PassedOnRetry = "passed_on_retry";
        public const string Fallback = "fallback";
        public const string FallbackFinal = "fallback_final";
        public const string CacheEvicted = "cache_evicted";

        /// <summary>A recent message written under an older prompt, cleared to be written again (MVP 0729864, item 5).</summary>
        public const string PromptRetired = "prompt_retired";

        /// <summary>The stored prose is the fixed card, not model output.</summary>
        public static bool IsCard(string? guardOutcome) => guardOutcome is Fallback or FallbackFinal;

        public static void Catch(ILogger logger, string kind, uint teamId, int day, int attempt, string outcome, string rules, string? model)
        {
            var level = outcome is Fallback or FallbackFinal ? LogLevel.Warning : LogLevel.Information;
            logger.Log(level,
                "[EranGuard] at={At:o} team={Team} day={Day} kind={Kind} attempt={Attempt} outcome={Outcome} rules=[{Rules}] model={Model}",
                DateTime.UtcNow, teamId, day, kind, attempt, outcome, rules, model ?? "none");
        }

        /// <summary>Even the fixed card broke a rule: a data bug in the library text, raised as an error.</summary>
        public static void FallbackAlert(ILogger logger, string kind, uint teamId, int day, string rules) =>
            logger.LogError(
                "[EranGuard] ALERT the fallback card itself broke [{Rules}] for team={Team} day={Day} kind={Kind}; the headline alone was delivered.",
                rules, teamId, day, kind);
    }
}
