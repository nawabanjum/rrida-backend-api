using CloverBackoffice.Components.Enums;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Implementation;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Linq;

namespace CloverBackoffice.Services.Implementation
{
    public class AggregateDashboardService : IAggregateDashboardService
    {
        private readonly IAggregateDashboardRepository _repository;
        private readonly ApplicationDbContext _context;
        private readonly ILicensedPeriodService _licensedPeriod;
        private readonly IConfiguration _configuration;
        private readonly IUnitAccessService _unitAccess;

        public AggregateDashboardService(IAggregateDashboardRepository repository, ApplicationDbContext context, ILicensedPeriodService licensedPeriod, IConfiguration configuration, IUnitAccessService unitAccess)
        {
            _repository = repository;
            _context = context;
            _licensedPeriod = licensedPeriod;
            _configuration = configuration;
            _unitAccess = unitAccess;
        }

        // Get all UnitIds starting from the top-level parent or the given child
        public async Task<List<long>> GetBusinessUnitHierarchyAsync(long unitId)
        {
            var unit = await _repository.GetBusinessUnitAsync(unitId);
            if (unit == null) return new List<long>();

            long startUnitId;

            if (unit.ParentId.HasValue)
            {
                // It's a child: start from itself only (not parent)
                startUnitId = unit.Id;
            }
            else
            {
                // It's a parent: start from itself (root) to get all children
                startUnitId = unit.Id;
            }

            // Get full hierarchy from the startUnitId
            var allUnitIds = await _repository.GetBusinessUnitHierarchyRecursiveAsync(startUnitId);
            return allUnitIds;
        }

        // Get UnitIds for analysis with recursive hierarchy logic
        // Always returns the selected unit plus all its descendants (children, sub-children, etc.)
        public async Task<List<long>> GetBusinessUnitIdsForAnalysisAsync(long unitId, DateTimeOffset fromDate, DateTimeOffset toDate)
        {
            var unit = await _repository.GetBusinessUnitAsync(unitId);
            if (unit == null) return new List<long>();

            // Always get the unit and all its descendants recursively
            var allDescendants = await _repository.GetBusinessUnitHierarchyRecursiveAsync(unitId);

            return allDescendants;
        }


        public async Task<DashboardSummaryDto> AnalyzeFeedbackRunTimeAsync(List<long> unitIds, DateTimeOffset fromDate, DateTimeOffset toDate, bool ignoreLicenseFilter = false)
        {
            // Calculate the duration and extend date range to include previous period
            var duration = toDate - fromDate;
            var extendedFromDate = fromDate - duration; // Double the range to include previous period

            var feedbackRows = await _repository.GetDailyFeedbackReportAsync(unitIds, extendedFromDate, toDate);

            // Exclude feedback generated while a team had no active license, so the
            // aggregated stats reflect only licensed-period activity (same rule as
            // the Team Structure metrics).
            //
            // CloverERA Admins are exempt: they see the overall data across the
            // whole selected range regardless of license state. Every other user
            // keeps the licensed-period-only behaviour.
            if (!ignoreLicenseFilter)
            {
                var intervals = await _licensedPeriod.GetLicensedIntervalsAsync(unitIds);
                feedbackRows = feedbackRows
                    .Where(r => r.BusinessUnitId.HasValue
                                && _licensedPeriod.IsLicensed(intervals, r.BusinessUnitId.Value, r.SubmissionDateTime.UtcDateTime))
                    .ToList();
            }

            // Use the first unitId as requestedUnitId for display purposes, but aggregate across all unitIds
            var requestedUnitId = unitIds.First();
            var dashboard = await _repository.AnalyzeAndSaveFeedbackInternal(feedbackRows, unitIds, requestedUnitId, fromDate.UtcDateTime, toDate.UtcDateTime);
            return dashboard;
        }

