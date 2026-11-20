namespace CloverBackoffice.Services.Interfaces
{
    // A half-open-ish licensed window [Start, End] (both inclusive for filtering).
    public readonly record struct LicensedInterval(System.DateTime Start, System.DateTime End);

    // Resolves each team's LICENSED time intervals (purchased assignments +
    // complimentary grants, with legacy grandfathering) so read-side metric
    // queries can exclude activity generated while a team had no active license.
    // Shared by the Team Structure and Aggregate Dashboard reads to guarantee
    // both apply identical licensing rules.
    //
    // Special units (FeedbackAutoImport:BusinessUnitIds) are exempt from all
    // gating here: they always read as licensed regardless of assignments/grants,
    // so downstream metrics treat them exactly as they did before licensing.
    public interface ILicensedPeriodService
    {
        // Licensed intervals per unit id. Units with no license ever map to an
        // empty list. Timestamps are UTC.
        Task<Dictionary<uint, List<LicensedInterval>>> GetLicensedIntervalsAsync(IEnumerable<long> unitIds);

        // True when a UTC timestamp falls inside any licensed interval of the unit.
        bool IsLicensed(Dictionary<uint, List<LicensedInterval>> intervals, uint unitId, DateTime timestampUtc);

        // Earliest interval start across all units — a safe lower bound for
        // metric fetches (nothing before this can be licensed). Returns UtcNow
        // when nothing is licensed.
        DateTime EarliestIntervalStart(Dictionary<uint, List<LicensedInterval>> intervals);

        // Subset of the supplied unit ids that hold an ACTIVE license right now:
        // a live purchased assignment (IsActive, not unassigned) or a live
        // complimentary grant (not revoked), whose backing license has not
        // expired. Used by read-side metrics that must count only currently
        // licensed units and exclude every unlicensed one.
        Task<HashSet<long>> GetActivelyLicensedUnitIdsAsync(IEnumerable<long> unitIds);

        // True when the unit is one of the special units configured in
        // FeedbackAutoImport:BusinessUnitIds. These are exempt from licensing and
        // must be indistinguishable from a normally licensed unit everywhere —
        // same flags, same data, no separate status. Exposed so any code path that
        // derives licence state from raw assignment rows (rather than from this
        // service) can apply the SAME exemption instead of re-reading config.
        bool IsExemptFromLicensing(uint unitId);

        // Subset of GetActivelyLicensedUnitIdsAsync whose active license is
        // COMPLIMENTARY only — a live grant and no live purchased assignment.
        // A unit holding both consumes a purchased license, so it is reported as
        // purchased, not complimentary. Special units are never complimentary.
        // Always a subset of the actively-licensed set, so callers can present it
        // as "of the N active licenses, M are complimentary".
        Task<HashSet<long>> GetActivelyComplimentaryUnitIdsAsync(IEnumerable<long> unitIds);
    }
}
