namespace CloverBackoffice.Infrastructure.DTO
{
    // Team capacity of ONE manager licence: the purchased team plus the
    // complimentary (same-manager) teams riding on it. Single source of truth
    // for the grant guard and the "N of 3" usage returned by the licence APIs.
    //
    // A RRIDA Admin can raise it per profile or per licence (TeamLimitExtensions,
    // see ProfileTeamLimits); MaxTeamsPerLicense is the default without one.
    //
    // Checked only when a new complimentary grant is created, so a licence that
    // already covers more teams (granted under an older limit) is never revoked
    // or trimmed by a lower value here.
    public static class LicenseTeamLimits
    {
        public const int MaxTeamsPerLicense = 3;

        // The purchased team takes one slot; the rest are complimentary.
        public const int MaxComplimentaryTeamsPerLicense = MaxTeamsPerLicense - 1;

        public static string FormatUsage(int teamsUsed) => FormatUsage(teamsUsed, MaxTeamsPerLicense);

        public static string FormatUsage(int teamsUsed, int maxTeams) => $"{teamsUsed} of {maxTeams}";

        // A plan whose team limit is "Unlimited" (no number) counts as this many teams.
        public const int Unlimited = 999;

        // Plan catalogue: the comparison row holding a plan's team limit, and the plan a
        // licence from before plans gives (every existing licence was sold at the Pro price).
        public const string TeamsFeatureKey = "MANAGERS_AND_TEAMS";
        public const string LegacyPlanCode = "pro";
    }

    // The team limits of one profile. Without a plan catalogue every licence allows
    // TeamsPerLicense teams (3 + the profile's extra) plus its own extra. With one, each
    // licence's limit comes from its plan (PlanLicenseLimits): the plan's MANAGERS_AND_TEAMS
    // value, plus the profile and licence extensions only when the plan allows extension
    // (Pro 3 + extra, Light 1). A licence from before plans reads as Pro, so it is unchanged.
    public class ProfileTeamLimits
    {
        public int TeamsPerLicense { get; init; } = LicenseTeamLimits.MaxTeamsPerLicense;

        // Extra teams per licence id, from licence-level extensions only.
        public IReadOnlyDictionary<int, int> LicenseExtraTeams { get; init; } = new Dictionary<int, int>();

        // Full team limit per licence id from its plan. Empty when there is no plan catalogue.
        public IReadOnlyDictionary<int, int> PlanLicenseLimits { get; init; } = new Dictionary<int, int>();

        // Licences whose plan does not allow a RRIDA Admin extension (Light).
        public IReadOnlySet<int> NonExtendableLicenseIds { get; init; } = new HashSet<int>();

        public int LimitOf(int? companyLicenseId)
        {
            if (companyLicenseId.HasValue && PlanLicenseLimits.TryGetValue(companyLicenseId.Value, out var planLimit))
                return planLimit;
            return TeamsPerLicense
                   + (companyLicenseId.HasValue && LicenseExtraTeams.TryGetValue(companyLicenseId.Value, out var extra) ? extra : 0);
        }

        public bool AllowsExtension(int companyLicenseId) => !NonExtendableLicenseIds.Contains(companyLicenseId);
    }
}