        // Effective Team-Health license breakdown for the aggregate stats endpoint:
        // splits the requested range into contiguous segments, each flagged with
        // its effective state — true (licensed), false (unlicensed), or null
        // (TRANSITION: a license assign/unassign happened within the grace period,
        // so the team is not yet Team-Health-active and the frontend shows a lock
        // screen). Purely informational and appended to the summary response — it
        // does NOT gate or alter the health data. The licensed/unlicensed base
        // reuses ILicensedPeriodService so the licensing rules (purchased +
        // complimentary grants, expiry capping, legacy grandfathering, special
        // units) stay identical to the read-side metric filtering; the transition
        // overlay is scoped to this endpoint only.
        public async Task<List<LicensePeriodSegmentDto>> GetLicensePeriodsAsync(long unitId, DateTimeOffset fromDate, DateTimeOffset toDate)
        {
            var fromUtc = fromDate.UtcDateTime;
            var toUtc = toDate.UtcDateTime;
            var now = DateTime.UtcNow;

            // Licensed intervals cap their end at "now" (a live license has no
            // future end yet), so a range extending past now can only be
            // evaluated up to now. Clamp the window there.
            var effectiveTo = toUtc < now ? toUtc : now;

            // A team cannot be "unlicensed" before it existed. Without this the
            // window (always now - days) reports the whole pre-registration stretch
            // as false — e.g. 90 days of "unlicensed" for a company created today.
            // Clamp the start to the team's registration instead.
            var registeredAt = await _context.BusinessUnits
                .Where(b => b.Id == (uint)unitId)
                .Select(b => (DateTime?)b.RegistrationDate)
                .FirstOrDefaultAsync();
            var effectiveFrom = registeredAt.HasValue && registeredAt.Value > fromUtc
                ? registeredAt.Value
                : fromUtc;

            // Whole range is in the future, or entirely before the team existed.
            if (effectiveTo <= effectiveFrom)
                return new List<LicensePeriodSegmentDto>();

            var intervals = await _licensedPeriod.GetLicensedIntervalsAsync(new[] { unitId });
            intervals.TryGetValue((uint)unitId, out var unitIntervals);
            unitIntervals ??= new List<LicensedInterval>();

            // Transition windows [event, event + grace] around each assign/unassign
            // (and complimentary grant/revoke) event for this unit.
            var grace = GetTransitionGracePeriod();
            var transitions = await GetTransitionWindowsAsync(unitId, effectiveFrom, effectiveTo, grace, registeredAt);

            return BuildEffectiveSegments(unitIntervals, transitions, effectiveFrom, effectiveTo);
        }

        // Grace period during which a freshly assigned/unassigned license is still
        // "settling" and the team is NOT considered Team-Health-active. Configurable
        // via Licensing:TeamHealthTransitionGraceHours (expected 1–2 hours); defaults
        // to 2 hours when unset/unparseable.
        private TimeSpan GetTransitionGracePeriod()
        {
            var raw = _configuration["Licensing:TeamHealthTransitionGraceHours"];
            return double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var hours) && hours > 0
                ? TimeSpan.FromHours(hours)
                : TimeSpan.FromHours(2);
        }

        // Window after team registration in which the FIRST license assignment is
        // treated as initial provisioning rather than a license transition. Company
        // creation registers the unit and mints/assigns its trial licence a beat
        // later (observed: ~2 seconds), so anything inside this window is the same
        // logical event.
        private static readonly TimeSpan InitialProvisioningWindow = TimeSpan.FromMinutes(5);

