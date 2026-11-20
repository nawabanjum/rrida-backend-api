using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace CloverBackoffice.Services.Implementation
{
    // See ILicensedPeriodService. Reconstructs licensed intervals from
    // AssignedCompanyLicenses (purchased) ∪ ComplimentaryLicenseGrants
    // (complimentary), each capped by the license's own ExpiryDate, then merged.
    //
    // Legacy grandfathering: a team registered BEFORE Licensing:LaunchDateUtc
    // (or every team when that config is unset/unparseable) has its earliest
    // interval extended back to the team's RegistrationDate, so pre-feature
    // history is preserved. Teams registered on/after the launch date are
    // filtered strictly to their real licensed periods.
    //
    // Special units: ids listed in FeedbackAutoImport:BusinessUnitIds are a
    // separate category — neither "licensed" nor "unlicensed". They are exempt
    // from all license gating and behave exactly as they did before licensing
    // (every moment since registration is treated as licensed), so their data
    // is never filtered out of any read-side metric.
    public class LicensedPeriodService : ILicensedPeriodService
    {
        private readonly ApplicationDbContext _context;
        private readonly DateTime? _launchDateUtc;
        private readonly HashSet<uint> _specialUnitIds;

        public LicensedPeriodService(ApplicationDbContext context, IConfiguration configuration)
        {
            _context = context;
            var launchRaw = configuration["Licensing:LaunchDateUtc"];
            _launchDateUtc = DateTime.TryParse(
                launchRaw,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                out var launch)
                ? launch
                : (DateTime?)null;

            _specialUnitIds = ParseSpecialUnitIds(configuration["FeedbackAutoImport:BusinessUnitIds"]);
        }

        // Special units come from the FeedbackAutoImport:BusinessUnitIds app
        // setting as a comma-separated list (e.g. "166,299,300"). Unparseable
        // entries are ignored; an unset/empty value yields no special units.
        private static HashSet<uint> ParseSpecialUnitIds(string raw)
        {
            var set = new HashSet<uint>();
            if (string.IsNullOrWhiteSpace(raw)) return set;
            foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries))
                if (uint.TryParse(part.Trim(), out var id)) set.Add(id);
            return set;
        }

        public bool IsExemptFromLicensing(uint unitId) => _specialUnitIds.Contains(unitId);

        public async Task<Dictionary<uint, List<LicensedInterval>>> GetLicensedIntervalsAsync(IEnumerable<long> unitIds)
        {
            var result = new Dictionary<uint, List<LicensedInterval>>();
            var unitSet = (unitIds ?? Enumerable.Empty<long>()).Select(id => (uint)id).Distinct().ToList();
            if (unitSet.Count == 0) return result;

            var now = DateTime.UtcNow;

            // NOTE ON CompanyLicense.IsDel: on this entity IsDel is the EXPIRED-SEAT
            // flag, not a delete marker — the expiry cascade sets it true
            // (SubscriptionLifecycleJob / SubscriptionService.ReconcileLicenseExpiryAsync)
            // and renewal sets it back to false to "revive a previously-expired
            // seat". Filtering history on !l.IsDel therefore ERASED a team's whole
            // licensed past the moment its license lapsed, retroactively turning
            // months of legitimately-licensed activity into "unlicensed". These two
            // history queries deliberately do NOT filter on it: each interval is
            // already bounded by UnassignedAt / RevokedAt and capped by ExpiryDate
            // in AddInterval below, so a lapsed license contributes exactly the
            // window it really covered and nothing after it. The present-tense
            // queries (GetLiveAssignedUnitIdsAsync / GetLiveGrantedUnitIdsAsync) DO
            // still exclude it — there "expired" correctly means "not licensed now".

            // Purchased-assignment intervals (capped by the license's own expiry).
            var assignmentRows = await (
                from a in _context.AssignedCompanyLicenses
                join l in _context.CompanyLicenses on a.CompanyLicenseId equals l.Id
                where unitSet.Contains(a.BusinessUnitId) && !a.IsDel
                select new { a.BusinessUnitId, a.AssignedAt, a.UnassignedAt, l.ExpiryDate }
            ).ToListAsync();

            // Complimentary-grant intervals (capped by the backing license expiry).
            var grantRows = await (
                from g in _context.ComplimentaryLicenseGrants
                join l in _context.CompanyLicenses on g.CompanyLicenseId equals l.Id
                where unitSet.Contains(g.ComplimentaryBusinessUnitId) && !g.IsDel
                select new { UnitId = g.ComplimentaryBusinessUnitId, Start = g.GrantedAt, End = g.RevokedAt, l.ExpiryDate }
            ).ToListAsync();

            var regDates = await _context.BusinessUnits
                .Where(b => unitSet.Contains(b.Id))
                .Select(b => new { b.Id, b.RegistrationDate })
                .ToDictionaryAsync(b => b.Id, b => b.RegistrationDate);

            var rawByUnit = new Dictionary<uint, List<LicensedInterval>>();
            void AddInterval(uint unitId, DateTime start, DateTime? rawEnd, DateTime? expiry)
            {
                var end = rawEnd ?? now;
                if (expiry.HasValue && expiry.Value < end) end = expiry.Value; // license expired mid-interval
                if (end > now) end = now;
                if (end <= start) return;                                       // zero/negative span → skip
                if (!rawByUnit.TryGetValue(unitId, out var list))
                    rawByUnit[unitId] = list = new List<LicensedInterval>();
                list.Add(new LicensedInterval(start, end));
            }

            foreach (var a in assignmentRows) AddInterval(a.BusinessUnitId, a.AssignedAt, a.UnassignedAt, a.ExpiryDate);
            foreach (var g in grantRows) AddInterval(g.UnitId, g.Start, g.End, g.ExpiryDate);

            foreach (var unitId in unitSet)
            {
                // Special units are exempt from licensing: treat the entire span
                // since registration (or the epoch if registration is unknown) as
                // licensed so EarliestIntervalStart never bounds their data out and
                // every record passes IsLicensed — i.e. exactly pre-licensing behavior.
                if (_specialUnitIds.Contains(unitId))
                {
                    var specialStart = regDates.TryGetValue(unitId, out var specialReg) ? specialReg : DateTime.UnixEpoch;
                    result[unitId] = new List<LicensedInterval> { new LicensedInterval(specialStart, now) };
                    continue;
                }

                rawByUnit.TryGetValue(unitId, out var intervals);
                intervals ??= new List<LicensedInterval>();

                bool isLegacy = regDates.TryGetValue(unitId, out var reg)
                                && (!_launchDateUtc.HasValue || reg < _launchDateUtc.Value);
                if (isLegacy && intervals.Count > 0)
                {
                    var earliestStart = intervals.Min(iv => iv.Start);
                    if (reg < earliestStart)
                        intervals.Add(new LicensedInterval(reg, earliestStart)); // bridge pre-feature; merged below
                }

                result[unitId] = MergeIntervals(intervals);
            }

            return result;
        }

        public async Task<HashSet<long>> GetActivelyLicensedUnitIdsAsync(IEnumerable<long> unitIds)
        {
            var result = new HashSet<long>();
            var unitSet = (unitIds ?? Enumerable.Empty<long>()).Select(id => (uint)id).Distinct().ToList();
            if (unitSet.Count == 0) return result;

            foreach (var id in await GetLiveAssignedUnitIdsAsync(unitSet)) result.Add(id);
            foreach (var id in await GetLiveGrantedUnitIdsAsync(unitSet)) result.Add(id);

            // Special units are never license-gated: any that were requested count
            // as "actively licensed" so they are never excluded from the metrics.
            foreach (var id in unitSet)
                if (_specialUnitIds.Contains(id)) result.Add(id);

            return result;
        }

        public async Task<HashSet<long>> GetActivelyComplimentaryUnitIdsAsync(IEnumerable<long> unitIds)
        {
            var result = new HashSet<long>();
            var unitSet = (unitIds ?? Enumerable.Empty<long>()).Select(id => (uint)id).Distinct().ToList();
            if (unitSet.Count == 0) return result;

            var assigned = (await GetLiveAssignedUnitIdsAsync(unitSet)).ToHashSet();

            // A unit with both a live grant and a live purchased assignment is
            // consuming the purchased license, so it is not counted here. Special
            // units read as licensed without holding any grant, so they never
            // appear in the grant query and need no explicit exclusion.
            foreach (var id in await GetLiveGrantedUnitIdsAsync(unitSet))
                if (!assigned.Contains(id)) result.Add(id);

            return result;
        }

        // Units with a live purchased assignment: active, not yet unassigned,
        // effective now, and backing license not expired. Same tables/predicates
        // as GetLicensedIntervalsAsync so both reads agree on "licensed".
        private async Task<List<uint>> GetLiveAssignedUnitIdsAsync(List<uint> unitSet)
        {
            var now = DateTime.UtcNow;
            return await (
                from a in _context.AssignedCompanyLicenses
                join l in _context.CompanyLicenses on a.CompanyLicenseId equals l.Id
                where unitSet.Contains(a.BusinessUnitId)
                      && !a.IsDel && !l.IsDel
                      && a.IsActive && a.UnassignedAt == null
                      && a.AssignedAt <= now
                      && (l.ExpiryDate == null || l.ExpiryDate > now)
                select a.BusinessUnitId
            ).Distinct().ToListAsync();
        }

        // Units with a live complimentary grant: active, not revoked, effective
        // now, and backing license not expired.
        private async Task<List<uint>> GetLiveGrantedUnitIdsAsync(List<uint> unitSet)
        {
            var now = DateTime.UtcNow;
            return await (
                from g in _context.ComplimentaryLicenseGrants
                join l in _context.CompanyLicenses on g.CompanyLicenseId equals l.Id
                where unitSet.Contains(g.ComplimentaryBusinessUnitId)
                      && !g.IsDel && !l.IsDel
                      && g.IsActive && g.RevokedAt == null
                      && g.GrantedAt <= now
                      && (l.ExpiryDate == null || l.ExpiryDate > now)
                select g.ComplimentaryBusinessUnitId
            ).Distinct().ToListAsync();
        }

        public bool IsLicensed(Dictionary<uint, List<LicensedInterval>> intervals, uint unitId, DateTime timestampUtc)
        {
            // Special units are exempt from licensing — always in-license.
            if (_specialUnitIds.Contains(unitId)) return true;

            return intervals.TryGetValue(unitId, out var list)
                && list.Any(iv => timestampUtc >= iv.Start && timestampUtc <= iv.End);
        }

        public DateTime EarliestIntervalStart(Dictionary<uint, List<LicensedInterval>> intervals)
        {
            return intervals.Values
                .SelectMany(v => v)
                .Select(iv => iv.Start)
                .DefaultIfEmpty(DateTime.UtcNow)
                .Min();
        }

        // Sorts by start and merges overlapping/adjacent intervals.
        private static List<LicensedInterval> MergeIntervals(List<LicensedInterval> intervals)
        {
            if (intervals.Count <= 1) return intervals;
            var sorted = intervals.OrderBy(i => i.Start).ToList();
            var merged = new List<LicensedInterval> { sorted[0] };
            foreach (var cur in sorted.Skip(1))
            {
                var last = merged[merged.Count - 1];
                if (cur.Start <= last.End)
                    merged[merged.Count - 1] = new LicensedInterval(last.Start, cur.End > last.End ? cur.End : last.End);
                else
                    merged.Add(cur);
            }
            return merged;
        }
    }
}
