using CloverBackoffice.Infrastructure.Models;

namespace CloverBackoffice.Services.Implementation
{
    /// <summary>
    /// Which model Eran runs on. One rule for the chat and for the feed, so the Eran a
    /// manager talks to and the Eran that writes their daily message are the same one.
    /// </summary>
    public static class EranModelPriority
    {
        /// <summary>ModelConfiguration.ModelConfigTypeId for Search-models.</summary>
        public const int SearchModelTypeId = 1;

        /// <summary>
        /// An admin's explicit choice (IsSelected) first, then Claude Sonnet, then GPT-4o,
        /// then the default, then the first active model.
        /// </summary>
        public static ModelConfiguration Pick(IReadOnlyList<ModelConfiguration>? models)
        {
            if (models == null || models.Count == 0)
                throw new InvalidOperationException("No active Search-model found in ModelConfiguration.");

            static bool Has(ModelConfiguration m, params string[] tokens)
            {
                var hay = $"{m.ModelName} {m.FriendlyName} {m.Provider}".ToLowerInvariant();
                return tokens.All(t => hay.Contains(t));
            }

            var selected = models.FirstOrDefault(m => m.IsSelected);
            selected ??= models.FirstOrDefault(m => Has(m, "claude", "sonnet"))
                         ?? models.FirstOrDefault(m => Has(m, "sonnet"));
            selected ??= models.FirstOrDefault(m => Has(m, "gpt-4o"))
                         ?? models.FirstOrDefault(m => Has(m, "gpt4o"));
            selected ??= models.FirstOrDefault(m => m.IsDefault) ?? models.First();
            return selected;
        }
    }
}