        // Collects transition windows [event, event + grace] for the unit from the
        // raw assign/unassign timestamps (purchased assignments) and grant/revoke
        // timestamps (complimentary grants). Only events whose forward grace window
        // can overlap [from, to] are kept. Read-only — does not touch the
        // assign/unassign flow.
        //
        // The grace period exists so a team whose licence state CHANGES settles
        // before Team Health reactivates. A brand-new team has no prior state to
        // settle from, so the licence it is provisioned with at registration is not
        // a transition — counting it as one left every new company reading "locked"
        // (null) for the first two hours of its life. That first event is dropped
        // when it lands inside InitialProvisioningWindow of registrationDate; a team
        // that genuinely gets its first licence later (unlicensed -> licensed) is a
        // real transition and keeps its grace window.
        private async Task<List<(DateTime Start, DateTime End)>> GetTransitionWindowsAsync(
            long unitId, DateTime from, DateTime to, TimeSpan grace, DateTime? registrationDate)
        {
            var uid = (uint)unitId;
            var lowerBound = from - grace; // e >= from - grace  =>  [e, e+grace] can reach `from`

            var assignments = await _context.AssignedCompanyLicenses
                .Where(a => a.BusinessUnitId == uid && !a.IsDel)
                .Select(a => new { a.AssignedAt, a.UnassignedAt })
                .ToListAsync();

            var grants = await _context.ComplimentaryLicenseGrants
                .Where(g => g.ComplimentaryBusinessUnitId == uid && !g.IsDel)
                .Select(g => new { g.GrantedAt, g.RevokedAt })
                .ToListAsync();

            // Earliest moment this unit ever became licensed, across both sources.
            DateTime? firstCoverageAt = assignments.Select(a => a.AssignedAt)
                .Concat(grants.Select(g => g.GrantedAt))
                .DefaultIfEmpty()
                .Min();
            if (firstCoverageAt == default(DateTime)) firstCoverageAt = null;

            var provisioningAt = registrationDate.HasValue
                                 && firstCoverageAt.HasValue
                                 && firstCoverageAt.Value <= registrationDate.Value + InitialProvisioningWindow
                ? firstCoverageAt
                : null;

            var events = new List<DateTime>();
            foreach (var a in assignments)
            {
                events.Add(a.AssignedAt);
                if (a.UnassignedAt.HasValue) events.Add(a.UnassignedAt.Value);
            }
            foreach (var g in grants)
            {
                events.Add(g.GrantedAt);
                if (g.RevokedAt.HasValue) events.Add(g.RevokedAt.Value);
            }

            return events
                .Where(e => e >= lowerBound && e <= to)
                .Where(e => !provisioningAt.HasValue || e != provisioningAt.Value)
                .Select(e => (Start: e, End: e + grace))
                .ToList();
        }

        // Shortest segment worth reporting. Below this a boundary is noise (clock
        // skew between a team's registration and its licence rows, back-to-back
        // unassign/assign during a licence swap) rather than a real period.
        private static readonly TimeSpan MinSegmentDuration = TimeSpan.FromMinutes(1);

        // Sweeps [from, to] across every interval/transition boundary and emits one
        // segment per gap, choosing the effective state at the segment midpoint:
        //   transition window covers it -> null (overrides everything)
        //   else inside a licensed interval -> true
        //   else -> false
        // Adjacent segments with the same state are merged.
        private static List<LicensePeriodSegmentDto> BuildEffectiveSegments(
            List<LicensedInterval> intervals,
            List<(DateTime Start, DateTime End)> transitions,
            DateTime from,
            DateTime to)
        {
            var points = new SortedSet<DateTime>();
            void AddPoint(DateTime p) { if (p > from && p < to) points.Add(p); }

            foreach (var iv in intervals) { AddPoint(iv.Start); AddPoint(iv.End); }
            foreach (var w in transitions) { AddPoint(w.Start); AddPoint(w.End); }

            // Boundaries closer together than MinSegmentDuration would emit slivers
            // that mean nothing to a consumer — e.g. the ~2-second "licensed" gap
            // between a team's RegistrationDate and the moment its licence row is
            // written at signup. Drop such a boundary instead of splitting on it, so
            // the surrounding segment simply absorbs the sliver. Dropping boundaries
            // (rather than emitted segments) keeps the timeline gapless by
            // construction; the tail check stops a short final segment.
            var ordered = new List<DateTime> { from };
            foreach (var p in points)
                if (p - ordered[ordered.Count - 1] >= MinSegmentDuration) ordered.Add(p);
            if (ordered.Count > 1 && to - ordered[ordered.Count - 1] < MinSegmentDuration)
                ordered.RemoveAt(ordered.Count - 1);
            ordered.Add(to);
            var segments = new List<LicensePeriodSegmentDto>();

            for (int i = 0; i < ordered.Count - 1; i++)
            {
                var segStart = ordered[i];
                var segEnd = ordered[i + 1];
                if (segEnd <= segStart) continue;

                var mid = segStart + TimeSpan.FromTicks((segEnd - segStart).Ticks / 2);

                bool? state;
                if (transitions.Any(w => mid >= w.Start && mid < w.End))
                    state = null; // transition overrides licensed/unlicensed
                else
                    state = intervals.Any(iv => mid >= iv.Start && mid <= iv.End);

                // Merge with the previous segment when the state matches.
                if (segments.Count > 0 && NullableBoolEquals(segments[segments.Count - 1].IsLicensed, state))
                    segments[segments.Count - 1].ToDate = new DateTimeOffset(DateTime.SpecifyKind(segEnd, DateTimeKind.Utc));
                else
                    segments.Add(NewSegment(segStart, segEnd, state));
            }

            return segments;
        }

