using System.Text.RegularExpressions;

namespace CloverBackoffice.Services.Implementation
{
    /// <summary>
    /// What a Claude model accepts in a Messages API request, by model id, for the hand-built
    /// requests in this project. Newer models reject parameters older ones need:
    /// <list type="bullet">
    /// <item><b>Sampling</b> (<c>temperature</c>, <c>top_p</c>, <c>top_k</c>): removed on Claude Opus 4.7 and
    /// later, Claude Sonnet 5 and later, Claude Haiku 5.5 and the Fable/Mythos models; sending one
    /// is a 400 ("`top_p` is deprecated for this model."). Only Claude 3.x and the Opus/Sonnet/Haiku
    /// 4 family up to 4.6 take them.</item>
    /// <item><b>Thinking budgets</b> (<c>thinking: {type: "enabled", budget_tokens}</c>): only Claude 3.7 and
    /// the 4 family up to 4.5 use them. From 4.6 on thinking is adaptive, and on Claude Opus 5.5 the
    /// budget form is a 400.</item>
    /// </list>
    /// A model id this does not recognise is treated as a new model: no sampling, adaptive thinking.
    /// </summary>
    public static class AnthropicModelRules
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(500);

        // claude-opus-4, claude-sonnet-4-5, claude-haiku-4-5-20251001, claude-sonnet-4@20250514,
        // claude-opus-4-1-v1:0 - the Opus/Sonnet/Haiku 4 family up to the given minor version.
        private static Regex Family4UpTo(char lastMinor) => new(
            $@"^claude-(?:opus|sonnet|haiku)-4(?:-[0-{lastMinor}])?(?:-\d{{8}}|@\d{{8}})?(?:-v\d+(?::\d+)?)?$",
            RegexOptions.CultureInvariant, Timeout);

        private static readonly Regex SamplingFamily4 = Family4UpTo('6');
        private static readonly Regex BudgetFamily4 = Family4UpTo('5');
        private static readonly Regex Claude3 = new(@"^claude-(?:2|instant|3)", RegexOptions.CultureInvariant, Timeout);
        private static readonly Regex Claude37 = new(@"^claude-3-7", RegexOptions.CultureInvariant, Timeout);

        /// <summary>The model id from "claude-" on (drops a Bedrock "us.anthropic." style prefix), lower case.</summary>
        private static string Id(string? model)
        {
            var name = (model ?? string.Empty).Trim().ToLowerInvariant();
            var at = name.IndexOf("claude-", StringComparison.Ordinal);
            return at < 0 ? string.Empty : name[at..];
        }

        /// <summary>Whether the request may carry <c>temperature</c> / <c>top_p</c>.</summary>
        public static bool AcceptsSampling(string? model)
        {
            var id = Id(model);
            return id.Length > 0 && (Claude3.IsMatch(id) || SamplingFamily4.IsMatch(id));
        }

        /// <summary>Whether extended thinking is asked for with a token budget rather than adaptively.</summary>
        public static bool UsesThinkingBudget(string? model)
        {
            var id = Id(model);
            return id.Length > 0 && (Claude37.IsMatch(id) || BudgetFamily4.IsMatch(id));
        }

        /// <summary>
        /// Adds the sampling parameter the project's model configuration sets (<c>top_p</c> when it is
        /// below 1, otherwise <c>temperature</c>; Claude never takes both), and only for a model that
        /// accepts sampling.
        /// </summary>
        public static void AddSampling(IDictionary<string, object?> body, string? model, double temperature, double topP)
        {
            if (!AcceptsSampling(model)) return;
            if (topP < 1.0) body["top_p"] = topP;
            else body["temperature"] = temperature;
        }
    }
}
