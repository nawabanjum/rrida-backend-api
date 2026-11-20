using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace CloverBackoffice.Infrastructure.DTO.PlanManagement
{
    /// <summary>
    /// A plan's terms as frozen in a ServicePlanVersion: what a licence bought on that version
    /// gets and costs. Stored as JSON (PascalCase); read case-insensitively.
    /// </summary>
    public class PlanTerms
    {
        public string Currency { get; set; } = "USD";
        public string PricingUnit { get; set; } = "manager";
        public bool AllowsTeamLimitExtension { get; set; }
        public List<PlanTermsTier> PriceTiers { get; set; } = new();
        public List<PlanTermsFeature> Features { get; set; } = new();

        private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

        public string ToJson() => JsonSerializer.Serialize(this);

        public static PlanTerms FromJson(string? json) =>
            string.IsNullOrWhiteSpace(json) ? new PlanTerms() : JsonSerializer.Deserialize<PlanTerms>(json, Json) ?? new PlanTerms();

        public PlanTermsFeature? Feature(string key) =>
            Features.FirstOrDefault(f => string.Equals(f.Key, key, System.StringComparison.OrdinalIgnoreCase));

        /// <summary>The tier whose range holds the quantity; null when none does.</summary>
        public PlanTermsTier? TierFor(int quantity) =>
            PriceTiers.Where(t => t.MinManagers <= quantity && (t.MaxManagers == null || t.MaxManagers >= quantity))
                      .OrderBy(t => t.MinManagers)
                      .FirstOrDefault();
    }

    public class PlanTermsTier
    {
        public int MinManagers { get; set; }
        public int? MaxManagers { get; set; }
        public decimal MonthlyPrice { get; set; }
        public decimal AnnualPrice { get; set; }
    }

    public class PlanTermsFeature
    {
        public int FeatureId { get; set; }
        public string Key { get; set; } = string.Empty;
        public bool IsIncluded { get; set; }
        public int? LimitValue { get; set; }
        public string? DisplayText { get; set; }
        public string? SubText { get; set; }
    }
}