        private static bool NullableBoolEquals(bool? a, bool? b) => a == b;

        private static LicensePeriodSegmentDto NewSegment(DateTime from, DateTime to, bool? licensed) =>
            new LicensePeriodSegmentDto
            {
                FromDate = new DateTimeOffset(DateTime.SpecifyKind(from, DateTimeKind.Utc)),
                ToDate = new DateTimeOffset(DateTime.SpecifyKind(to, DateTimeKind.Utc)),
                IsLicensed = licensed
            };

        // Role-based unit scoping now lives in the shared UnitAccessService; kept as a thin
        // wrapper so the existing call sites in this class stay unchanged.
        private Task<List<long>> GetAccessibleUnitIds(User user)
            => _unitAccess.GetAccessibleUnitIdsAsync(user);

        public async Task<CategoryDetailDto> GetCategoryDetailAsync(List<long> unitIds, DateTimeOffset fromDate, DateTimeOffset toDate, int cloverCategory)
        {
            // Extend date range to include previous period (same pattern as AnalyzeFeedbackRunTimeAsync)
            var duration = toDate - fromDate;
            var extendedFromDate = fromDate - duration;

            var feedbackRows = await _repository.GetDailyFeedbackReportAsync(unitIds, extendedFromDate, toDate);

            return _repository.GetCategoryDetail(feedbackRows, unitIds, cloverCategory, fromDate.UtcDateTime, toDate.UtcDateTime);
        }

        public async Task<DashboardResponseDto> GetDashboardData(int userId, int? profileId = null)
        {
            try
            {
                // 1) Get user
                var user = await _repository.GetUserByIdAsync(userId);
                if (user == null)
                    throw new UnauthorizedAccessException();

                // 2) Find which UnitIds user can access
                var accessibleUnitIds = await GetAccessibleUnitIds(user);

                // 3) If profileId is provided, filter units to only that company
                if (profileId.HasValue)
                {
                    var companyUnitIds = await _context.BusinessUnits
                        .Where(bu => bu.Profile.Id == profileId.Value && bu.IsDel != true)
                        .Select(bu => (long)bu.Id)
                        .ToListAsync();

                    accessibleUnitIds = accessibleUnitIds
                        .Where(id => companyUnitIds.Contains(id))
                        .ToList();
                }

                // 4) Restrict every dashboard statistic to units that currently
                //    hold an ACTIVE assigned license (purchased assignment or
                //    complimentary grant). Unlicensed units are excluded from all
                //    counts, averages and notification-banner metrics below.
                var activelyLicensed = await _licensedPeriod.GetActivelyLicensedUnitIdsAsync(accessibleUnitIds);
                var licensedUnitIds = accessibleUnitIds.Where(id => activelyLicensed.Contains(id)).ToList();

                // 4b) Of those active licenses, which are complimentary rather than
                //     purchased. Reported alongside the active count, not added to it.
                var activelyComplimentary = await _licensedPeriod.GetActivelyComplimentaryUnitIdsAsync(licensedUnitIds);
                var complimentaryUnitIds = licensedUnitIds.Where(id => activelyComplimentary.Contains(id)).ToList();

                // 5) Pass licensed UnitIds to repository for the statistical
                //    sections. ActionHubActivities is the audit-based activity feed
                //    (access-scoped, not a statistic) so it keeps the full
                //    accessible set.
                var dto = new DashboardResponseDto
                {
                    // TotalTeams = overall (accessible) teams; ActiveLicensedTeams =
                    // the active-license subset; ComplimentaryLicenseCount = the
                    // complimentary part of that subset. Other sections stay licensed-only.
                    TotalTeamsSection = await _repository.GetTeamsSection(accessibleUnitIds, licensedUnitIds, complimentaryUnitIds),
                    AvgResponsesPerTeamSection = await _repository.GetResponseRateSection(licensedUnitIds),
                    NeedsAttentionSection = await _repository.GetNeedsAttentionSection(licensedUnitIds),
                    AvgTrendScoreSection = await _repository.GetEngagementSection(licensedUnitIds),
                    ActionHubActivities = await _repository.GetActionHubActivities(user, accessibleUnitIds)
                };

                return dto;
            }
            catch
            {
                throw;
            }
        }

    }
}
