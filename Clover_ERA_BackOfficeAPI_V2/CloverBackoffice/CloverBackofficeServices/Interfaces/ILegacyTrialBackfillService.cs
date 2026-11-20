namespace CloverBackoffice.Services.Interfaces
{
    // One-time migration that moves legacy companies (created before the
    // subscription system, i.e. with NO subscription row) onto a default Trial.
    public interface ILegacyTrialBackfillService
    {
        Task<LegacyTrialBackfillResult> RunAsync(int trialDays = 30, bool dryRun = false);

        // On-demand bootstrap for a SINGLE company that has NO subscription at
        // all (created via Create-Company but never trialed/subscribed). Creates
        // a {trialDays}-day Trial subscription, mints one Trial license per
        // existing team, and assigns each license to its team. No-op (returns
        // false) when the company already has any subscription — so trial/paid
        // companies and their unassigned license pools are never touched.
        // Returns true when a trial was bootstrapped.
        Task<bool> EnsureTrialForCompanyAsync(uint profileId, int trialDays = 30);
    }

    public class LegacyTrialBackfillResult
    {
        public bool DryRun { get; set; }
        public int TrialDays { get; set; }
        public DateTime TrialEndUtc { get; set; }
        public int LegacyCompanyCount { get; set; }
        public int TotalLicensesCreated { get; set; }
        public List<LegacyCompanyResult> Profiles { get; set; } = new();
    }

    public class LegacyCompanyResult
    {
        public uint ProfileId { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public int UnitCount { get; set; }
        public int LicensesCreated { get; set; }
        public string Status { get; set; } = string.Empty; // "Migrated" | "Preview" | "Error: ..."
    }
}
