using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Implementation;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace CloverBackoffice.Services.Implementation
{
    public partial class SubscriptionService : ISubscriptionService
    {
        private readonly ICompanySubscriptionRepository _subscriptionRepo;
        private readonly IPlanRepository _planRepo;
        private readonly IStripeService _stripeService;
        private readonly IPromoCodeService _promoCodeService;
        private readonly IPromoCodeRepository _promoCodeRepo;
        private readonly IPaymentRepository _paymentRepo;
        private readonly ICompanyLicenseRepository _licenseRepo;
        private readonly ApplicationDbContext _db;
        private readonly StripeSettings _stripeSettings;
        private readonly ILogger<SubscriptionService> _logger;
        private readonly ILegacyTrialBackfillService _legacyBackfill;

        private const int MaxPeoplePerTeam = 20;
        // Trial window. Matches the 30 days used by CreateTrial, the legacy
        // backfill and AutoTrialDays — every trial path grants the same length.
        private const int TrialDays = 30;

        // Expiry fallback for legacy rows with no CurrentPeriodEnd. Kept at the
        // old 21 days so moving the trial to 30 days never changes an existing
        // record's computed expiry.
        private const int LegacyFallbackTrialDays = 21;

        // Auto-trial window granted when the Status API bootstraps a company that
        // has no subscription at all (created via Create-Company, never trialed).
        private const int AutoTrialDays = 30;

        public SubscriptionService(
            ICompanySubscriptionRepository subscriptionRepo,
            IPlanRepository planRepo,
            IStripeService stripeService,
            IPromoCodeService promoCodeService,
            IPromoCodeRepository promoCodeRepo,
            IPaymentRepository paymentRepo,
            ICompanyLicenseRepository licenseRepo,
            ApplicationDbContext db,
            IOptions<StripeSettings> stripeSettings,
            ILogger<SubscriptionService> logger,
            ILegacyTrialBackfillService legacyBackfill)
        {
            _subscriptionRepo = subscriptionRepo;
            _planRepo = planRepo;
            _stripeService = stripeService;
            _promoCodeService = promoCodeService;
            _promoCodeRepo = promoCodeRepo;
            _paymentRepo = paymentRepo;
            _licenseRepo = licenseRepo;
            _db = db;
            _stripeSettings = stripeSettings.Value;
            _logger = logger;
            _legacyBackfill = legacyBackfill;
        }

        public async Task<CurrentSubscriptionDto?> GetCurrentSubscriptionAsync(uint profileId)
        {
            var sub = await _subscriptionRepo.GetActiveByProfileIdAsync(profileId);
            if (sub == null) return null;
            await ApplyExpiryIfDueAsync(sub);
            return MapToDto(sub);
        }

        // Raw period-end. CurrentPeriodEnd is the single source of truth for
        // expiry — both trial and paid subscriptions populate it (StartTrial
        // seeds it alongside TrialEndDate; Stripe webhooks overwrite it on
        // renewal). TrialEndDate is kept on the row for audit/reporting only;
        // it no longer drives /status, IsExpired, or the renewal URL.
        // Fallback: if CurrentPeriodEnd is unset (legacy rows), derive from
        // the start date + TrialDays so old data still computes a sane expiry.
        private static DateTime ComputeRawExpiry(CompanySubscription sub)
        {
            if (sub.CurrentPeriodEnd != default)
                return sub.CurrentPeriodEnd;

            var start = sub.CurrentPeriodStart != default ? sub.CurrentPeriodStart
                       : (sub.TrialStartDate ?? sub.CreatedAt);
            return start.AddDays(LegacyFallbackTrialDays);
        }

        // Effective expiry = raw + grace. Grace resolution prefers the unit-
        // specific value on AssignedCompanyLicense; falls back to the company-
        // wide CompanySubscription.GracePeriod when the assignment hasn't
        // overridden it. Admin can edit either column and the next /status
        // call reflects it.
        private static DateTime ComputeEffectiveExpiry(CompanySubscription sub, int? gracePeriodDays)
        {
            var raw = ComputeRawExpiry(sub);
            var grace = ResolveGrace(sub, gracePeriodDays);
            return grace > 0 ? raw.AddDays(grace) : raw;
        }

        // Grace precedence: CompanySubscriptions.GracePeriod (company-wide,
        // admin-editable in DB) wins. Falls back to per-assignment grace only
        // when the sub-level value is NULL. Matches SubscriptionLifecycleJob,
        // which reads sub.GracePeriod directly.
        private static int ResolveGrace(CompanySubscription sub, int? assignmentGrace)
            => sub.GracePeriod ?? assignmentGrace ?? 0;

        // Individual license expiry = anchor + one billing cycle. Monthly →
        // +1 month, Annual → +1 year. Each license carries its OWN date so it
        // lives independently of the subscription period.
        private static DateTime ComputeLicenseExpiry(BillingCycle cycle, DateTime from)
            => cycle == BillingCycle.Annual ? from.AddYears(1) : from.AddMonths(1);

        // Reads the "billingCycle" key an addLicenses checkout session carries
        // (stamped in BuildAddLicensesCheckoutUrlAsync as the integer enum value).
        // Returns null when the key is absent (legacy sessions) or not a defined
        // BillingCycle, so callers can fall back to the subscription's own cycle.
        private static BillingCycle? ParseBillingCycleMetadata(IDictionary<string, string>? metadata)
        {
            if (metadata != null &&
                metadata.TryGetValue("billingCycle", out var raw) &&
                int.TryParse(raw, out var val) &&
                Enum.IsDefined(typeof(BillingCycle), val))
                return (BillingCycle)val;
            return null;
        }

        // Auto-heal for the "admin extended CurrentPeriodEnd directly in SQL"
        // case. The expiry cascade earlier flipped every CompanyLicense to
        // IsDel=true and every AssignedCompanyLicense to IsActive=false; a
        // direct UPDATE of CompanySubscriptions.CurrentPeriodEnd doesn't
        // touch those rows, so /status's active-only query returns empty
        // until the hourly lifecycle tick reactivates them.
        //
        // This finds any sub for the company that is Status=Expired but
        // already has a future CurrentPeriodEnd (the renewed-but-not-yet-
        // reactivated state) and restores it in-line. Healthy subs are
        // untouched — the query returns zero rows and no writes happen.
        private async Task AutoHealRenewedSubsAsync(uint profileId)
        {
            var now = DateTime.UtcNow;
            var stale = await _db.CompanySubscriptions
                .Where(s => s.ProfileId == profileId
                         && !s.IsDel
                         && s.Status == SubscriptionStatus.Expired
                         && s.CurrentPeriodEnd > now)
                .ToListAsync();

            foreach (var s in stale)
            {
                s.Status = SubscriptionStatus.Active;
                s.LastWarningDaysSent = null;
                s.UpdatedAt = now;
                await _db.SaveChangesAsync();
                // Licenses are revived only if their OWN ExpiryDate is in the
                // future — extending CurrentPeriodEnd alone no longer forces
                // licenses back to life.
                await ReconcileLicenseExpiryAsync(s.Id, now);
            }
        }

        // Reconcile on every read. Two INDEPENDENT concerns:
        //   1. Subscription Status (billing/UI only). Period passed → Status=
        //      Expired; period in the future but row still Expired → Status=
        //      Active. This NEVER touches licenses — a lapsed subscription must
        //      not disable any license.
        //   2. License lifecycle. Each license is expired/revived by its OWN
        //      ExpiryDate via ReconcileLicenseExpiryAsync, regardless of the
        //      subscription's status.
        // Canceled subs are terminal — left alone.
        private async Task ApplyExpiryIfDueAsync(CompanySubscription sub)
        {
            if (sub.Status == SubscriptionStatus.Canceled)
                return;

            var now = DateTime.UtcNow;
            var rawExpiry = ComputeRawExpiry(sub);
            bool periodPassed = now >= rawExpiry;

            if (periodPassed && sub.Status != SubscriptionStatus.Expired)
            {
                sub.Status = SubscriptionStatus.Expired;
                sub.UpdatedAt = now;
                await _db.SaveChangesAsync();
            }
            else if (!periodPassed && sub.Status == SubscriptionStatus.Expired)
            {
                sub.Status = SubscriptionStatus.Active;
                sub.LastWarningDaysSent = null;
                sub.UpdatedAt = now;
                await _db.SaveChangesAsync();
            }

            await ReconcileLicenseExpiryAsync(sub.Id, now);
        }

        // Per-license expiry reconciliation, independent of the subscription's
        // status. A license expires ONLY when its individual ExpiryDate is
        // reached; once expired it is soft-deleted (IsDel=true) and its active
        // assignment is closed. If a license's ExpiryDate is later moved into
        // the future (renewal / admin extension) it is restored and its last
        // assignment reopened. Licenses with no ExpiryDate never auto-expire.
        // Wrapped in a transaction so a mid-write crash never leaves licenses
        // and assignments out of sync.
        //
        // This path deliberately sends NO notification — it runs inside request
        // handling and would fire mail from arbitrary GETs. SubscriptionLifecycleJob
        // owns every expiry/reactivation notification and picks these rows up on
        // its next tick via CompanyLicense.ExpiryNotifiedAt/ReactivationNotifiedAt,
        // which is why it no longer matters who flips IsDel first.
        //
        // The expiry boundary MUST match the job's ComputeLicenseExpiry
        // (ExpiryDate + subscription grace). Using the raw ExpiryDate here made
        // this path kill licenses days before the job considered them due, so
        // with GracePeriod > 0 access was cut off during the grace window.
        private async Task ReconcileLicenseExpiryAsync(int subscriptionId, DateTime now)
        {
            var licenses = await _db.CompanyLicenses
                .Where(l => l.CompanySubscriptionId == subscriptionId && l.ExpiryDate != null
                            // A trial ended by a move to a purchased licence stays ended.
                            && l.TerminatedAt == null)
                .ToListAsync();
            if (licenses.Count == 0) return;

            var grace = await _db.CompanySubscriptions
                .Where(s => s.Id == subscriptionId)
                .Select(s => s.GracePeriod)
                .FirstOrDefaultAsync() ?? 0;

            DateTime EffectiveExpiry(CompanyLicense l) =>
                grace > 0 ? l.ExpiryDate!.Value.AddDays(grace) : l.ExpiryDate!.Value;

            var toExpire = licenses.Where(l => !l.IsDel && EffectiveExpiry(l) <= now).ToList();
            var toRevive = licenses.Where(l => l.IsDel && EffectiveExpiry(l) > now).ToList();
            if (toExpire.Count == 0 && toRevive.Count == 0) return;

            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                if (toExpire.Count > 0)
                {
                    foreach (var lic in toExpire) { lic.IsDel = true; lic.UpdatedAt = now; }

                    var ids = toExpire.Select(l => l.Id).ToList();
                    var active = await _db.AssignedCompanyLicenses
                        .Where(a => ids.Contains(a.CompanyLicenseId) && a.IsActive && !a.IsDel)
                        .ToListAsync();
                    foreach (var a in active)
                    {
                        a.IsActive = false;
                        a.UnassignedAt = now;
                        a.UpdatedAt = now;
                    }
                }

                if (toRevive.Count > 0)
                {
                    foreach (var lic in toRevive) { lic.IsDel = false; lic.UpdatedAt = now; }

                    var ids = toRevive.Select(l => l.Id).ToList();
                    var last = await _db.AssignedCompanyLicenses
                        .Where(a => ids.Contains(a.CompanyLicenseId) && !a.IsDel)
                        .GroupBy(a => a.CompanyLicenseId)
                        .Select(g => g.OrderByDescending(a => a.AssignedAt).First())
                        .ToListAsync();
                    foreach (var a in last)
                    {
                        a.IsActive = true;
                        a.UnassignedAt = null;
                        a.UpdatedAt = now;
                    }
                }

                await _db.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        // On a renewal / trial→paid conversion / admin extension the subscription
        // period moves forward; push every PAID license on the sub out to the new
        // period end so seats don't lapse. Only ever extends FORWARD (never
        // shortens a license that already runs longer). Soft-deleted (expired)
        // licenses are extended too — the following ReconcileLicenseExpiryAsync
        // call revives any whose new date is back in the future.
        //
        // Trial licenses are deliberately EXCLUDED: a trial seat runs its own
        // window (registration + TrialDays) and must expire on that date even
        // when the company pays mid-trial. Buying a subscription mints separate
        // paid seats rather than extending the trial ones — see
        // CompanyLicense.ExpiryDate, which documents each license as owning a
        // lifecycle independent of the parent subscription's CurrentPeriodEnd.
        //
        // Licences cancelled at period end (CancelLicenseAsync) are EXCLUDED too:
        // their seat is no longer billed, so they lapse on their own ExpiryDate.
        private async Task ExtendLicensesToAsync(int subscriptionId, DateTime newExpiry, DateTime now)
        {
            var licenses = await _db.CompanyLicenses
                .Where(l => l.CompanySubscriptionId == subscriptionId
                         && l.LicenseType != "Trial"
                         && !l.CancelAtPeriodEnd)
                .ToListAsync();

            bool changed = false;
            foreach (var lic in licenses)
            {
                if (!lic.ExpiryDate.HasValue || lic.ExpiryDate.Value < newExpiry)
                {
                    lic.ExpiryDate = newExpiry;
                    lic.UpdatedAt = now;
                    changed = true;
                }
            }
            if (changed) await _db.SaveChangesAsync();
        }

        public async Task<SubscriptionStatusDto> GetSubscriptionStatusAsync(uint profileId, uint? unitId)
        {
            if (profileId == 0)
                throw new InvalidOperationException("ProfileId is required.");

            var company = await _subscriptionRepo.GetCompanyByIdAsync(profileId)
                ?? throw new InvalidOperationException("Invalid ProfileId. Profile does not exist.");

            if (!unitId.HasValue || unitId.Value == 0)
                throw new InvalidOperationException("Invalid UnitId.");

            var unit = await _subscriptionRepo.GetBusinessUnitForCompanyAsync(unitId.Value, profileId)
                ?? throw new InvalidOperationException("Invalid UnitId. Unit does not exist for the provided Company.");

            // Lazy bootstrap: a company with NO subscription (created via
            // Create-Company, never trialed) is placed on a 30-day trial here —
            // licenses are minted + assigned to every team — so the rest of this
            // method resolves a live trial instead of throwing "No subscription".
            // No-op for companies that already have any subscription.
            await _legacyBackfill.EnsureTrialForCompanyAsync(profileId, AutoTrialDays);

            await AutoHealRenewedSubsAsync(profileId);

            // Resolve the sub via the unit's active license assignment. If
            // that's empty — which happens after an expiry cascade flips
            // every assignment IsActive=false and every license IsDel=true —
            // fall back to the most-recent assignment regardless of state so
            // we can still return a status DTO with IsExpired=true and a
            // freshly-built RenewalUrl. Throwing "No subscription found"
            // strands users on an expired sub with no way to renew.
            var assignment = await _subscriptionRepo.GetActiveLicenseAssignmentForUnitAsync(unitId.Value)
                          ?? await _subscriptionRepo.GetLatestLicenseAssignmentForUnitAsync(unitId.Value);

            // Complimentary-only unit: no purchased assignment of its own — resolve
            // status from the backing license it rides.
            if (assignment == null)
            {
                var compStatus = await BuildComplimentaryStatusAsync(company, unit);
                if (compStatus == null)
                    throw new InvalidOperationException("No subscription found for this unit.");
                return compStatus;
            }

            var sub = assignment.CompanyLicense?.CompanySubscription
                ?? throw new InvalidOperationException("No subscription found for this unit.");

            await ApplyExpiryIfDueAsync(sub);

            // Re-resolve the unit's assignment AFTER reconciliation so the DTO
            // reflects any expiry that just fired: once a license expires its
            // active assignment is closed (IsActive=false, UnassignedAt set), so
            // the active query goes empty and we fall back to the latest
            // (now-closed) assignment — which carries the fresh UnassignedAt.
            // The repo reads are AsNoTracking, so the pre-reconcile copy would
            // otherwise still show UnassignedAt=null.
            var current = await _subscriptionRepo.GetActiveLicenseAssignmentForUnitAsync(unitId.Value)
                       ?? await _subscriptionRepo.GetLatestLicenseAssignmentForUnitAsync(unitId.Value)
                       ?? assignment;

            var statusDto = BuildStatusDto(sub, company, unit, current.GracePeriod, current.CompanyLicense?.ExpiryDate, current.UnassignedAt);
            if (statusDto.IsExpired)
                statusDto.RenewalUrl = await BuildRenewalUrlAsync(sub, company);
            return statusDto;
        }

        public async Task<SubscriptionDetailDto> GetSubscriptionDetailAsync(uint profileId, uint unitId)
        {
            if (profileId == 0)
                throw new InvalidOperationException("ProfileId is required.");
            if (unitId == 0)
                throw new InvalidOperationException("UnitId is required.");

            var company = await _subscriptionRepo.GetCompanyByIdAsync(profileId)
                ?? throw new InvalidOperationException("Invalid ProfileId. Profile does not exist.");

            var unit = await _subscriptionRepo.GetBusinessUnitForCompanyAsync(unitId, profileId)
                ?? throw new InvalidOperationException("Invalid UnitId. Unit does not exist for the provided Company.");

            // Pull ONLY the licenses currently pinned to this unit. The
            // company-wide license pool is intentionally not loaded — a
            // single-unit query must not bleed company-level data.
            var unitLicenses = await _subscriptionRepo.GetActiveLicensesForUnitAsync(unitId);

            // No purchased license of its own — fall back to a complimentary
            // grant (a same-manager team riding another team's license).
            if (unitLicenses.Count == 0)
            {
                var compDto = await BuildComplimentaryUnitDetailAsync(unit);
                if (compDto == null)
                    throw new InvalidOperationException("No subscription found for this unit.");
                compDto.ProfileId = company.Id;
                compDto.CompanyName = company.ProfileName;
                await StampLicenseManagersAsync(compDto.Licenses);
                return compDto;
            }

            var sub = unitLicenses.First().CompanySubscription;
            if (sub != null) await ApplyExpiryIfDueAsync(sub);

            // Grace lives on the active assignment for this unit, not the sub
            // (which is shared across the company's licenses).
            var firstActive = unitLicenses
                .SelectMany(l => l.Assignments ?? Enumerable.Empty<AssignedCompanyLicense>())
                .FirstOrDefault(a => a.IsActive && !a.IsDel && a.BusinessUnitId == unitId);

            var dto = BuildUnitDetail(unit, sub, unitLicenses, firstActive?.GracePeriod);
            dto.ProfileId = company.Id;
            dto.CompanyName = company.ProfileName;
            await StampLicenseManagersAsync(dto.Licenses);
            return dto;
        }

        public Task<CompanySubscriptionDetailDto> GetSubscriptionDetailsForAllUnitsAsync(uint profileId)
        {
            return GetSubscriptionDetailsForAllUnitsAsync(profileId, requireUnits: true);
        }

        // requireUnits = false lets a profile with no teams come back with an
        // empty Units list (subscription and license pool still filled) instead
        // of the "No business units found" error. Used by the trial extension,
        // which extends a profile whether or not it has teams yet.
        private async Task<CompanySubscriptionDetailDto> GetSubscriptionDetailsForAllUnitsAsync(uint profileId, bool requireUnits)
        {
            if (profileId == 0)
                throw new InvalidOperationException("ProfileId is required.");

            var company = await _subscriptionRepo.GetCompanyByIdAsync(profileId)
                ?? throw new InvalidOperationException("Invalid ProfileId. Profile does not exist.");

            var companyLicenses = await _subscriptionRepo.GetCompanyLicensesWithAssignmentsAsync(profileId);
            var allUnits = await _subscriptionRepo.GetBusinessUnitsByCompanyAsync(profileId);

            if (allUnits.Count == 0 && requireUnits)
                throw new InvalidOperationException("No business units found for this company.");

            // Index active assignments by unit so each unit is enriched with
            // its own subscription + grace. Units without an active license
            // still appear in the response — they come back as
            // HasSubscription=false so the frontend can show them as
            // "Unsubscribed" rather than hiding them.
            var licensesByUnit = companyLicenses
                .Where(l => l.Assignments != null)
                .SelectMany(l => l.Assignments!
                    .Where(a => a.IsActive && !a.IsDel)
                    .Select(a => new { Assignment = a, License = l }))
                .GroupBy(x => x.Assignment.BusinessUnitId)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Complimentary beneficiaries: each rides a purchased license already
            // in companyLicenses. Project a synthetic license per beneficiary unit
            // so it builds the same detail as the primary team. These units have
            // no purchased assignment, so they never appear in licensesByUnit.
            var licenseById = companyLicenses.ToDictionary(l => l.Id);
            var complimentaryByUnit = new Dictionary<uint, (CompanyLicense synth, int? grace)>();
            var grants = await _licenseRepo.GetActiveComplimentaryGrantsByCompanyAsync(profileId);
            foreach (var grant in grants)
            {
                if (!licenseById.TryGetValue(grant.CompanyLicenseId, out var backing))
                    continue;
                var primaryGrace = backing.Assignments?.FirstOrDefault(a => a.IsActive && !a.IsDel)?.GracePeriod;
                complimentaryByUnit[grant.ComplimentaryBusinessUnitId] =
                    (ComplimentaryLicenseProjection.SyntheticLicense(grant, backing, primaryGrace), primaryGrace);
            }

            // Apply expiry once per distinct subscription before building any
            // DTO — multiple units can share a single CompanySubscription, so
            // looping per-unit would issue redundant UPDATEs.
            var distinctSubs = companyLicenses
                .Select(l => l.CompanySubscription)
                .Where(s => s != null)
                .Distinct()
                .ToList();
            foreach (var s in distinctSubs)
                await ApplyExpiryIfDueAsync(s!);

            var units = new List<SubscriptionDetailDto>(allUnits.Count);
            foreach (var unit in allUnits)
            {
                SubscriptionDetailDto unitDetail;
                if (licensesByUnit.TryGetValue(unit.Id, out var assignments))
                {
                    var unitLicenses = assignments.Select(x => x.License).ToList();
                    var sub = unitLicenses.First().CompanySubscription;
                    var unitGrace = assignments.First().Assignment.GracePeriod;
                    unitDetail = BuildUnitDetail(unit, sub, unitLicenses, unitGrace);
                }
                else if (complimentaryByUnit.TryGetValue(unit.Id, out var comp))
                {
                    // Complimentary coverage — same license/expiry as the primary
                    // team, tagged so it's distinguishable and never counted as a
                    // purchased seat.
                    unitDetail = BuildUnitDetail(unit, comp.synth.CompanySubscription, new List<CompanyLicense> { comp.synth }, comp.grace);
                    MarkComplimentary(unitDetail);
                }
                else
                {
                    // Unsubscribed unit — no active license, no subscription
                    // chain to build from. Emit a stub so the unit still
                    // shows up in the response with HasSubscription=false.
                    unitDetail = BuildUnitDetail(unit, null, new List<CompanyLicense>(), null);
                }

                // BuildUnitDetail doesn't know the company — stamp it here so
                // each unit row carries ProfileId/CompanyName, not just the
                // wrapper. Frontends iterating Units[] shouldn't have to look
                // back at the parent envelope.
                unitDetail.ProfileId = company.Id;
                unitDetail.CompanyName = company.ProfileName;
                units.Add(unitDetail);
            }

            var detail = new CompanySubscriptionDetailDto
            {
                ProfileId = company.Id,
                CompanyName = company.ProfileName,
                Card = await GetCardOnFileAsync(profileId),
                CompanyLicenses = BuildCompanyLicenseSummary(companyLicenses),
                Units = units.OrderBy(u => u.UnitId).ToList()
            };
            await StampLicenseManagersAsync(LicenseRows(detail));
            RecountPool(detail.CompanyLicenses);
            await StampPoolPlansAsync(detail.CompanyLicenses, company.Id);
            return detail;
        }

        // Builds a unit-detail DTO for a unit whose ONLY coverage is a
        // complimentary grant (no purchased assignment of its own). Loads the
        // backing purchased license, projects a synthetic assignment onto this
        // unit, and reuses BuildUnitDetail so the output matches the primary
        // team's license/expiry/subscription. Returns null when the unit has no
        // active grant.
        private async Task<SubscriptionDetailDto?> BuildComplimentaryUnitDetailAsync(BusinessUnit unit)
        {
            var grant = await _licenseRepo.GetActiveComplimentaryGrantForUnitAsync(unit.Id);
            if (grant == null)
                return null;

            var backing = await _db.CompanyLicenses
                .Include(l => l.CompanySubscription!).ThenInclude(s => s.Plan)
                .Include(l => l.Plan)
                .Include(l => l.Assignments!.Where(a => a.IsActive && !a.IsDel))
                .FirstOrDefaultAsync(l => l.Id == grant.CompanyLicenseId);
            if (backing == null)
                return null;

            var sub = backing.CompanySubscription;
            if (sub != null) await ApplyExpiryIfDueAsync(sub);

            var primaryGrace = backing.Assignments?.FirstOrDefault()?.GracePeriod;
            var synth = ComplimentaryLicenseProjection.SyntheticLicense(grant, backing, primaryGrace);

            var dto = BuildUnitDetail(unit, sub, new List<CompanyLicense> { synth }, primaryGrace);
            MarkComplimentary(dto);
            return dto;
        }

        // Tags a unit-detail DTO (and its license rows) as complimentary coverage.
        private static void MarkComplimentary(SubscriptionDetailDto dto)
        {
            dto.IsComplimentary = true;
            foreach (var lic in dto.Licenses)
            {
                lic.IsComplimentary = true;
                lic.AssignmentStatus = "Complimentary";
            }
        }

        // Stamps AssignedToManagerId / AssignedToManagerName on license rows.
        // 1 license = 1 Team Manager: the holder is the Team Manager of the team
        // the license is applied to (most recently assigned first, same rule as
        // GetSharedManagerUserIdAsync), else the manager recorded on its active
        // complimentary grant. Rows for licenses not applied get null.
        private async Task StampLicenseManagersAsync(IEnumerable<SubscriptionLicenseDto> rows)
        {
            var list = rows.ToList();
            // Plan catalogue: each licence's plan and price per manager.
            await StampLicencePlansAsync(list);
            foreach (var row in list)
            {
                row.AssignedToManagerId = null;
                row.AssignedToManagerName = null;
            }

            // A licence held by a live Team Manager (CompanyLicenses.AssignedUserId:
            // the Profile Admin's trial licence, or one auto-assigned at user
            // creation) is in use even before it is on a team: the same "consumed"
            // rule as the availability API.
            var rowIds = list.Select(r => r.LicenseId).Distinct().ToList();
            var tm = (int)CloverBackoffice.Components.Enums.UserGroups.TeamManager;
            var holders = await _db.CompanyLicenses.AsNoTracking()
                .Where(l => rowIds.Contains(l.Id) && l.AssignedUserId != null)
                .Join(_db.Users.Where(u => !u.IsDel
                                           && (u.Roles.Any(r => r.UserGroupId == tm)
                                               || (!u.Roles.Any() && u.UserGroupId == tm))),
                      l => l.AssignedUserId, u => (int?)u.UserId,
                      (l, u) => new { l.Id, l.ProfileId, UserProfileId = u.ProfileId, u.UserId, u.Name })
                .ToListAsync();
            var heldBy = holders
                .Where(h => h.UserProfileId == (int)h.ProfileId)
                .ToDictionary(h => h.Id, h => (Id: h.UserId, h.Name));
            foreach (var row in list)
            {
                if (!heldBy.TryGetValue(row.LicenseId, out var holder)) continue;
                if (row.AssignmentStatus == "Not Applied") row.AssignmentStatus = "Applied";
                row.AssignedToManagerId = holder.Id;
                row.AssignedToManagerName = holder.Name;
            }

            var licenseIds = list
                .Where(r => r.AssignmentStatus is "Applied" or "Complimentary")
                .Select(r => r.LicenseId)
                .Distinct()
                .ToList();
            if (licenseIds.Count == 0) return;

            var teamManager = (int)CloverBackoffice.Components.Enums.UserGroups.TeamManager;

            // Team the license is applied to (live teams only).
            var primaryTeams = await _db.AssignedCompanyLicenses.AsNoTracking()
                .Where(a => licenseIds.Contains(a.CompanyLicenseId) && a.IsActive && !a.IsDel
                            && a.BusinessUnit != null && a.BusinessUnit.IsDel != true)
                .Select(a => new { a.CompanyLicenseId, a.BusinessUnitId })
                .ToListAsync();
            var teamIds = primaryTeams.Select(p => p.BusinessUnitId).Distinct().ToList();

            // Team Managers on those teams (ManagerTeamIds rule: active, or a
            // replacement still waiting to accept the invitation).
            var teamManagers = await _db.UserTeams.AsNoTracking()
                .Where(ut => teamIds.Contains(ut.UnitId) && (!ut.IsUnAssigned || ut.IsPendingActivation))
                .Join(_db.Users.Where(u => !u.IsDel
                                           && (u.Roles.Any(r => r.UserGroupId == teamManager)
                                               || (!u.Roles.Any() && u.UserGroupId == teamManager))),
                      ut => ut.UserId, u => u.UserId,
                      (ut, u) => new { ut.UnitId, u.UserId, u.Name, ut.CreatedDate, UserTeamId = ut.Id })
                .ToListAsync();

            var managerByLicense = new Dictionary<int, (int Id, string? Name)>();
            foreach (var p in primaryTeams)
            {
                var m = teamManagers
                    .Where(t => t.UnitId == p.BusinessUnitId)
                    .OrderByDescending(t => t.CreatedDate).ThenByDescending(t => t.UserTeamId)
                    .FirstOrDefault();
                if (m != null && !managerByLicense.ContainsKey(p.CompanyLicenseId))
                    managerByLicense[p.CompanyLicenseId] = (m.UserId, m.Name);
            }

            // Fallback: the manager recorded on the license's complimentary grant.
            var missing = licenseIds.Where(id => !managerByLicense.ContainsKey(id)).ToList();
            if (missing.Count > 0)
            {
                var grantManagers = await _db.ComplimentaryLicenseGrants.AsNoTracking()
                    .Where(g => missing.Contains(g.CompanyLicenseId) && g.IsActive && !g.IsDel)
                    .Join(_db.Users.Where(u => !u.IsDel), g => g.ManagerUserId, u => u.UserId,
                          (g, u) => new { g.CompanyLicenseId, u.UserId, u.Name, g.GrantedAt })
                    .ToListAsync();
                foreach (var g in grantManagers.OrderBy(g => g.GrantedAt))
                    managerByLicense.TryAdd(g.CompanyLicenseId, (g.UserId, g.Name));
            }

            foreach (var row in list)
            {
                if (row.AssignmentStatus is not ("Applied" or "Complimentary")) continue;
                if (heldBy.ContainsKey(row.LicenseId)) continue;
                if (!managerByLicense.TryGetValue(row.LicenseId, out var manager)) continue;
                row.AssignedToManagerId = manager.Id;
                row.AssignedToManagerName = manager.Name;
            }
        }

        // Pool counts after StampLicenseManagersAsync may have marked held licences Applied.
        private static void RecountPool(CompanyLicensePoolDto? pool)
        {
            if (pool == null) return;
            pool.AppliedCount = pool.Licenses.Count(x => x.AssignmentStatus == "Applied");
            pool.UnappliedCount = pool.TotalCount - pool.AppliedCount;
        }

        // Every license row in a company detail: the pool and each unit's list.
        private static IEnumerable<SubscriptionLicenseDto> LicenseRows(CompanySubscriptionDetailDto detail)
            => (detail.CompanyLicenses?.Licenses ?? Enumerable.Empty<SubscriptionLicenseDto>())
                .Concat((detail.Units ?? new List<SubscriptionDetailDto>())
                    .SelectMany(u => u.Licenses ?? Enumerable.Empty<SubscriptionLicenseDto>()));

        // Builds a /status DTO for a unit whose only coverage is a complimentary
        // grant. Mirrors GetSubscriptionStatusAsync's tail (expiry reconcile +
        // renewal URL) against the backing license. Null when no active grant.
        private async Task<SubscriptionStatusDto?> BuildComplimentaryStatusAsync(Profile company, BusinessUnit unit)
        {
            var grant = await _licenseRepo.GetActiveComplimentaryGrantForUnitAsync(unit.Id);
            if (grant == null)
                return null;

            var backing = await _db.CompanyLicenses
                .Include(l => l.CompanySubscription!).ThenInclude(s => s.Plan)
                .Include(l => l.Assignments!.Where(a => a.IsActive && !a.IsDel))
                .FirstOrDefaultAsync(l => l.Id == grant.CompanyLicenseId);

            var sub = backing?.CompanySubscription;
            if (sub == null)
                return null;

            await ApplyExpiryIfDueAsync(sub);

            var primaryGrace = backing!.Assignments?.FirstOrDefault()?.GracePeriod;
            var dto = BuildStatusDto(sub, company, unit, primaryGrace, backing.ExpiryDate, null);
            dto.IsComplimentary = true;
            if (dto.IsExpired)
                dto.RenewalUrl = await BuildRenewalUrlAsync(sub, company);
            return dto;
        }

        public async Task<uint> GetProfileIdForUnitAsync(uint unitId)
        {
            if (unitId == 0)
                throw new InvalidOperationException("UnitId is required.");

            return await _subscriptionRepo.GetProfileIdForUnitAsync(unitId)
                ?? throw new InvalidOperationException("Invalid UnitId. Unit does not exist.");
        }

        // Company- or unit-scoped transaction view. Composes the existing
        // subscription-detail reads with the payment history + card mapping so
        // the License tab gets everything for the chosen scope in one call.
        public async Task<TransactionDetailsDto> GetTransactionDetailsAsync(uint profileId, uint? unitId, int page, int pageSize, BillingCycle? billingCycle = null)
        {
            if (profileId == 0)
                throw new InvalidOperationException("ProfileId is required.");
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 10;

            bool unitScope = unitId.HasValue && unitId.Value != 0;

            var result = new TransactionDetailsDto
            {
                ProfileId = profileId,
                UnitId = unitScope ? unitId : null,
                Scope = unitScope ? "Unit" : "Company",
                Page = page,
                PageSize = pageSize
            };

            List<Payment> payments;
            int total;

            if (unitScope)
            {
                // Unit scope: the unit's subscription detail + the payments of
                // the subscription its licenses belong to (payments carry no
                // unit column, so subscription is the join).
                var unitDetail = await GetSubscriptionDetailAsync(profileId, unitId!.Value);
                result.UnitSubscription = unitDetail;

                if (unitDetail.SubscriptionId.HasValue)
                {
                    payments = await _paymentRepo.GetByCompanySubscriptionIdAsync(unitDetail.SubscriptionId.Value, page, pageSize);
                    total = await _paymentRepo.GetTotalCountByCompanySubscriptionIdAsync(unitDetail.SubscriptionId.Value);
                    result.Card = await GetCardOnFileForSubscriptionAsync(unitDetail.SubscriptionId.Value);
                }
                else
                {
                    payments = new List<Payment>();
                    total = 0;
                }
            }
            else
            {
                // Company scope: company-wide detail (all units + license pool +
                // card) + all of the company's payments.
                var companyDetail = await GetSubscriptionDetailsForAllUnitsAsync(profileId);
                result.CompanySubscription = companyDetail;
                result.Card = companyDetail.Card;

                payments = await _paymentRepo.GetByProfileIdAsync(profileId, page, pageSize);
                total = await _paymentRepo.GetTotalCountByProfileIdAsync(profileId);
            }

            // Reuse PaymentService's mapping so transaction + card fields stay
            // identical to /api/payment/history and /transaction/details rows.
            // ToHistoryDto already stamps CompanySubscriptionId on each row.
            result.Payments = payments.Select(PaymentService.ToHistoryDto).ToList();
            result.Total = total;

            // Stamp HasSubscription: true when the row's subscription is currently
            // Active (exists, not soft-deleted). One batched lookup for the page.
            var activePaymentSubIds = await _subscriptionRepo.GetActiveSubscriptionIdsAsync(
                result.Payments.Select(d => d.CompanySubscriptionId));
            foreach (var d in result.Payments)
                d.HasSubscription = activePaymentSubIds.Contains(d.CompanySubscriptionId);

            // Stamp each payment row with the expiry date of the license(s) it
            // paid for, so the frontend can show it alongside the card details.
            await PopulateLicenseExpiryAsync(payments, result.Payments);

            // Reshape Description into a clean label + a separate Quantity field
            // (e.g. "Monthly license(s)" + 3) for the Licenses transaction list.
            await PopulateDescriptionAndQuantityAsync(payments, result.Payments);

            // Per-license effective billing cycle (Monthly/Annual). Computed once
            // and reused for both stamping each license row and the optional
            // cycle filter below.
            var licenseCycles = await ResolveLicenseCyclesAsync(profileId);

            // Stamp every license row (company pool + each unit + unit-scope list)
            // with its billing cycle and the last four of the card used for its
            // subscription.
            await PopulateLicenseBillingAndCardAsync(result, licenseCycles);

            // Optional billing-cycle filter: keep only the licenses (company pool
            // + each unit + the unit-scope list) whose effective cycle matches the
            // requested one. Payments, card and subscription dates are untouched.
            // When null the response is returned unchanged (current behavior).
            if (billingCycle.HasValue)
                ApplyLicenseCycleFilter(result, billingCycle.Value, licenseCycles);

            return result;
        }

        // Fills PaymentHistoryDto.LicenseExpiryDate for each transaction row.
        // Payments and licenses share only CompanySubscriptionId (there is no
        // direct payment→license FK), and every license minted in one purchase
        // carries the same expiry — so for each payment we take the licenses on
        // its subscription and pick the batch whose mint time (CreatedAt) is
        // closest to the payment, i.e. the licenses that transaction created or
        // renewed. Rows without a dated license are left null. Matched back to the
        // DTOs by transaction Id (ToHistoryDto copies Payment.Id verbatim).
        //
        // Trial rows are resolved from the subscription's TrialEndDate instead —
        // see the comment on the trial branch below.
        private async Task PopulateLicenseExpiryAsync(List<Payment> payments, List<PaymentHistoryDto> dtos)
        {
            if (payments.Count == 0 || dtos.Count == 0)
                return;

            var subIds = payments.Select(p => p.CompanySubscriptionId).Distinct().ToList();

            var licenses = await _db.CompanyLicenses
                .AsNoTracking()
                .Where(l => !l.IsDel
                            && l.CompanySubscriptionId != null
                            && l.ExpiryDate != null
                            && subIds.Contains(l.CompanySubscriptionId.Value))
                .Select(l => new { SubId = l.CompanySubscriptionId!.Value, l.CreatedAt, l.ExpiryDate })
                .ToListAsync();

            var trialEndBySub = await _db.CompanySubscriptions
                .AsNoTracking()
                .Where(s => subIds.Contains(s.Id) && s.TrialEndDate != null)
                .Select(s => new { s.Id, s.TrialEndDate })
                .ToDictionaryAsync(s => s.Id, s => s.TrialEndDate);

            if (licenses.Count == 0 && trialEndBySub.Count == 0)
                return;

            var bySub = licenses
                .GroupBy(l => l.SubId)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Resolve one expiry per transaction (by nearest mint time), keyed by
            // payment Id so it survives any later reordering of the DTO list.
            var expiryByPayment = new Dictionary<int, DateTime?>();
            foreach (var p in payments)
            {
                // A trial row must report the TRIAL window, not the current expiry
                // of the seats it minted. On trial→paid conversion
                // UpgradeTrialLicensesToPaidAsync extends those very rows in place
                // to the paid period end, so by read time the licenses nearest the
                // trial payment carry a PAID expiry and the trial's own window is
                // no longer recoverable from the license table. TrialEndDate is
                // audit-only (never touched by conversion) and still holds it.
                if (p.PaymentMethod == PaymentMethodType.Trial
                    && trialEndBySub.TryGetValue(p.CompanySubscriptionId, out var trialEnd))
                {
                    expiryByPayment[p.Id] = trialEnd;
                    continue;
                }

                if (!bySub.TryGetValue(p.CompanySubscriptionId, out var lic) || lic.Count == 0)
                    continue;

                var nearest = lic
                    .OrderBy(l => Math.Abs((l.CreatedAt - p.CreatedAt).Ticks))
                    .First();
                expiryByPayment[p.Id] = nearest.ExpiryDate;
            }

            foreach (var d in dtos)
                if (expiryByPayment.TryGetValue(d.Id, out var exp))
                    d.LicenseExpiryDate = exp;
        }

        // Reshapes each transaction row's Description into the label the Licenses
        // UI shows, and fills Quantity, replacing the raw free-text stored on the
        // Payment row:
        //   * Trial            → "Trial - {N} days"  (N = trial window length)
        //   * anything else    → "Monthly license(s)" / "Yearly license(s)",
        //     the cycle taken from the SPAN of the licenses this transaction
        //     minted (> ~6 months ⇒ Yearly) — the same >180-day rule
        //     ResolveLicenseCyclesAsync uses, so a yearly add-on on a monthly
        //     base subscription is still labelled Yearly.
        // Quantity = the size of that license batch: the rows whose mint time
        // (CreatedAt) is closest to the payment (licenses minted together share
        // a CreatedAt). Rows with no resolvable batch keep their original
        // description and a null quantity (e.g. legacy invoice-only payments).
        // Scoped to the transaction/details composition — the shared ToHistoryDto
        // and every other endpoint keep the raw Description.
        private async Task PopulateDescriptionAndQuantityAsync(List<Payment> payments, List<PaymentHistoryDto> dtos)
        {
            if (payments.Count == 0 || dtos.Count == 0)
                return;

            var subIds = payments.Select(p => p.CompanySubscriptionId).Distinct().ToList();

            // All licenses on these subs — INCLUDING soft-deleted (expired) ones,
            // so a past transaction's quantity/label survives after its licenses
            // lapse. (The expiry pass above deliberately excludes IsDel; here we
            // want the full historical batch, not just live rows.)
            var licenses = await _db.CompanyLicenses
                .AsNoTracking()
                .Where(l => l.CompanySubscriptionId != null
                            && subIds.Contains(l.CompanySubscriptionId.Value))
                .Select(l => new { SubId = l.CompanySubscriptionId!.Value, l.CreatedAt, l.ExpiryDate })
                .ToListAsync();

            var bySub = licenses
                .GroupBy(l => l.SubId)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Trial window per sub, for the "Trial - N days" label.
            var trialWindowBySub = await _db.CompanySubscriptions
                .AsNoTracking()
                .Where(s => subIds.Contains(s.Id) && s.TrialStartDate != null && s.TrialEndDate != null)
                .Select(s => new { s.Id, s.TrialStartDate, s.TrialEndDate })
                .ToDictionaryAsync(s => s.Id, s => new { s.TrialStartDate, s.TrialEndDate });

            var descByPayment = new Dictionary<int, string>();
            var qtyByPayment = new Dictionary<int, int>();

            string TrialLabel(int subId)
            {
                var tw = trialWindowBySub[subId];
                var days = (int)Math.Round((tw.TrialEndDate!.Value - tw.TrialStartDate!.Value).TotalDays);
                return $"Trial - {days} days";
            }

            foreach (var p in payments)
            {
                if (bySub.TryGetValue(p.CompanySubscriptionId, out var lic) && lic.Count > 0)
                {
                    // The license batch this transaction minted: the rows whose
                    // mint time is closest to the payment.
                    var nearestMint = lic
                        .OrderBy(l => Math.Abs((l.CreatedAt - p.CreatedAt).Ticks))
                        .First().CreatedAt;
                    var batch = lic.Where(l => l.CreatedAt == nearestMint).ToList();
                    qtyByPayment[p.Id] = batch.Count;

                    if (p.PaymentMethod == PaymentMethodType.Trial
                        && trialWindowBySub.ContainsKey(p.CompanySubscriptionId))
                    {
                        descByPayment[p.Id] = TrialLabel(p.CompanySubscriptionId);
                    }
                    else
                    {
                        var sample = batch[0];
                        bool annual = sample.ExpiryDate.HasValue
                                      && (sample.ExpiryDate.Value - sample.CreatedAt).TotalDays > 180;
                        descByPayment[p.Id] = annual ? "Yearly license(s)" : "Monthly license(s)";
                    }
                }
                else if (p.PaymentMethod == PaymentMethodType.Trial
                         && trialWindowBySub.ContainsKey(p.CompanySubscriptionId))
                {
                    // Trial with no surviving license rows — still label by window.
                    descByPayment[p.Id] = TrialLabel(p.CompanySubscriptionId);
                }
            }

            foreach (var d in dtos)
            {
                if (descByPayment.TryGetValue(d.Id, out var desc))
                    d.Description = desc;
                if (qtyByPayment.TryGetValue(d.Id, out var qty))
                    d.Quantity = qty;
            }
        }

        // Maps every license the company owns to its effective billing cycle.
        // Annual when the license's backing subscription is Annual OR the license
        // itself spans clearly more than a month (~>180 days: monthly ≈ 30, yearly
        // ≈ 365) — the span check catches a yearly license minted on a monthly
        // base subscription (a yearly seat added to a monthly plan), which the
        // sub's BillingCycle alone would mislabel Monthly. Mirrors the team-banner
        // cadence logic (LicenseSpansYear) so both surfaces agree.
        private async Task<Dictionary<int, BillingCycle>> ResolveLicenseCyclesAsync(uint profileId)
        {
            var rows = await _db.CompanyLicenses
                .AsNoTracking()
                .Where(l => l.ProfileId == profileId)
                .Select(l => new
                {
                    l.Id,
                    l.CreatedAt,
                    l.ExpiryDate,
                    SubCycle = l.CompanySubscription != null
                        ? (BillingCycle?)l.CompanySubscription.BillingCycle
                        : null
                })
                .ToListAsync();

            return rows.ToDictionary(
                r => r.Id,
                r => (r.SubCycle == BillingCycle.Annual
                      || (r.ExpiryDate.HasValue && (r.ExpiryDate.Value - r.CreatedAt).TotalDays > 180))
                     ? BillingCycle.Annual
                     : BillingCycle.Monthly);
        }

        // Removes every license that doesn't match the requested cycle from all
        // three places the transaction view carries them, and recomputes the pool
        // / unit counts so the summary stays consistent with the filtered lists.
        private static void ApplyLicenseCycleFilter(
            TransactionDetailsDto result, BillingCycle wanted, Dictionary<int, BillingCycle> cycles)
        {
            bool Keep(SubscriptionLicenseDto l) =>
                cycles.TryGetValue(l.LicenseId, out var c) && c == wanted;

            static int AppliedCount(IEnumerable<SubscriptionLicenseDto> ls) =>
                ls.Count(l => string.Equals(l.AssignmentStatus, "Applied", StringComparison.OrdinalIgnoreCase));

            // Unit scope.
            if (result.UnitSubscription?.Licenses != null)
            {
                result.UnitSubscription.Licenses = result.UnitSubscription.Licenses.Where(Keep).ToList();
                result.UnitSubscription.AppliedLicenseCount = AppliedCount(result.UnitSubscription.Licenses);
            }

            // Company scope: the company-wide pool + every unit's own list.
            if (result.CompanySubscription != null)
            {
                var pool = result.CompanySubscription.CompanyLicenses;
                if (pool?.Licenses != null)
                {
                    pool.Licenses = pool.Licenses.Where(Keep).ToList();
                    pool.TotalCount = pool.Licenses.Count;
                    pool.AppliedCount = AppliedCount(pool.Licenses);
                    pool.UnappliedCount = pool.TotalCount - pool.AppliedCount;
                }

                if (result.CompanySubscription.Units != null)
                {
                    foreach (var unit in result.CompanySubscription.Units)
                    {
                        if (unit.Licenses == null) continue;
                        unit.Licenses = unit.Licenses.Where(Keep).ToList();
                        unit.AppliedLicenseCount = AppliedCount(unit.Licenses);
                    }
                }
            }
        }

        // Stamps every license row in the transaction view with:
        //   * BillingCycle — "Trial" for a trial license, otherwise the license's
        //     resolved Monthly/Annual cycle (from licenseCycles).
        //   * CardLast4    — last four of the card on the payment that MINTED this
        //     license, resolved per-license (never per-subscription-latest). One
        //     subscription can have several payments made with different cards
        //     (e.g. a trial, then two purchases on two cards); each license must
        //     report the card of its own purchase. Trial licenses carry no card.
        // Runs across the three places licenses appear: the company-wide pool,
        // each unit's own list, and the unit-scope list.
        private async Task PopulateLicenseBillingAndCardAsync(
            TransactionDetailsDto result, Dictionary<int, BillingCycle> licenseCycles)
        {
            // Every license DTO in the response (pool + each unit + unit-scope).
            // The same license can appear twice (pool and its unit); both DTO
            // instances get stamped.
            var licenseDtos = new List<SubscriptionLicenseDto>();
            if (result.UnitSubscription?.Licenses != null)
                licenseDtos.AddRange(result.UnitSubscription.Licenses);
            if (result.CompanySubscription?.CompanyLicenses?.Licenses != null)
                licenseDtos.AddRange(result.CompanySubscription.CompanyLicenses.Licenses);
            if (result.CompanySubscription?.Units != null)
                foreach (var unit in result.CompanySubscription.Units)
                    if (unit.Licenses != null)
                        licenseDtos.AddRange(unit.Licenses);

            if (licenseDtos.Count == 0)
                return;

            var licenseIds = licenseDtos.Select(l => l.LicenseId).Distinct().ToList();

            // Mint time + subscription of each license — the correlation key used
            // across this composition to tie a license to the payment that minted
            // it (licenses minted together share a CreatedAt).
            var mintRows = await _db.CompanyLicenses
                .AsNoTracking()
                .Where(l => licenseIds.Contains(l.Id))
                .Select(l => new { l.Id, l.CompanySubscriptionId, l.CreatedAt })
                .ToListAsync();
            var mintById = mintRows.ToDictionary(x => x.Id);

            // ALL payments on the subscriptions these licenses belong to — NOT the
            // paged transaction list — so every license can find its minting
            // payment even when that payment isn't on the current page. Trial
            // payments (CardLast4 == null) are included so a paid license is never
            // mis-matched to a trial and trial licenses resolve to null.
            var subIds = mintRows
                .Where(x => x.CompanySubscriptionId.HasValue)
                .Select(x => x.CompanySubscriptionId!.Value)
                .Distinct()
                .ToList();

            var payRows = await _db.Payments
                .AsNoTracking()
                .Where(p => subIds.Contains(p.CompanySubscriptionId))
                .Select(p => new { p.CompanySubscriptionId, p.CreatedAt, p.CardLast4 })
                .ToListAsync();
            var paysBySub = payRows
                .GroupBy(p => p.CompanySubscriptionId)
                .ToDictionary(g => g.Key, g => g.ToList());

            string? CardFor(SubscriptionLicenseDto l)
            {
                // A trial license is never tied to a payment card.
                if (string.Equals(l.LicenseType, "Trial", StringComparison.OrdinalIgnoreCase))
                    return null;
                if (!mintById.TryGetValue(l.LicenseId, out var m)
                    || !m.CompanySubscriptionId.HasValue
                    || !paysBySub.TryGetValue(m.CompanySubscriptionId.Value, out var pays)
                    || pays.Count == 0)
                    return null;

                // The payment that minted this license: nearest by mint time.
                return pays
                    .OrderBy(p => Math.Abs((p.CreatedAt - m.CreatedAt).Ticks))
                    .First().CardLast4;
            }

            foreach (var l in licenseDtos)
            {
                l.BillingCycle = string.Equals(l.LicenseType, "Trial", StringComparison.OrdinalIgnoreCase)
                    ? "Trial"
                    : (licenseCycles.TryGetValue(l.LicenseId, out var c) ? c.ToString() : BillingCycle.Monthly.ToString());
                l.CardLast4 = CardFor(l);
            }
        }

        // Most-recent card on file for a specific subscription (unit scope) —
        // mirrors GetCardOnFileAsync but filtered by subscription id.
        private async Task<PaymentCardDto?> GetCardOnFileForSubscriptionAsync(int subscriptionId)
        {
            return await _db.Payments
                .AsNoTracking()
                .Where(p => p.CompanySubscriptionId == subscriptionId && p.CardLast4 != null)
                .OrderByDescending(p => p.CreatedAt)
                .Select(p => new PaymentCardDto
                {
                    CardholderName = p.CardholderName,
                    CardType = p.CardBrand,
                    CardLast4 = p.CardLast4,
                    ExpMonth = p.CardExpMonth,
                    ExpYear = p.CardExpYear,
                    Funding = p.CardFunding,
                    Country = p.CardCountry,
                    PaymentMethodId = p.StripePaymentMethodId
                })
                .FirstOrDefaultAsync();
        }

        // Most-recent card on file for the company — brand + last four taken
        // from the latest payment that actually carries card details. Returns
        // null when the company has no card payment (trial-only / invoice-paid).
        private async Task<PaymentCardDto?> GetCardOnFileAsync(uint profileId)
        {
            return await _db.Payments
                .AsNoTracking()
                .Where(p => p.ProfileId == profileId && p.CardLast4 != null)
                .OrderByDescending(p => p.CreatedAt)
                .Select(p => new PaymentCardDto
                {
                    CardholderName = p.CardholderName,
                    CardType = p.CardBrand,
                    CardLast4 = p.CardLast4,
                    ExpMonth = p.CardExpMonth,
                    ExpYear = p.CardExpYear,
                    Funding = p.CardFunding,
                    Country = p.CardCountry,
                    PaymentMethodId = p.StripePaymentMethodId
                })
                .FirstOrDefaultAsync();
        }

        // Admin-only trial extension / grace tool. This NEVER converts a trial
        // to a paid subscription — it only pushes a trial company's window out.
        //   * ProfileId only          → extend the profile's trial: its trial
        //                               subscription(s) and trial licences. Paid
        //                               licences / paid subscriptions of the same
        //                               profile are left as they are.
        //   * ProfileId + UnitId      → extend only that unit's trial license(s).
        // EndDate sets the new trial end date (licenses move with it); ExtendByDays
        // adds days to the current trial end instead (company-wide only);
        // GracePeriod adds extra days on top. Only the requested profile's rows
        // are touched, all in one transaction, and every call writes a
        // TrialExtensionAuditLogs row naming the admin.
        public async Task<CompanySubscriptionDetailDto> ApplySubscriptionAsync(ApplySubscriptionRequestDto dto, int? actorUserId)
        {
            if (!dto.ProfileId.HasValue || dto.ProfileId.Value == 0)
                throw new InvalidOperationException("ProfileId is required.");

            var forUnit = dto.UnitId.HasValue && dto.UnitId.Value != 0;
            if (dto.ExtendByDays.HasValue)
            {
                if (dto.ExtendByDays.Value <= 0 || dto.ExtendByDays.Value > MaxTrialExtensionDays)
                    throw new InvalidOperationException($"ExtendByDays must be between 1 and {MaxTrialExtensionDays}.");
                if (dto.EndDate.HasValue)
                    throw new InvalidOperationException("Provide ExtendByDays or EndDate, not both.");
                if (forUnit)
                    throw new InvalidOperationException("ExtendByDays extends the whole profile's trial. Leave UnitId out.");
            }
            else if (!dto.EndDate.HasValue && !dto.GracePeriod.HasValue)
                throw new InvalidOperationException("Provide ExtendByDays (days to add) or EndDate to extend the trial, GracePeriod to add grace days, or a combination.");

            if (dto.GracePeriod is < 0)
                throw new InvalidOperationException("GracePeriod cannot be negative.");

            var notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim();
            if (notes?.Length > TrialExtensionAuditLog.NotesMaxLength)
                throw new InvalidOperationException($"Notes cannot be longer than {TrialExtensionAuditLog.NotesMaxLength} characters.");

            var company = await _subscriptionRepo.GetCompanyByIdAsync(dto.ProfileId.Value)
                ?? throw new InvalidOperationException("Invalid ProfileId. Profile does not exist.");

            var now = DateTime.UtcNow;

            TrialExtensionAuditLog audit;
            await using (var tx = await _db.Database.BeginTransactionAsync())
            {
                // Two extensions of one profile queue here, so each reads the end
                // date the other wrote (14 + 14 days is 28, never 14).
                await LockProfileLicensesAsync(company.Id);

                audit = forUnit
                    ? await ExtendTrialForUnitAsync(company, dto, actorUserId, now)
                    : await ExtendTrialForCompanyAsync(company, dto, now);

                audit.ProfileId = company.Id;
                audit.Notes = notes;
                audit.ExtendedByUserId = actorUserId ?? 0;
                audit.LoggedAt = now;
                _db.TrialExtensionAuditLogs.Add(audit);
                await _db.SaveChangesAsync();
                await tx.CommitAsync();
            }

            _logger.LogInformation(
                "Trial extended for profile {ProfileId} ({Scope}, unit {UnitId}) by user {ActorId}: {PreviousEnd:O} -> {NewEnd:O}, +{Days} day(s), grace {Grace}.",
                company.Id, audit.Scope, audit.UnitId, actorUserId, audit.PreviousTrialEndDate, audit.NewTrialEndDate, audit.DaysAdded, audit.NewGracePeriod);

            // Always return the full company picture so the admin sees every
            // unit's (now extended) dates and the company-wide license pool,
            // plus what this extension changed. A profile with no teams yet gets
            // an empty Units list rather than an error — the extension is saved.
            var detail = await GetSubscriptionDetailsForAllUnitsAsync(company.Id, requireUnits: false);
            detail.TrialExtension = new TrialExtensionResultDto
            {
                AuditId = audit.AuditId,
                Scope = audit.Scope,
                UnitId = audit.UnitId,
                DaysAdded = audit.DaysAdded,
                PreviousTrialEndDate = audit.PreviousTrialEndDate,
                NewTrialEndDate = audit.NewTrialEndDate,
                GracePeriod = audit.NewGracePeriod,
                EffectiveExpiryDate = audit.NewTrialEndDate?.AddDays(audit.NewGracePeriod ?? 0),
                LicensesUpdated = audit.LicensesUpdated,
                Notes = audit.Notes,
                ExtendedByUserId = audit.ExtendedByUserId,
                ExtendedAt = audit.LoggedAt
            };
            return detail;
        }

        // Upper bound for ExtendByDays: a sanity limit (10 years) so a typo can't
        // push the date out of range, not a business cap.
        private const int MaxTrialExtensionDays = 3650;

        // Per-profile licence lock, shared with Add User, licence assignment and
        // team creation (CompanyLicenseService.ProfileLicenseLockKey). Virtual
        // only so in-memory tests can skip the advisory lock.
        private const int ProfileLicenseLockKey = 16016;

        protected virtual Task LockProfileLicensesAsync(uint profileId)
            => _db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({ProfileLicenseLockKey}, {(int)profileId})");

        // Company-wide: extend the profile's trial to the new end date / grace.
        // ExtendByDays counts from the current trial end, or from now when the
        // trial has already ended, so the extension always gives that many days
        // of access.
        //
        // Eligibility is per LICENCE, not per profile: one profile can have Team
        // Managers on trial next to Team Managers on paid licences (a purchase
        // during the trial turns the trial subscription row Active). Only the
        // trial side moves:
        //   * trial licences (IsTrialSeat) get the new expiry;
        //   * subscriptions still on trial (IsTrialSub) get the new window/grace;
        //   * paid licences and paid subscriptions are never touched.
        // Refused only when the profile has nothing on trial to extend.
        private async Task<TrialExtensionAuditLog> ExtendTrialForCompanyAsync(Infrastructure.Models.Profile company, ApplySubscriptionRequestDto dto, DateTime now)
        {
            var subs = await _db.CompanySubscriptions
                .Where(s => s.ProfileId == company.Id && !s.IsDel)
                .ToListAsync();

            if (subs.Count == 0)
                throw new InvalidOperationException("Company has no subscription to extend.");

            var trialSubs = subs.Where(IsTrialSub).ToList();
            var profileHasPaidSub = trialSubs.Count < subs.Count;
            var subById = subs.ToDictionary(s => s.Id);

            // Trials ended by a move to a purchased licence are excluded (and never revived).
            var trialLicenses = (await _db.CompanyLicenses
                    .Where(l => l.ProfileId == company.Id && l.TerminatedAt == null)
                    .ToListAsync())
                .Where(l => IsTrialSeat(l, LicenseSub(l, subById), profileHasPaidSub))
                .ToList();

            if (trialSubs.Count == 0 && trialLicenses.Count == 0)
                throw new InvalidOperationException(NoTrialToExtendMessage);

            // An all-trial profile reads the end from its trial subscription(s), as
            // before; a trial licence on a paid subscription reads its own expiry.
            var previousEnd = trialSubs.Count > 0
                ? trialSubs.Max(s => s.TrialEndDate ?? s.CurrentPeriodEnd)
                : trialLicenses.Max(l => l.ExpiryDate) ?? now;
            var previousGrace = trialSubs.Count > 0 ? trialSubs.Max(s => s.GracePeriod) : null;

            var endDate = dto.EndDate;
            if (dto.ExtendByDays.HasValue)
                endDate = (previousEnd > now ? previousEnd : now).AddDays(dto.ExtendByDays.Value);

            foreach (var sub in trialSubs)
            {
                ApplyTrialExtension(sub, dto.StartDate, endDate, dto.GracePeriod, now);
                await _subscriptionRepo.UpdateAsync(sub);
            }

            // Every trial licence tracks the new trial end date.
            //   * on a trial subscription: the end date; grace comes from that
            //     subscription's GracePeriod (set above), as before;
            //   * on a paid subscription (trial row turned Active by a purchase):
            //     the subscription's grace would also stretch its paid licences,
            //     so the grace days go onto this trial licence's own end date.
            var onPaidSub = trialLicenses
                .Where(l => LicenseSub(l, subById) is { } s && !IsTrialSub(s))
                .ToList();
            var onTrialSub = trialLicenses.Except(onPaidSub).ToList();
            var grace = dto.GracePeriod ?? 0;

            var licensesUpdated = 0;
            if (endDate.HasValue && onTrialSub.Count > 0)
            {
                await MoveLicensesExpiryAsync(onTrialSub, endDate.Value, now);
                licensesUpdated += onTrialSub.Count;
            }
            if ((endDate.HasValue || grace > 0) && onPaidSub.Count > 0)
            {
                // Grace only (no new end date): on top of each licence's own end.
                foreach (var group in onPaidSub.GroupBy(l => (endDate ?? l.ExpiryDate ?? now).AddDays(grace)))
                    await MoveLicensesExpiryAsync(group.ToList(), group.Key, now);
                licensesUpdated += onPaidSub.Count;
            }

            var auditSubId = trialSubs.Count > 0
                ? trialSubs.OrderByDescending(s => s.TrialEndDate ?? s.CurrentPeriodEnd).First().Id
                : trialLicenses.Select(l => l.CompanySubscriptionId).FirstOrDefault(id => id.HasValue)
                  ?? subs.OrderByDescending(s => s.CurrentPeriodEnd).First().Id;

            return new TrialExtensionAuditLog
            {
                SubscriptionId = auditSubId,
                Scope = "PROFILE",
                DaysAdded = dto.ExtendByDays,
                PreviousTrialEndDate = previousEnd,
                NewTrialEndDate = endDate ?? previousEnd,
                PreviousGracePeriod = previousGrace,
                NewGracePeriod = dto.GracePeriod ?? previousGrace,
                LicensesUpdated = licensesUpdated
            };
        }

        private const string NoTrialToExtendMessage = "This profile has no trial license to extend.";

        private static CompanySubscription? LicenseSub(CompanyLicense l, IReadOnlyDictionary<int, CompanySubscription> subById)
            => l.CompanySubscriptionId is int id && subById.TryGetValue(id, out var s) ? s : null;

        // A licence the trial extension may move: a Trial licence, or any licence
        // riding on a subscription that is still on trial (an all-trial profile
        // moves every licence, as before). A licence with no subscription moves
        // only while the profile has no paid subscription. Paid seats on a paid
        // subscription and trials ended by a move to a purchased licence never move.
        private static bool IsTrialSeat(CompanyLicense l, CompanySubscription? sub, bool profileHasPaidSub)
            => l.TerminatedAt == null
               && (string.Equals(l.LicenseType, "Trial", StringComparison.OrdinalIgnoreCase)
                   || (sub != null ? IsTrialSub(sub) : !profileHasPaidSub));

        // Single unit: extend only the license(s) assigned to that unit and the
        // unit's grace. The shared company subscription window is left untouched
        // so the other units are unaffected.
        private async Task<TrialExtensionAuditLog> ExtendTrialForUnitAsync(Infrastructure.Models.Profile company, ApplySubscriptionRequestDto dto, int? actorUserId, DateTime now)
        {
            _ = await _subscriptionRepo.GetBusinessUnitForCompanyAsync(dto.UnitId!.Value, company.Id)
                ?? throw new InvalidOperationException("Invalid UnitId. Unit does not exist for the provided Company.");

            var assignment = await _subscriptionRepo.GetActiveLicenseAssignmentForUnitAsync(dto.UnitId.Value)
                ?? throw new InvalidOperationException("Unit has no active license to extend.");

            var sub = assignment.CompanyLicense?.CompanySubscription
                ?? throw new InvalidOperationException("Active license has no subscription attached.");

            // Per licence, as the company-wide path: the team's licence must be a
            // trial seat, even when its subscription row turned Active because the
            // profile bought paid licences. Only trial licences on the team move.
            var profileHasPaidSub = await _db.CompanySubscriptions
                .AnyAsync(s => s.ProfileId == company.Id && !s.IsDel
                               && !(s.Status == SubscriptionStatus.Trial
                                    || (s.Status == SubscriptionStatus.Expired
                                        && (s.StripeSubscriptionId == null || s.StripeSubscriptionId == ""))));
            if (!IsTrialSeat(assignment.CompanyLicense!, sub, profileHasPaidSub))
                throw new InvalidOperationException("Trial extension is only allowed for a team on a trial license.");

            var unitLicenses = (await _subscriptionRepo.GetActiveLicensesForUnitAsync(dto.UnitId.Value))
                .Where(l => IsTrialSeat(l, l.CompanySubscription, profileHasPaidSub))
                .ToList();
            var previousEnd = unitLicenses.Max(l => l.ExpiryDate) ?? sub.TrialEndDate ?? sub.CurrentPeriodEnd;
            var previousGrace = assignment.GracePeriod;

            var licensesUpdated = 0;
            if (dto.EndDate.HasValue)
                licensesUpdated = await SetLicensesExpiryAsync(unitLicenses.Select(l => l.Id), dto.EndDate.Value, now);

            if (dto.GracePeriod.HasValue)
            {
                assignment.GracePeriod = dto.GracePeriod.Value;
                assignment.UpdatedAt = now;
                assignment.UpdatedBy = actorUserId;
                await _db.SaveChangesAsync();
            }

            return new TrialExtensionAuditLog
            {
                UnitId = dto.UnitId.Value,
                Scope = "UNIT",
                PreviousTrialEndDate = previousEnd,
                NewTrialEndDate = dto.EndDate ?? previousEnd,
                PreviousGracePeriod = previousGrace,
                NewGracePeriod = dto.GracePeriod ?? previousGrace,
                LicensesUpdated = licensesUpdated
            };
        }

        // Push the trial window out and keep the row firmly in Trial status.
        // Never sets a paid status and never clears IsTrialUsed.
        private static void ApplyTrialExtension(CompanySubscription sub, DateTime? startDate, DateTime? endDate, int? gracePeriod, DateTime now)
        {
            if (startDate.HasValue)
            {
                sub.CurrentPeriodStart = startDate.Value;
                sub.TrialStartDate = startDate.Value;
            }

            if (endDate.HasValue)
            {
                sub.CurrentPeriodEnd = endDate.Value;
                sub.TrialEndDate = endDate.Value;
            }

            if (gracePeriod.HasValue)
                sub.GracePeriod = gracePeriod.Value;

            // Stay on trial — this flow must never convert to paid.
            sub.Status = SubscriptionStatus.Trial;
            sub.CancelAtPeriodEnd = false;
            sub.CanceledAt = null;
            sub.LastWarningDaysSent = null;
            sub.UpdatedAt = now;
        }

        // Moves the licences to the new expiry. One that had expired (soft-deleted)
        // and now runs into the future is revived WITH its last team assignment
        // reopened, as ReconcileLicenseExpiryAsync does. Flipping IsDel alone left
        // the team unlicensed: the reconciler only reopens assignments for
        // licences it still finds deleted.
        private async Task MoveLicensesExpiryAsync(List<CompanyLicense> licenses, DateTime newExpiry, DateTime now)
        {
            var revived = newExpiry > now
                ? licenses.Where(l => l.IsDel).Select(l => l.Id).ToList()
                : new List<int>();

            foreach (var lic in licenses)
            {
                lic.ExpiryDate = newExpiry;
                if (revived.Contains(lic.Id))
                    lic.IsDel = false;
                lic.UpdatedAt = now;
            }

            if (revived.Count > 0)
            {
                var last = await _db.AssignedCompanyLicenses
                    .Where(a => revived.Contains(a.CompanyLicenseId) && !a.IsDel)
                    .GroupBy(a => a.CompanyLicenseId)
                    .Select(g => g.OrderByDescending(a => a.AssignedAt).First())
                    .ToListAsync();
                foreach (var a in last)
                {
                    a.IsActive = true;
                    a.UnassignedAt = null;
                    a.UpdatedAt = now;
                }
            }

            await _db.SaveChangesAsync();
        }

        // Set ExpiryDate on a specific set of licenses (the ones assigned to a
        // single unit) without touching the rest of the company's pool.
        private async Task<int> SetLicensesExpiryAsync(IEnumerable<int> licenseIds, DateTime newExpiry, DateTime now)
        {
            var ids = licenseIds.Distinct().ToList();
            if (ids.Count == 0) return 0;

            var licenses = await _db.CompanyLicenses
                .Where(l => ids.Contains(l.Id) && l.TerminatedAt == null)
                .ToListAsync();

            await MoveLicensesExpiryAsync(licenses, newExpiry, now);
            return licenses.Count;
        }

        // Clovera-admin-only license expiry extension. See ISubscriptionService
        // for the full contract. Always extends FORWARD by one billing cycle and
        // revives any soft-deleted (expired) license it touches.
        public async Task<CompanySubscriptionDetailDto> ExtendLicenseAsync(ExtendLicenseRequestDto dto, int? actorUserId)
        {
            if (dto == null || !dto.ProfileId.HasValue || dto.ProfileId.Value == 0)
                throw new InvalidOperationException("ProfileId is required.");

            var company = await _subscriptionRepo.GetCompanyByIdAsync(dto.ProfileId.Value)
                ?? throw new InvalidOperationException("Invalid ProfileId. Profile does not exist.");

            // Trial-only guard. Extend License / Extend Trial (and any grace it
            // grants) is restricted to companies CURRENTLY on a trial. A company
            // that is on — or has ever converted to — a PAID subscription must be
            // rejected here so a paid plan can never be extended for free through
            // this admin tool. "Trial" matches IsTrialSub (also used by /apply to
            // pick which subscriptions move): a live Trial, or an expired trial that never had
            // a Stripe subscription. A paid plan — active, canceled, or lapsed —
            // carries a StripeSubscriptionId and so is NOT a trial and is blocked.
            var companySubs = await _db.CompanySubscriptions
                .Where(s => s.ProfileId == company.Id && !s.IsDel)
                .ToListAsync();
            if (companySubs.Count == 0)
                throw new InvalidOperationException("Company has no subscription to extend.");
            if (companySubs.Any(s => !IsTrialSub(s)))
                throw new InvalidOperationException(
                    "License extension is only allowed for companies currently on a trial. This company is on a paid subscription and cannot be extended here.");

            var now = DateTime.UtcNow;

            if (dto.LicenseId.HasValue && dto.LicenseId.Value != 0)
            {
                await ExtendSingleLicenseAsync(company.Id, dto.LicenseId.Value, dto.ExpiryDate, dto.Notes, actorUserId, now);
                // Scoped response: only the extended license and the unit it is
                // assigned to — no other licenses or units.
                return await BuildSingleLicenseDetailAsync(company, dto.LicenseId.Value);
            }

            await ExtendAllCompanyLicensesAsync(company.Id, dto.ExpiryDate, dto.Notes, actorUserId, now);

            // Company-wide: return the full company picture so the admin sees
            // every unit's (now extended) dates and the company-wide license pool.
            return await GetSubscriptionDetailsForAllUnitsAsync(company.Id);
        }

        // Scoped detail for a single extended license: the company envelope, a
        // license pool containing ONLY this license, and at most one unit — the
        // one the license is actively assigned to. Other licenses/units are
        // intentionally omitted.
        private async Task<CompanySubscriptionDetailDto> BuildSingleLicenseDetailAsync(Infrastructure.Models.Profile company, int licenseId)
        {
            var license = await _db.CompanyLicenses
                .Include(l => l.Plan)
                .Include(l => l.CompanySubscription).ThenInclude(s => s!.Plan)
                .Include(l => l.Assignments!).ThenInclude(a => a.BusinessUnit)
                .FirstOrDefaultAsync(l => l.Id == licenseId && l.ProfileId == company.Id)
                ?? throw new InvalidOperationException("License not found for the provided company.");

            var single = new List<CompanyLicense> { license };

            // This license's active complimentary grants (the extra teams it
            // covers for free) — each surfaced as a unit row below.
            var grants = await _licenseRepo.GetActiveComplimentaryGrantsForLicenseAsync(license.Id);
            var benUnitIds = grants.Select(g => g.ComplimentaryBusinessUnitId).ToList();
            var benUnits = await _db.BusinessUnits.AsNoTracking()
                .Where(b => benUnitIds.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id);

            var result = new CompanySubscriptionDetailDto
            {
                ProfileId = company.Id,
                CompanyName = company.ProfileName,
                CompanyLicenses = BuildCompanyLicenseSummary(single),
                Units = new List<SubscriptionDetailDto>()
            };

            // Surface the unit only when the license is actively assigned. An
            // unassigned (pooled) license has no unit to show.
            var active = license.Assignments?.FirstOrDefault(a => a.IsActive && !a.IsDel);
            if (active?.BusinessUnit != null)
            {
                var unitDetail = BuildUnitDetail(active.BusinessUnit, license.CompanySubscription, single, active.GracePeriod);
                unitDetail.ProfileId = company.Id;
                unitDetail.CompanyName = company.ProfileName;
                result.Units.Add(unitDetail);
            }

            // Also surface the complimentary beneficiary teams riding this license.
            foreach (var grant in grants)
            {
                if (!benUnits.TryGetValue(grant.ComplimentaryBusinessUnitId, out var benUnit))
                    continue;
                var primaryGrace = active?.GracePeriod;
                var synth = ComplimentaryLicenseProjection.SyntheticLicense(grant, license, primaryGrace);
                var compDetail = BuildUnitDetail(benUnit, license.CompanySubscription, new List<CompanyLicense> { synth }, primaryGrace);
                MarkComplimentary(compDetail);
                compDetail.ProfileId = company.Id;
                compDetail.CompanyName = company.ProfileName;
                result.Units.Add(compDetail);
            }

            await StampLicenseManagersAsync(LicenseRows(result));
            RecountPool(result.CompanyLicenses);
            await StampPoolPlansAsync(result.CompanyLicenses, company.Id);
            return result;
        }

        // Single license: extend ONLY the named license. The subscription
        // plan/status and every other license are left untouched — a trial
        // license stays a trial license here (no Trial→Paid promotion). When
        // expiryDate is supplied it is the new expiry; otherwise one billing
        // cycle is added on top of the current window.
        private async Task ExtendSingleLicenseAsync(uint profileId, int licenseId, DateTime? expiryDate, string? notes, int? actorUserId, DateTime now)
        {
            var license = await _db.CompanyLicenses
                .Include(l => l.CompanySubscription)
                .FirstOrDefaultAsync(l => l.Id == licenseId && l.ProfileId == profileId)
                ?? throw new InvalidOperationException("License not found for the provided company.");
            if (license.TerminatedAt != null)
                throw new InvalidOperationException(
                    "This trial license was ended when its Team Manager moved to a purchased license, so it cannot be extended.");

            var cycle = license.CompanySubscription?.BillingCycle ?? BillingCycle.Monthly;
            var anchor = license.ExpiryDate.HasValue && license.ExpiryDate.Value > now ? license.ExpiryDate.Value : now;
            var newExpiry = expiryDate ?? ComputeLicenseExpiry(cycle, anchor);

            await ExtendAndStampLicensesAsync(new List<CompanyLicense> { license }, newExpiry, notes, promoteToPaid: false, force: expiryDate.HasValue, actorUserId, now);
        }

        // Company-wide: extend every license under the company by one billing
        // cycle. Licenses are grouped by their subscription so each group is
        // anchored to that subscription's period end:
        //   * Trial subscription → flipped to Paid (Status=Active, period rolled
        //     forward) and its licenses promoted Trial→Paid.
        //   * Paid subscription  → stays Paid; period + licenses roll forward.
        //     NOTE: ExtendLicenseAsync now rejects any company that holds a paid
        //     subscription, so this paid branch is unreachable in practice and
        //     kept only as a defensive fallback.
        // Licenses with no subscription link (legacy/imported rows) are still
        // extended, anchored to their own latest expiry, with no promotion.
        // When expiryDate is supplied it is used as the new expiry for every
        // subscription period + license; otherwise one billing cycle is added.
        private async Task ExtendAllCompanyLicensesAsync(uint profileId, DateTime? expiryDate, string? notes, int? actorUserId, DateTime now)
        {
            var allLicenses = await _db.CompanyLicenses
                .Where(l => l.ProfileId == profileId && l.TerminatedAt == null)
                .ToListAsync();

            if (allLicenses.Count == 0)
                throw new InvalidOperationException("Company has no licenses to extend.");

            var subs = await _db.CompanySubscriptions
                .Where(s => s.ProfileId == profileId && !s.IsDel)
                .ToListAsync();
            var subById = subs.ToDictionary(s => s.Id);

            foreach (var group in allLicenses.GroupBy(l => l.CompanySubscriptionId))
            {
                if (group.Key.HasValue && subById.TryGetValue(group.Key.Value, out var sub))
                {
                    var cycle = sub.BillingCycle;
                    var anchor = sub.CurrentPeriodEnd > now ? sub.CurrentPeriodEnd : now;
                    var newExpiry = expiryDate ?? ComputeLicenseExpiry(cycle, anchor);
                    bool wasTrial = IsTrialSub(sub);

                    // Roll the subscription forward and land it firmly on Paid.
                    if (sub.CurrentPeriodStart == default) sub.CurrentPeriodStart = now;
                    sub.CurrentPeriodEnd = newExpiry;
                    sub.Status = SubscriptionStatus.Active;
                    sub.CancelAtPeriodEnd = false;
                    sub.CanceledAt = null;
                    sub.LastWarningDaysSent = null;
                    sub.UpdatedAt = now;
                    await _subscriptionRepo.UpdateAsync(sub);

                    await ExtendAndStampLicensesAsync(group.ToList(), newExpiry, notes, promoteToPaid: wasTrial, force: expiryDate.HasValue, actorUserId, now);
                }
                else
                {
                    // Orphan licenses (no live subscription): anchor to the latest
                    // expiry among them, default to a Monthly cycle, no promotion.
                    var latest = group
                        .Where(l => l.ExpiryDate.HasValue)
                        .Select(l => l.ExpiryDate!.Value)
                        .DefaultIfEmpty(now)
                        .Max();
                    var anchor = latest > now ? latest : now;
                    var newExpiry = expiryDate ?? ComputeLicenseExpiry(BillingCycle.Monthly, anchor);
                    await ExtendAndStampLicensesAsync(group.ToList(), newExpiry, notes, promoteToPaid: false, force: expiryDate.HasValue, actorUserId, now);
                }
            }
        }

        // Apply a new expiry to a set of licenses, stamp the optional Notes remark,
        // revive any that were soft-deleted, optionally promote Trial→Paid, and
        // write one UPDATE audit row per license. By default expiry only moves
        // FORWARD; pass force=true (an explicit admin ExpiryDate) to set it
        // authoritatively even when that shortens the current window.
        private async Task ExtendAndStampLicensesAsync(List<CompanyLicense> licenses, DateTime newExpiry, string? notes, bool promoteToPaid, bool force, int? actorUserId, DateTime now)
        {
            if (licenses.Count == 0) return;

            // Collision-safe TRI→PRO rename (same rationale as
            // UpgradeTrialLicensesToPaidAsync). All licenses in a call share one
            // company. Load the company's existing keys + next free sequence so a
            // promoted key never clashes with an occupied (incl. soft-deleted)
            // PRO key on the unique LicenseKey index.
            var stampProfileId = licenses[0].ProfileId;
            var existingKeys = new HashSet<string>(await _db.CompanyLicenses
                .Where(l => l.ProfileId == stampProfileId && l.LicenseKey != null)
                .Select(l => l.LicenseKey!)
                .ToListAsync());
            var nextSeq = await NextLicenseSeqAsync(stampProfileId);

            foreach (var lic in licenses)
            {
                if (promoteToPaid && lic.LicenseType == "Trial")
                {
                    lic.LicenseType = "Paid";
                    if (!string.IsNullOrEmpty(lic.LicenseKey) && lic.LicenseKey.Contains("-TRI"))
                    {
                        var candidate = lic.LicenseKey.Replace("-TRI", "-PRO");
                        if (existingKeys.Contains(candidate))
                            candidate = $"CLD-PRO{nextSeq++:000}-{stampProfileId}";
                        existingKeys.Add(candidate);
                        lic.LicenseKey = candidate;
                    }
                }

                if (force || !lic.ExpiryDate.HasValue || lic.ExpiryDate.Value < newExpiry)
                    lic.ExpiryDate = newExpiry;

                if (!string.IsNullOrWhiteSpace(notes))
                    lic.Notes = notes;

                lic.IsDel = false;               // revive a previously-expired seat
                lic.UpdatedAt = now;
                lic.UpdatedBy = actorUserId;
            }
            await _db.SaveChangesAsync();

            // Audit: one UPDATE per license, VersionNumber = max(existing) + 1 so
            // the row history reads 1 (INSERT) → 2 (UPDATE) → … chronologically.
            var ids = licenses.Select(l => l.Id).ToList();
            var maxVersions = await _db.CompanyLicenseAuditLogs
                .Where(a => ids.Contains(a.Id))
                .GroupBy(a => a.Id)
                .Select(g => new { Id = g.Key, Max = g.Max(a => a.VersionNumber) })
                .ToDictionaryAsync(x => x.Id, x => x.Max);

            _db.CompanyLicenseAuditLogs.AddRange(licenses.Select(l => new CompanyLicenseAuditLog
            {
                Id = l.Id,
                ProfileId = l.ProfileId,
                LicenseKey = l.LicenseKey,
                LicenseType = l.LicenseType,
                CompanySubscriptionId = l.CompanySubscriptionId,
                PlanId = l.PlanId,
                ExpiryDate = l.ExpiryDate,
                Notes = l.Notes,
                CreatedBy = l.CreatedBy,
                UpdatedBy = l.UpdatedBy,
                CreatedAt = l.CreatedAt,
                UpdatedAt = l.UpdatedAt,
                IsDel = l.IsDel,
                LoggedAt = now,
                VersionNumber = (maxVersions.TryGetValue(l.Id, out var v) ? v : 0) + 1,
                OperationType = "UPDATE"
            }));
            await _db.SaveChangesAsync();
        }

        // A subscription is "on trial" when it is live Trial, or an Expired row
        // that never had a Stripe subscription (an expired trial, not a lapsed
        // paid plan).
        private static bool IsTrialSub(CompanySubscription sub)
            => sub.Status == SubscriptionStatus.Trial
               || (sub.Status == SubscriptionStatus.Expired && string.IsNullOrEmpty(sub.StripeSubscriptionId));

        private static SubscriptionDetailDto BuildUnitDetail(
            BusinessUnit unit,
            CompanySubscription? sub,
            List<CompanyLicense> unitLicenses,
            int? gracePeriod)
        {
            var dto = new SubscriptionDetailDto
            {
                UnitId = unit.Id,
                UnitName = unit.Name,
                HasSubscription = sub != null
            };

            if (sub != null)
            {
                var now = DateTime.UtcNow;
                bool isTrial = sub.Status == SubscriptionStatus.Trial
                               || (sub.Status == SubscriptionStatus.Expired
                                   && string.IsNullOrEmpty(sub.StripeSubscriptionId));

                var start = sub.CurrentPeriodStart != default ? sub.CurrentPeriodStart
                           : (sub.TrialStartDate ?? sub.CreatedAt);

                // Expiry shown for the unit follows the unit's OWN license, not
                // the subscription period — a license outlives a lapsed sub.
                // Use the latest ExpiryDate among the unit's active licenses;
                // fall back to the subscription period only when no license
                // carries a date (legacy rows).
                var grace = ResolveGrace(sub, gracePeriod);
                var unitLicenseExpiry = unitLicenses
                    .Where(l => l.Assignments != null
                             && l.Assignments.Any(a => a.IsActive && !a.IsDel && a.BusinessUnitId == unit.Id)
                             && l.ExpiryDate.HasValue)
                    .Select(l => l.ExpiryDate!.Value)
                    .DefaultIfEmpty(ComputeRawExpiry(sub))
                    .Max();
                var rawExpiry = unitLicenseExpiry;
                var effectiveExpiry = grace > 0 ? rawExpiry.AddDays(grace) : rawExpiry;

                var totalDuration = Math.Max(0, (int)Math.Ceiling((rawExpiry - start).TotalDays));
                bool isExpired = now >= effectiveExpiry;
                int daysRemaining = isExpired ? 0 : Math.Max(0, (int)Math.Ceiling((effectiveExpiry - now).TotalDays));

                // Displayed status reflects the unit's LICENSE state, not the
                // subscription's: a live license under a lapsed sub still reads
                // Active/Trial.
                string displayedStatus;
                if (sub.Status == SubscriptionStatus.Canceled)
                    displayedStatus = SubscriptionStatus.Canceled.ToString();
                else if (isExpired)
                    displayedStatus = SubscriptionStatus.Expired.ToString();
                else if (isTrial)
                    displayedStatus = SubscriptionStatus.Trial.ToString();
                else
                    displayedStatus = SubscriptionStatus.Active.ToString();

                dto.SubscriptionId = sub.Id;
                dto.SubscriptionType = isTrial ? "Trial" : "Paid";
                dto.Status = displayedStatus;
                dto.StartDate = start;
                dto.ExpiryDate = rawExpiry;                                     // unit's license expiry, no grace
                dto.GracePeriod = grace;                                        // assignment override else sub-level
                dto.EffectiveExpiryDate = effectiveExpiry;                      // raw + resolved grace
                dto.DaysRemaining = daysRemaining;
                dto.IsExpired = isExpired;
                dto.TotalTrialDurationDays = isTrial ? totalDuration : null;
            }

            var nowUtc = DateTime.UtcNow;
            foreach (var l in unitLicenses)
            {
                var active = l.Assignments?.FirstOrDefault(a => a.IsActive && !a.IsDel && a.BusinessUnitId == unit.Id);
                var latest = l.Assignments?
                    .Where(a => !a.IsDel && a.BusinessUnitId == unit.Id)
                    .OrderByDescending(a => a.AssignedAt)
                    .FirstOrDefault();
                dto.Licenses.Add(new SubscriptionLicenseDto
                {
                    LicenseId = l.Id,
                    LicenseKey = l.LicenseKey,
                    LicenseType = l.LicenseType,
                    CompanySubscriptionId = l.CompanySubscriptionId,
                    ExpiryDate = l.ExpiryDate,
                    IsExpired = l.ExpiryDate.HasValue && nowUtc >= l.ExpiryDate.Value,
                    AssignmentStatus = "Applied",
                    AssignedAt = active?.AssignedAt ?? latest?.AssignedAt,
                    UnassignedAt = active != null ? null : latest?.UnassignedAt
                });
            }

            dto.AppliedLicenseCount = dto.Licenses.Count;
            return dto;
        }

        // Company-wide license roll-up. Includes every license — applied
        // (with the unit it's pinned to) and unapplied. Lives once at the
        // top level so the per-unit sections don't have to repeat it.
        private static CompanyLicensePoolDto BuildCompanyLicenseSummary(List<CompanyLicense> companyLicenses)
        {
            var summary = new CompanyLicensePoolDto
            {
                TotalCount = companyLicenses.Count
            };

            var nowUtc = DateTime.UtcNow;
            foreach (var l in companyLicenses)
            {
                // An assignment pointing at a soft-deleted business unit is stale —
                // the team no longer exists, so the license is effectively free and
                // must NOT count as "Applied". Without this, a license left active
                // on a removed unit (legacy rows, or a delete predating the
                // unassign-on-delete cascade) inflates AppliedCount and makes this
                // pool disagree with the Dashboard/TeamStructure licensed-team
                // counts, which both exclude deleted units.
                var active = l.Assignments?.FirstOrDefault(a =>
                    a.IsActive && !a.IsDel && a.BusinessUnit != null && a.BusinessUnit.IsDel != true);
                var applied = active != null;
                var latest = l.Assignments?
                    .Where(a => !a.IsDel && a.BusinessUnit != null && a.BusinessUnit.IsDel != true)
                    .OrderByDescending(a => a.AssignedAt)
                    .FirstOrDefault();

                summary.Licenses.Add(new SubscriptionLicenseDto
                {
                    LicenseId = l.Id,
                    LicenseKey = l.LicenseKey,
                    LicenseType = l.LicenseType,
                    CompanySubscriptionId = l.CompanySubscriptionId,
                    ExpiryDate = l.ExpiryDate,
                    IsExpired = l.ExpiryDate.HasValue && nowUtc >= l.ExpiryDate.Value,
                    AssignmentStatus = applied ? "Applied" : "Not Applied",
                    AssignedAt = active?.AssignedAt ?? latest?.AssignedAt,
                    UnassignedAt = active != null ? null : latest?.UnassignedAt
                });
            }

            summary.AppliedCount = summary.Licenses.Count(x => x.AssignmentStatus == "Applied");
            summary.UnappliedCount = summary.TotalCount - summary.AppliedCount;
            return summary;
        }

        public async Task<List<SubscriptionStatusDto>> GetSubscriptionsForAllUnitsAsync(uint profileId)
        {
            if (profileId == 0)
                throw new InvalidOperationException("ProfileId is required.");

            var company = await _subscriptionRepo.GetCompanyByIdAsync(profileId)
                ?? throw new InvalidOperationException("Invalid ProfileId. Profile does not exist.");

            // Lazy bootstrap: a company with NO subscription (created via
            // Create-Company, never trialed) is placed on a 30-day trial here —
            // licenses are minted + assigned to every team — so it surfaces as a
            // live trial. No-op for companies that already have any subscription.
            await _legacyBackfill.EnsureTrialForCompanyAsync(profileId, AutoTrialDays);

            await AutoHealRenewedSubsAsync(profileId);

            // List the company's units strictly via active license assignments. Units
            // without a license row (or with no linked subscription on their license)
            // are excluded — the company-level CompanySubscription does NOT cascade.
            //
            // Fallback: if active-only is empty (sub expired, cascade flipped
            // assignments off), retry with the latest-per-unit query so we
            // can still emit each unit's expired-state DTO + RenewalUrl
            // instead of throwing.
            var assignments = await _subscriptionRepo.GetActiveLicenseAssignmentsByCompanyAsync(profileId);
            if (assignments == null || assignments.Count == 0)
                assignments = await _subscriptionRepo.GetLatestLicenseAssignmentsByCompanyAsync(profileId);

            var valid = assignments
                .Where(a => a.BusinessUnit != null
                         && a.CompanyLicense?.CompanySubscription != null)
                .ToList();

            // Flip status on any rows whose period has passed BEFORE building
            // the response — keeps DB and DTO in lockstep. Distinct() so we
            // don't re-save the same subscription per unit when several units
            // share one company-level sub.
            foreach (var sub in valid.Select(a => a.CompanyLicense!.CompanySubscription!).Distinct())
                await ApplyExpiryIfDueAsync(sub);

            var items = valid
                .Select(a => new
                {
                    Sub = a.CompanyLicense!.CompanySubscription!,
                    Dto = BuildStatusDto(a.CompanyLicense!.CompanySubscription!, company, a.BusinessUnit!, a.GracePeriod, a.CompanyLicense!.ExpiryDate, a.UnassignedAt)
                })
                .ToList();

            // Append complimentary beneficiaries — each rides a purchased license
            // already resolved above, so it carries the same subscription/expiry.
            var grants = await _licenseRepo.GetActiveComplimentaryGrantsByCompanyAsync(profileId);
            if (grants.Count > 0)
            {
                var representedUnitIds = new HashSet<uint>(items.Where(i => i.Dto.UnitId.HasValue).Select(i => i.Dto.UnitId!.Value));
                var primaryByLicense = valid
                    .GroupBy(a => a.CompanyLicenseId)
                    .ToDictionary(g => g.Key, g => g.First());

                var benUnitIds = grants.Select(g => g.ComplimentaryBusinessUnitId).Distinct().ToList();
                var benUnits = await _db.BusinessUnits.AsNoTracking()
                    .Where(b => benUnitIds.Contains(b.Id))
                    .ToDictionaryAsync(b => b.Id);

                foreach (var grant in grants)
                {
                    if (representedUnitIds.Contains(grant.ComplimentaryBusinessUnitId))
                        continue;
                    if (!primaryByLicense.TryGetValue(grant.CompanyLicenseId, out var primary))
                        continue;
                    if (!benUnits.TryGetValue(grant.ComplimentaryBusinessUnitId, out var benUnit))
                        continue;

                    var subC = primary.CompanyLicense!.CompanySubscription!;
                    var dtoC = BuildStatusDto(subC, company, benUnit, primary.GracePeriod, primary.CompanyLicense!.ExpiryDate, null);
                    dtoC.IsComplimentary = true;
                    items.Add(new { Sub = subC, Dto = dtoC });
                }
            }

            // Dedupe renewal-URL generation per subscription. Multiple units can
            // share one CompanySubscription, so without caching we'd hit Stripe
            // (and create dead checkout sessions) once per unit instead of once
            // per sub.
            var urlCache = new Dictionary<int, string?>();
            foreach (var item in items.Where(i => i.Dto.IsExpired))
            {
                if (!urlCache.TryGetValue(item.Sub.Id, out var url))
                {
                    url = await BuildRenewalUrlAsync(item.Sub, company);
                    urlCache[item.Sub.Id] = url;
                }
                item.Dto.RenewalUrl = url;
            }

            var result = items.Select(i => i.Dto).ToList();
            if (result.Count == 0)
                throw new InvalidOperationException("No unit subscriptions found for this company.");

            return result;
        }

        // Builds a fresh Stripe checkout URL for renewing an expired subscription.
        // Mirrors CreateCheckoutAsync's call to CreateDynamicCheckoutSessionAsync
        // (the same flow /api/subscription/checkout uses) but skips the trial
        // window — renewals never get a fresh trial. Returns null on any failure
        // (missing Stripe config, network error, etc.) so the status endpoint
        // stays responsive even if Stripe is down.
        private async Task<string?> BuildRenewalUrlAsync(CompanySubscription sub, Profile company)
        {
            if (string.IsNullOrWhiteSpace(_stripeSettings.SecretKey))
            {
                _logger.LogWarning(
                    "[RenewalUrl] Skipped — Stripe SecretKey is EMPTY. SubId={SubId} CompanyId={CompanyId}. " +
                    "Stripe keys load per-environment from Azure Key Vault (AzureKeyVault:StripeSecretNames, " +
                    "selected by Environment:Name). Verify the vault holds this environment's secrets and the " +
                    "app identity has GET access.",
                    sub.Id, company.Id);
                return null;
            }
            if (string.IsNullOrWhiteSpace(_stripeSettings.SuccessUrl) ||
                string.IsNullOrWhiteSpace(_stripeSettings.CancelUrl))
            {
                _logger.LogWarning(
                    "[RenewalUrl] Skipped — Stripe SuccessUrl/CancelUrl missing. SubId={SubId} CompanyId={CompanyId}.",
                    sub.Id, company.Id);
                return null;
            }

            try
            {
                // Prefer the stored Stripe customer id when present — keeps
                // sandbox/prod customer mapping stable. Falls back to
                // GetOrCreateCustomerAsync, which needs an email + name; we
                // pull both from the company's primary contact if loaded,
                // otherwise from any company-admin user's Username (=email).
                var customerId = sub.StripeCustomerId;
                if (string.IsNullOrWhiteSpace(customerId))
                {
                    var email = company.ContactEmailAddress;
                    if (string.IsNullOrWhiteSpace(email))
                    {
                        email = await _db.Users
                            .Where(u => u.ProfileId == (int)company.Id
                                     && u.UserGroupId == 1
                                     && !u.IsDel)
                            .Select(u => u.Username)
                            .FirstOrDefaultAsync();
                    }

                    if (string.IsNullOrWhiteSpace(email))
                    {
                        _logger.LogWarning(
                            "[RenewalUrl] Skipped — no email available. SubId={SubId} CompanyId={CompanyId}. " +
                            "No StripeCustomerId on the sub, Profile.ContactEmailAddress is null/empty, and no company-admin user " +
                            "(UserGroupId=1, !IsDel, CompanyId={CompanyId}) exists in Users table.",
                            sub.Id, company.Id, company.Id);
                        return null;
                    }

                    _logger.LogInformation(
                        "[RenewalUrl] Resolving Stripe customer. SubId={SubId} CompanyId={CompanyId} email={Email}",
                        sub.Id, company.Id, email);
                    customerId = await _stripeService.GetOrCreateCustomerAsync(
                        company.Id, email, company.ProfileName);
                }
                else
                {
                    _logger.LogInformation(
                        "[RenewalUrl] Reusing stored StripeCustomerId. SubId={SubId} CompanyId={CompanyId} cust={Cust}",
                        sub.Id, company.Id, customerId);
                }

                // Prefer the assigned plan's team tier when present — older
                // subs assigned by admins via /apply may have sub.TeamCount=1
                // even though their PlanId points at a 5/10/25-team plan.
                int teamCount = sub.TeamCount > 0 ? sub.TeamCount : 1;
                if (sub.PlanId.HasValue)
                {
                    var plan = await _planRepo.GetByIdAsync(sub.PlanId.Value);
                    if (plan != null && plan.MaxTeams > 0)
                        teamCount = plan.MaxTeams;
                }
                // Flat price per license (no plan tiers). teamCount still
                // resolves the license QUANTITY to bill on renewal.
                var pricePerTeam = _stripeSettings.PricePerLicenseMonthly;

                // A renewal opens a BRAND-NEW Stripe subscription, so the original
                // `forever` coupon does not come with it. Re-apply the company's
                // standing lifetime discount or the "forever" promise silently
                // lapses the moment a subscription expires and is repurchased.
                var renewalDiscount = await GetCompanyLifetimeDiscountAsync(company.Id);

                // Priced by the plan and version this subscription was bought on (a pre-plan
                // one: Pro version 1 at its flat first-tier price), so a renewal keeps its terms.
                var renewalPlan = await QuotePlanCheckoutAsync(sub.ServicePlanId, null, teamCount, sub.BillingCycle,
                    sub.ServicePlanVersionId, keepTerms: true, tierQuantity: sub.ServicePlanId == null ? 1 : null);

                var url = await _stripeService.CreateDynamicCheckoutSessionAsync(
                    customerId: customerId,
                    teamCount: teamCount,
                    pricePerTeamCents: pricePerTeam,
                    billingCycle: sub.BillingCycle,
                    trialDays: 0,
                    successUrl: _stripeSettings.SuccessUrl,
                    cancelUrl: _stripeSettings.CancelUrl,
                    profileId: company.Id,
                    discountPercent: renewalDiscount,
                    isLifetimeDiscount: renewalDiscount.HasValue,
                    extraMetadata: renewalPlan?.Metadata,
                    planPrice: renewalPlan?.Price);

                _logger.LogInformation(
                    "[RenewalUrl] Session created. SubId={SubId} CompanyId={CompanyId} teamCount={Tc}",
                    sub.Id, company.Id, teamCount);
                return url;
            }
            catch (StripeException sx)
            {
                // Most common: invalid API key (e.g. a stray trailing comma in
                // config) or missing capability on the test account. Surface
                // enough to debug from logs without leaking the secret itself.
                _logger.LogError(sx,
                    "[SubscriptionService] Stripe checkout session failed for SubscriptionId={SubId} CompanyId={CompanyId}. " +
                    "Code={Code} Type={Type} Message={Message}",
                    sub.Id, company.Id, sx.StripeError?.Code, sx.StripeError?.Type, sx.Message);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[SubscriptionService] Unexpected error building RenewalUrl for SubscriptionId={SubId} CompanyId={CompanyId}.",
                    sub.Id, company.Id);
                return null;
            }
        }

        private static SubscriptionStatusDto BuildStatusDto(CompanySubscription sub, Profile company, BusinessUnit? unit, int? gracePeriod, DateTime? licenseExpiry = null, DateTime? unassignedAt = null)
        {
            var now = DateTime.UtcNow;

            bool isTrial = sub.Status == SubscriptionStatus.Trial
                           || (sub.Status == SubscriptionStatus.Expired
                               && string.IsNullOrEmpty(sub.StripeSubscriptionId));

            var start = sub.CurrentPeriodStart != default ? sub.CurrentPeriodStart
                       : (sub.TrialStartDate ?? sub.CreatedAt);
            // Expiry follows the unit's OWN license when available — a license
            // outlives a lapsed subscription. Fall back to the subscription
            // period only for legacy licenses with no ExpiryDate.
            var rawExpiry = licenseExpiry ?? ComputeRawExpiry(sub);
            var grace = ResolveGrace(sub, gracePeriod);
            var effectiveExpiry = grace > 0 ? rawExpiry.AddDays(grace) : rawExpiry;

            var type = isTrial ? "Trial" : "Paid";
            var totalDuration = Math.Max(0, (int)Math.Ceiling((rawExpiry - start).TotalDays));
            bool isExpired = now >= effectiveExpiry;
            int daysRemaining = isExpired ? 0 : Math.Max(0, (int)Math.Ceiling((effectiveExpiry - now).TotalDays));

            // Displayed Status reflects the CURRENT dates, not whatever the DB
            // column happens to be. Otherwise a manual extension on an already-
            // expired sub keeps reporting "Expired" until SubscriptionLifecycleJob
            // ticks. Stored Status is left alone — the job owns DB reconciliation
            // (Status flip + license restore + notifications).
            string displayedStatus;
            if (sub.Status == SubscriptionStatus.Canceled)
                displayedStatus = SubscriptionStatus.Canceled.ToString();
            else if (isExpired)
                displayedStatus = SubscriptionStatus.Expired.ToString();
            else if (isTrial)
                displayedStatus = SubscriptionStatus.Trial.ToString();
            else
                displayedStatus = SubscriptionStatus.Active.ToString();

            return new SubscriptionStatusDto
            {
                ProfileId = company.Id,
                CompanyName = company.ProfileName,
                UnitId = unit?.Id,
                UnitName = unit?.Name,
                SubscriptionId = sub.Id,
                SubscriptionType = type,
                Status = displayedStatus,
                PlanName = sub.Plan?.Name ?? GetTierDisplayName(sub.TeamCount),
                StartDate = start,
                ExpiryDate = rawExpiry,
                GracePeriod = grace,
                EffectiveExpiryDate = effectiveExpiry,
                TotalDurationDays = totalDuration,
                DaysRemaining = daysRemaining,
                IsExpired = isExpired,
                UnassignedAt = isExpired ? unassignedAt : null
            };
        }

        public async Task<CurrentSubscriptionDto> StartTrialAsync(uint profileId, int teamCount, BillingCycle billingCycle)
        {
            if (teamCount < 1)
                throw new InvalidOperationException("Team count must be at least 1.");

            var existing = await _subscriptionRepo.GetActiveByProfileIdAsync(profileId);
            if (existing != null && existing.IsTrialUsed)
                throw new InvalidOperationException("Free trial has already been used for this company.");

            var now = DateTime.UtcNow;
            var trialEnd = now.AddDays(TrialDays);

            var subscription = new CompanySubscription
            {
                ProfileId = profileId,
                PlanId = null,
                TeamCount = teamCount,
                BillingCycle = billingCycle,
                Status = SubscriptionStatus.Trial,
                MaxTeamsAllowed = teamCount,
                MaxPeopleAllowed = teamCount * MaxPeoplePerTeam,
                TrialStartDate = now,
                TrialEndDate = trialEnd,
                CurrentPeriodStart = now,
                CurrentPeriodEnd = trialEnd,
                IsTrialUsed = true
            };

            await _subscriptionRepo.CreateAsync(subscription);

            var result = await _subscriptionRepo.GetActiveByProfileIdAsync(profileId);
            return MapToDto(result!);
        }

        public async Task<(string Email, string Name)?> GetProfileBillingContactAsync(uint profileId)
        {
            var profile = await _db.Profiles.AsNoTracking()
                .Where(p => p.Id == profileId && !p.IsDel)
                .Select(p => new { p.ProfileName })
                .FirstOrDefaultAsync();
            if (profile == null) return null;

            var profileAdmin = CloverBackoffice.Components.Enums.UserType.ProfileAdmin.ToString();
            var contact = await _db.Users.AsNoTracking()
                .Where(u => u.ProfileId == (int)profileId && !u.IsDel)
                .OrderBy(u => u.Type == profileAdmin ? 0 : 1)
                .ThenBy(u => u.UserId)
                .Select(u => new { u.Username, u.Name })
                .FirstOrDefaultAsync();

            return (contact?.Username ?? string.Empty,
                    string.IsNullOrWhiteSpace(contact?.Name) ? profile.ProfileName : contact!.Name!);
        }

        public async Task<CheckoutResponseDto> CreateCheckoutAsync(uint profileId, int teamCount, BillingCycle billingCycle, string companyEmail, string companyName, string? promoCode = null, int? servicePlanId = null, string? servicePlanCode = null)
        {
            // Fixed-price-per-license model: any positive license count is
            // allowed (no 1/5/10 plan tiers). Total = teamCount × flat price;
            // the Stripe session sets Quantity=teamCount so the charge and the
            // minted license count both follow this number.
            if (teamCount < 1)
                throw new InvalidOperationException("Team count must be a positive integer.");

            // Reject an undefined/garbage billing cycle instead of letting it
            // fall through. The Stripe interval is chosen with
            // `== Monthly ? "month" : "year"`, so ANY value that isn't exactly
            // Monthly(1) or Annual(2) — most notably the 0 default when the
            // client omits/mis-encodes the field — would otherwise be billed
            // silently as an annual (10×) subscription. Fail loudly so a bad
            // payload surfaces at checkout rather than as a wrong charge.
            if (!Enum.IsDefined(typeof(BillingCycle), billingCycle))
                throw new InvalidOperationException("Billing cycle is required and must be Monthly (1) or Annual (2).");

            // Plan checkout: priced from the plan's tier for this quantity (throws when the plan
            // can't be bought). Null = legacy flat-price checkout, unchanged.
            var planCheckout = await QuotePlanCheckoutAsync(servicePlanId, servicePlanCode, teamCount, billingCycle);

            var customerId = await _stripeService.GetOrCreateCustomerAsync(profileId, companyEmail, companyName);

            var existing = await _subscriptionRepo.GetActiveByProfileIdAsync(profileId);
            var pricePerTeam = _stripeSettings.PricePerLicenseMonthly;

            // Validate promo code if provided (applied as a Stripe coupon on the
            // checkout below). Done before the branch so both first and additional
            // purchases honor a valid code.
            decimal? discountPercent = null;
            int? promoCodeId = null;
            bool isLifetimeDiscount = false;
            if (!string.IsNullOrWhiteSpace(promoCode))
            {
                var validation = await _promoCodeService.ValidateAsync(promoCode, profileId, billingCycle);
                if (!validation.IsValid)
                    throw new InvalidOperationException(validation.Message);

                discountPercent = validation.DiscountPercent;
                isLifetimeDiscount = validation.IsLifetime;

                var promoEntity = await GetPromoCodeByCode(promoCode);
                promoCodeId = promoEntity?.Id;
            }

            // No code on this request → fall back to the company's standing
            // lifetime discount, redeemed on an earlier purchase.
            //
            // DELIBERATELY not routed through ValidateAsync: that rejects a code
            // the company has already used (HasCompanyUsedCodeAsync), which is
            // precisely the situation here. This is the SAME redemption being
            // honoured on a new Stripe subscription, not a new one — so no usage
            // is recorded and promoCodeId stays null.
            if (!discountPercent.HasValue)
            {
                var lifetimeDiscount = await GetCompanyLifetimeDiscountAsync(profileId);
                if (lifetimeDiscount.HasValue)
                {
                    discountPercent = lifetimeDiscount;
                    isLifetimeDiscount = true;
                    _logger.LogInformation(
                        "[Checkout] Applying standing lifetime discount {Pct}% for company {CompanyId} (no promo code supplied).",
                        lifetimeDiscount.Value, profileId);
                }
            }

            // ── EXISTING PAID COMPANY buying ADDITIONAL teams ──
            // Under the license-per-purchase model, a company that has ALREADY paid
            // (converted trial→paid: IsTrialUsed, Active, has a Stripe subscription)
            // coming back through Checkout is making a SEPARATE purchase. Each such
            // purchase gets its OWN recurring Stripe subscription — its own payment,
            // license batch, billing cycle and renewal — NOT an update of the
            // existing one. So we open a fresh mode=subscription checkout with NO
            // Stripe trial and DELIBERATELY do not mutate the existing paid row
            // (that row belongs to a prior purchase). The webhook
            // (HandleCheckoutSessionCompleted) creates the new CompanySubscription
            // when Stripe confirms payment. Trials and brand-new companies fall
            // through to the first-purchase path below (unchanged).
            bool isAdditionalPurchase = existing != null
                && existing.IsTrialUsed
                && existing.Status == SubscriptionStatus.Active
                && !string.IsNullOrEmpty(existing.StripeSubscriptionId);

            if (isAdditionalPurchase)
            {
                var addUrl = await _stripeService.CreateDynamicCheckoutSessionAsync(
                    customerId, teamCount, pricePerTeam, billingCycle, trialDays: 0,
                    successUrl: _stripeSettings.SuccessUrl,
                    cancelUrl: _stripeSettings.CancelUrl,
                    profileId, discountPercent, isLifetimeDiscount,
                    promoCodeText: promoCode, promoCodeId: promoCodeId,
                    extraMetadata: planCheckout?.Metadata, planPrice: planCheckout?.Price);
                return new CheckoutResponseDto { SessionUrl = addUrl };
            }

            // ── FIRST purchase / trial conversion / brand-new company (unchanged) ──
            var trialDays = (existing == null || !existing.IsTrialUsed) ? TrialDays : 0;

            // Store Stripe customer ID and promo code if applicable
            if (existing != null)
            {
                if (string.IsNullOrEmpty(existing.StripeCustomerId))
                    existing.StripeCustomerId = customerId;
                if (promoCodeId.HasValue)
                    existing.PromoCodeId = promoCodeId;
                if (isLifetimeDiscount && discountPercent.HasValue)
                    existing.LifetimeDiscountPercent = discountPercent.Value;
                await _subscriptionRepo.UpdateAsync(existing);
            }

            var sessionUrl = await _stripeService.CreateDynamicCheckoutSessionAsync(
                customerId, teamCount, pricePerTeam, billingCycle, trialDays,
                successUrl: _stripeSettings.SuccessUrl,
                cancelUrl: _stripeSettings.CancelUrl,
                profileId, discountPercent, isLifetimeDiscount,
                promoCodeText: promoCode, promoCodeId: promoCodeId,
                extraMetadata: planCheckout?.Metadata, planPrice: planCheckout?.Price);

            return new CheckoutResponseDto { SessionUrl = sessionUrl };
        }

        public async Task<CurrentSubscriptionDto> AddTeamsAsync(uint profileId, int newTeamCount)
        {
            if (newTeamCount < 1)
                throw new InvalidOperationException("Team count must be at least 1.");

            var sub = await _subscriptionRepo.GetActiveByProfileIdAsync(profileId)
                ?? throw new InvalidOperationException("No active subscription found.");

            if (newTeamCount <= sub.TeamCount)
                throw new InvalidOperationException($"New team count ({newTeamCount}) must be greater than current ({sub.TeamCount}).");

            var pricePerTeam = _stripeSettings.PricePerLicenseMonthly;

            if (!string.IsNullOrEmpty(sub.StripeSubscriptionId))
            {
                await _stripeService.UpdateSubscriptionPriceNoProrateAsync(
                    sub.StripeSubscriptionId, newTeamCount, pricePerTeam, sub.BillingCycle);
            }

            sub.TeamCount = newTeamCount;
            sub.MaxTeamsAllowed = newTeamCount;
            sub.MaxPeopleAllowed = newTeamCount * MaxPeoplePerTeam;
            sub.UpdatedAt = DateTime.UtcNow;

            await _subscriptionRepo.UpdateAsync(sub);

            var result = await _subscriptionRepo.GetActiveByProfileIdAsync(profileId);
            return MapToDto(result!);
        }

        // Additional purchase: opens a NEW recurring subscription checkout
        // (mode=subscription) for N extra teams. When Stripe confirms payment the
        // webhook creates a brand-new CompanySubscription with its own payment,
        // license batch, billing cycle and renewal (CreateAdditionalSubscriptionAsync)
        // — fully independent of the company's other subscriptions, so it can later
        // be canceled on its own. UnitId is captured for context/audit only; the
        // minted licenses land UNASSIGNED and the admin routes them via
        // /api/license-assignments afterwards.
        public async Task<PurchaseAdditionalTeamsResponseDto> PurchaseAdditionalTeamsAsync(
            PurchaseAdditionalTeamsRequestDto dto, int? actorUserId)
        {
            if (dto == null) throw new InvalidOperationException("Request body is required.");
            if (dto.ProfileId == 0) throw new InvalidOperationException("ProfileId is required.");
            if (dto.UnitId == 0) throw new InvalidOperationException("UnitId is required.");
            if (dto.AdditionalTeamsCount < 1)
                throw new InvalidOperationException("AdditionalTeamsCount must be at least 1.");

            var company = await _subscriptionRepo.GetCompanyByIdAsync(dto.ProfileId)
                ?? throw new InvalidOperationException("Invalid ProfileId. Profile does not exist.");

            // Validate the unit belongs to the company (throws on mismatch). The
            // unit is context/audit only — it does not auto-assign the licenses.
            _ = await _subscriptionRepo.GetBusinessUnitForCompanyAsync(dto.UnitId, dto.ProfileId)
                ?? throw new InvalidOperationException("Invalid UnitId. Unit does not exist for the provided Company.");

            var sub = await _subscriptionRepo.GetActiveByProfileIdAsync(dto.ProfileId)
                ?? throw new InvalidOperationException("No active subscription found for this company.");

            // An additional purchase reuses the company's Stripe customer (created
            // on its first paid checkout) — it must exist before we can open a
            // subscription checkout against it.
            if (string.IsNullOrEmpty(sub.StripeCustomerId))
                throw new InvalidOperationException(
                    "Company has no Stripe customer record. Complete the paid checkout first before purchasing additional licenses.");

            // Each additional purchase is its OWN recurring subscription. Open a
            // fresh mode=subscription checkout (no Stripe trial) for the requested
            // seats; the webhook then creates the new CompanySubscription + payment +
            // license batch. The DTO carries no cycle, so the new subscription
            // follows the company's existing billing cycle.
            var pricePerTeam = _stripeSettings.PricePerLicenseMonthly;

            // Each additional purchase is its own Stripe subscription, so the
            // lifetime coupon on the company's earlier subscription does not
            // apply to it. Carry the standing discount forward explicitly.
            var addOnDiscount = await GetCompanyLifetimeDiscountAsync(dto.ProfileId);

            // New seats: the company's plan (Pro when it has none) at its current terms.
            var addOnPlan = await QuotePlanCheckoutAsync(sub.ServicePlanId, null, dto.AdditionalTeamsCount, sub.BillingCycle);

            var url = await _stripeService.CreateDynamicCheckoutSessionAsync(
                sub.StripeCustomerId!, dto.AdditionalTeamsCount, pricePerTeam, sub.BillingCycle,
                trialDays: 0,
                successUrl: _stripeSettings.SuccessUrl,
                cancelUrl: _stripeSettings.CancelUrl,
                dto.ProfileId,
                discountPercent: addOnDiscount,
                isLifetimeDiscount: addOnDiscount.HasValue,
                extraMetadata: addOnPlan?.Metadata,
                planPrice: addOnPlan?.Price);

            return new PurchaseAdditionalTeamsResponseDto
            {
                SessionUrl = url,
                AdditionalTeamsCount = dto.AdditionalTeamsCount
            };
        }

        // Builds a one-time (mode=payment) Stripe Checkout URL that, once paid,
        // fires the op=addLicenses webhook (HandleAddLicensesPaidAsync →
        // MintAddLicensesAsync) to CREATE `additionalCount` NEW paid licenses on
        // `sub` and bump its TeamCount / MaxTeamsAllowed / MaxPeopleAllowed by
        // that many. Shared by BOTH the Additional Subscription API
        // (PurchaseAdditionalTeamsAsync) and the Checkout endpoint when an
        // already-paid company buys more teams, so the two flows create licenses
        // identically. `unitId` is context/audit only — it does NOT auto-assign;
        // the admin routes the new licenses via /api/license-assignments.
        private async Task<string> BuildAddLicensesCheckoutUrlAsync(
            CompanySubscription sub, string customerId, int additionalCount, uint? unitId, int? actorUserId,
            BillingCycle billingCycle)
        {
            if (additionalCount < 1)
                throw new InvalidOperationException("Additional team count must be at least 1.");

            // A one-time top-up only makes sense once the company has a Stripe
            // customer on file (created during its first paid checkout).
            if (string.IsNullOrEmpty(customerId))
                throw new InvalidOperationException(
                    "Company has no Stripe customer record. Complete the paid checkout first before purchasing additional licenses.");

            if (string.IsNullOrWhiteSpace(_stripeSettings.SuccessUrl) ||
                string.IsNullOrWhiteSpace(_stripeSettings.CancelUrl))
                throw new InvalidOperationException("Stripe SuccessUrl/CancelUrl not configured.");

            // Flat price per license — no plan tiers, no Plans-table lookup.
            // Total = additional licenses × per-license rate. A YEARLY add-on
            // charges the annual rate (monthly × AnnualPriceMultiplier) so it
            // costs the same as buying that seat on the primary yearly plan;
            // a MONTHLY add-on charges the flat monthly rate. Previously this
            // ignored the requested cycle entirely and always billed the flat
            // monthly amount (a yearly add-on wrongly cost one month).
            var monthlyPerTeam = _stripeSettings.PricePerLicenseMonthly;
            var pricePerTeam = billingCycle == BillingCycle.Annual
                ? monthlyPerTeam * _stripeSettings.AnnualPriceMultiplier
                : monthlyPerTeam;
            var totalDollars = pricePerTeam * additionalCount;
            var totalCents = (long)(totalDollars * 100m);

            var metadata = new Dictionary<string, string>
            {
                { "op", "addLicenses" },
                { "companyId", sub.ProfileId.ToString() },
                { "additionalTeamsCount", additionalCount.ToString() },
                { "subscriptionId", sub.Id.ToString() },
                { "pricePerTeam", pricePerTeam.ToString("0.##") },
                // Requested cycle flows back on the webhook so the mint sets the
                // license expiry to match (+1 month vs +1 year). Absent on
                // legacy sessions → mint falls back to the subscription's cycle.
                { "billingCycle", ((int)billingCycle).ToString() }
            };
            // UnitId is optional context — the webhook mints unassigned licenses
            // and tolerates its absence (TryGetValue).
            if (unitId.HasValue && unitId.Value != 0)
                metadata["unitId"] = unitId.Value.ToString();
            if (actorUserId.HasValue)
                metadata["actorUserId"] = actorUserId.Value.ToString();

            var productName = $"CloverERA additional {additionalCount} license(s)";

            var url = await _stripeService.CreateOneTimePaymentSessionAsync(
                customerId, totalCents, productName,
                _stripeSettings.SuccessUrl, _stripeSettings.CancelUrl,
                metadata);

            _logger.LogInformation(
                "[AddLicenses] Created checkout. CompanyId={C} SubId={S} UnitId={U} Add={N} PricePerTeam={P} Total={T}",
                sub.ProfileId, sub.Id, unitId, additionalCount, pricePerTeam, totalDollars);

            return url;
        }

        public async Task<CurrentSubscriptionDto> ChangeBillingCycleAsync(uint profileId, BillingCycle newCycle)
        {
            var sub = await _subscriptionRepo.GetActiveByProfileIdAsync(profileId)
                ?? throw new InvalidOperationException("No active subscription found.");

            if (sub.BillingCycle == newCycle)
                throw new InvalidOperationException("Subscription is already on this billing cycle.");

            var pricePerTeam = _stripeSettings.PricePerLicenseMonthly;

            if (!string.IsNullOrEmpty(sub.StripeSubscriptionId))
            {
                await _stripeService.UpdateSubscriptionPriceAsync(
                    sub.StripeSubscriptionId, sub.TeamCount, pricePerTeam, newCycle);
            }

            sub.BillingCycle = newCycle;
            sub.UpdatedAt = DateTime.UtcNow;
            await _subscriptionRepo.UpdateAsync(sub);

            var result = await _subscriptionRepo.GetActiveByProfileIdAsync(profileId);
            return MapToDto(result!);
        }

        // Manual recovery path for the case where Stripe completed the
        // payment (StripeCustomerId + StripeSubscriptionId are set) but the
        // dates didn't roll forward — usually because the new webhook code
        // wasn't deployed yet when the webhook arrived. Recomputes
        // CurrentPeriodEnd = now + 1 month/year based on the row's
        // BillingCycle and flips Status=Active so the license cascade
        // restores via ApplyExpiryIfDueAsync.
        public async Task<CurrentSubscriptionDto> RepairRenewalDatesAsync(uint profileId)
        {
            var sub = await _subscriptionRepo.GetActiveByProfileIdAsync(profileId)
                ?? throw new InvalidOperationException("No subscription found for this company.");

            if (string.IsNullOrEmpty(sub.StripeSubscriptionId))
                throw new InvalidOperationException(
                    "RepairRenewalDates only applies to paid subscriptions (StripeSubscriptionId is empty). " +
                    "Did the renewal webhook actually run? Check the API log for [RENEWAL-DATE-FIX v3].");

            var now = DateTime.UtcNow;
            sub.CurrentPeriodStart = now;
            sub.CurrentPeriodEnd = sub.BillingCycle == BillingCycle.Annual
                ? now.AddYears(1)
                : now.AddMonths(1);
            sub.Status = SubscriptionStatus.Active;
            sub.IsTrialUsed = true;
            sub.UpdatedAt = now;

            _logger.LogInformation(
                "[RepairRenewalDates] SubId={SubId} CompanyId={CompanyId} " +
                "CurrentPeriodStart={Start:O} CurrentPeriodEnd={End:O}",
                sub.Id, sub.ProfileId, sub.CurrentPeriodStart, sub.CurrentPeriodEnd);

            await _subscriptionRepo.UpdateAsync(sub);

            // Push licenses out to the repaired period end, then reconcile so
            // soft-deleted licenses / inactive assignments get flipped back on.
            await ExtendLicensesToAsync(sub.Id, sub.CurrentPeriodEnd, now);
            await ApplyExpiryIfDueAsync(sub);

            var result = await _subscriptionRepo.GetActiveByProfileIdAsync(profileId);
            return MapToDto(result!);
        }

        public async Task HandleWebhookEventAsync(object stripeEvent)
        {
            if (stripeEvent is not Stripe.Event ev) return;

            _logger.LogInformation("Stripe webhook received: {EventType}, EventId: {EventId}", ev.Type, ev.Id);

            if (!await IsEventForThisEnvironmentAsync(ev))
            {
                _logger.LogInformation(
                    "Stripe webhook {EventType} {EventId} ignored — it belongs to another environment (expected rridaEnv={Tag}).",
                    ev.Type, ev.Id, _stripeSettings.EnvironmentTag);
                return;
            }

            switch (ev.Type)
            {
                case "checkout.session.completed":
                    await HandleCheckoutSessionCompleted(ev);
                    break;

                case "invoice.paid":
                case "invoice_payment.paid":
                    await HandleInvoicePaid(ev);
                    break;

                case "customer.subscription.updated":
                    await HandleSubscriptionUpdated(ev);
                    break;

                case "customer.subscription.deleted":
                    await HandleSubscriptionDeleted(ev);
                    break;

                default:
                    _logger.LogInformation("Unhandled Stripe event type: {EventType}", ev.Type);
                    break;
            }
        }

        // Stripe API ≥2025-04-30 moved current_period_start/end from the
        // Subscription onto items[].data[]. Stripe.NET 47's SDK model still
        // exposes the deprecated top-level DateTime properties but they
        // deserialize to 1970-01-01 (UnixEpoch) because the API no longer
        // sends those fields. This helper extracts the authoritative values
        // from the raw JSON on the webhook event. Any value ≤ year 2000 is
        // treated as the epoch sentinel and returned as null so callers can
        // decide whether to fall back or skip the write entirely.
        private static (DateTime? Start, DateTime? End) TryReadStripeItemPeriod(Stripe.Event ev)
        {
            try
            {
                var itemNode = (ev.Data.RawObject as Newtonsoft.Json.Linq.JObject)?
                    ["items"]?["data"]?[0];
                var unixStart = itemNode?["current_period_start"]?.ToObject<long?>();
                var unixEnd = itemNode?["current_period_end"]?.ToObject<long?>();

                DateTime? start = unixStart.HasValue && unixStart.Value > 946684800
                    ? DateTimeOffset.FromUnixTimeSeconds(unixStart.Value).UtcDateTime
                    : (DateTime?)null;
                DateTime? end = unixEnd.HasValue && unixEnd.Value > 946684800
                    ? DateTimeOffset.FromUnixTimeSeconds(unixEnd.Value).UtcDateTime
                    : (DateTime?)null;
                return (start, end);
            }
            catch
            {
                return (null, null);
            }
        }

        // Reads the paid seat quantity from items[0] in the raw webhook JSON.
        // Same rationale as TryReadStripeItemPeriod: the SDK model can mis-map
        // fields under a mismatched API version (2026-01-28.clover), so the raw
        // JSON is the authoritative source. Returns null when it can't be read.
        private static int? TryReadStripeItemQuantity(Stripe.Event ev)
        {
            try
            {
                var itemNode = (ev.Data.RawObject as Newtonsoft.Json.Linq.JObject)?
                    ["items"]?["data"]?[0];
                return itemNode?["quantity"]?.ToObject<int?>();
            }
            catch
            {
                return null;
            }
        }

        private async Task HandleCheckoutSessionCompleted(Stripe.Event ev)
        {
            var session = ev.Data.Object as Session;
            if (session == null) return;

            _logger.LogInformation("checkout.session.completed - SessionId: {SessionId}, CustomerId: {CustomerId}, SubscriptionId: {SubscriptionId}",
                session.Id, session.CustomerId, session.SubscriptionId);

            // "licencePlanChange" = one licence moving to another billing cycle
            // (POST /api/license/change). Must run before the additional-purchase
            // detection below, which would otherwise mint a NEW licence for it.
            if (IsLicencePlanChange(session.Metadata))
            {
                await HandleLicencePlanChangePaidAsync(session);
                return;
            }

            // Branch on metadata.op so one webhook handler can serve multiple
            // checkout flows. "addLicenses" = one-time top-up; everything else
            // falls through to the original subscription-checkout logic.
            if (session.Metadata != null &&
                session.Metadata.TryGetValue("op", out var op) &&
                string.Equals(op, "addLicenses", StringComparison.OrdinalIgnoreCase))
            {
                await HandleAddLicensesPaidAsync(session);
                return;
            }

            // "productPurchase" = checkout started from the Stripe product
            // listing page. Runs the same downstream business flow as a paid
            // subscription conversion (subscription row + plan + licenses +
            // unit assignment).
            if (session.Metadata != null &&
                session.Metadata.TryGetValue("op", out var opPp) &&
                string.Equals(opPp, "productPurchase", StringComparison.OrdinalIgnoreCase))
            {
                await HandleProductPurchasePaidAsync(session);
                return;
            }

            if (!session.Metadata.TryGetValue("companyId", out var profileIdStr))
            {
                _logger.LogWarning("checkout.session.completed - No companyId in metadata, skipping.");
                return;
            }
            if (!uint.TryParse(profileIdStr, out var profileId)) return;

            session.Metadata.TryGetValue("teamCount", out var teamCountStr);
            var teamCount = int.TryParse(teamCountStr, out var tc) ? tc : 1;

            // Billing cycle actually purchased. Prefer the cycle stamped in the
            // checkout metadata (our intent), fall back to the Stripe
            // subscription's real recurring interval (authoritative — what Stripe
            // will charge on renewal), and only then to Monthly. Everything below
            // — the sub's BillingCycle, its period end, and therefore the minted
            // licenses' expiry — keys off this, so a yearly checkout no longer
            // gets provisioned as monthly.
            var purchasedCycle =
                ParseBillingCycleMetadata(session.Metadata)
                ?? await _stripeService.GetSubscriptionBillingCycleAsync(session.SubscriptionId)
                ?? BillingCycle.Monthly;

            _logger.LogInformation(
                "checkout.session.completed - Resolved purchasedCycle={Cycle} for company {CompanyId} (subId={SubId})",
                purchasedCycle, profileId, session.SubscriptionId);

            // Plan catalogue plan bought (plan checkout); null for a legacy checkout. Stamped on
            // the subscription below, and from it on every licence minted / converted for it.
            var servicePlanId = await ExistingServicePlanIdAsync(ParseServicePlanIdMetadata(session.Metadata));
            var servicePlanUnitPrice = ParseServicePlanUnitPriceMetadata(session.Metadata);
            var servicePlanVersionId = await ExistingServicePlanVersionIdAsync(session.Metadata, servicePlanId);

            var existing = await _subscriptionRepo.GetActiveByProfileIdAsync(profileId);

            // ── ADDITIONAL PURCHASE → its OWN recurring subscription ──
            // Under the license-per-purchase model, every purchase AFTER the first
            // creates a brand-new CompanySubscription (its own Stripe subscription,
            // payment, license batch, billing cycle and renewal). We detect it by
            // the company already owning a DIFFERENT paid Stripe subscription — a
            // check that's independent of which row happens to be "newest", so it's
            // race-safe. A webhook re-delivery is made idempotent inside the helper
            // (keyed on this Stripe subscription id). The company's other
            // subscriptions and all their licenses are left completely untouched, so
            // this purchase — and any later cancellation of it — only affects itself.
            bool isAdditionalPurchase = session.SubscriptionId != null
                && await _db.CompanySubscriptions.AnyAsync(s =>
                    s.ProfileId == profileId
                    && !s.IsDel
                    && s.StripeSubscriptionId != null
                    && s.StripeSubscriptionId != session.SubscriptionId);

            if (isAdditionalPurchase)
            {
                await CreateAdditionalSubscriptionAsync(session, profileId, teamCount, purchasedCycle);
                return;
            }

            if (existing != null)
            {
                var now = DateTime.UtcNow;

                // First paid conversion vs. replay. Genuine additional purchases
                // were already peeled off above (isAdditionalPurchase), so on
                // reaching here `alreadyPaid` is true ONLY on a re-delivery of the
                // FIRST checkout we already converted — it drives the idempotent
                // replay handling further down (keep the already-bumped TeamCount).
                bool alreadyPaid = existing.IsTrialUsed
                    && existing.Status == SubscriptionStatus.Active
                    && !string.IsNullOrEmpty(existing.StripeSubscriptionId);

                // ── FIRST paid conversion: GROW the pool by the purchased count ──
                existing.StripeCustomerId = session.CustomerId;
                existing.StripeSubscriptionId = session.SubscriptionId;
                // Capture the Stripe Product ID (prod_...) behind this
                // subscription so it's persisted on the company subscription row.
                // Non-critical enrichment — a failure here must NEVER abort the
                // license provisioning below, so it's isolated in its own try.
                if (session.SubscriptionId != null)
                {
                    try
                    {
                        existing.StripeProductId = await _stripeService.GetProductIdForSubscriptionAsync(session.SubscriptionId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "checkout.session.completed - GetProductIdForSubscriptionAsync failed for {SubId}; continuing to provision licenses.",
                            session.SubscriptionId);
                    }
                }
                // ADDITIVE: the purchased quantity is how many licenses the
                // company is BUYING, added on top of the seats it already holds
                // (its free trial pool). The old code SET TeamCount to the raw
                // purchased count, so any purchase <= the current pool minted
                // nothing (MintMissing sees a non-positive shortfall) and the
                // visible count never grew — the "buy 1 does nothing, buy 10
                // works" bug, whose threshold was exactly the trial license count.
                //
                // Idempotency without a transaction: `alreadyPaid` (computed above
                // from the ORIGINAL row) is true in THIS conversion branch only on
                // a re-delivery of a checkout we already converted — a genuine
                // add-on carries a NEW Stripe subscription id and is peeled off by
                // the isAddOn branch, so reaching here with alreadyPaid == true
                // means the SAME subscription id was redelivered. On replay we must
                // NOT add the seats again: keep the already-bumped TeamCount and
                // let the idempotent MintMissing below reconcile the pool (which
                // also self-heals a first delivery that failed mid-mint). On the
                // first delivery we add the purchased seats to the live pool.
                int newTeamCount;
                if (alreadyPaid)
                {
                    newTeamCount = existing.TeamCount;
                }
                else
                {
                    var existingPoolCount = await _db.CompanyLicenses
                        .CountAsync(l => l.CompanySubscriptionId == existing.Id && !l.IsDel);
                    newTeamCount = existingPoolCount + teamCount;
                }

                existing.TeamCount = newTeamCount;
                existing.MaxTeamsAllowed = newTeamCount;
                existing.MaxPeopleAllowed = newTeamCount * MaxPeoplePerTeam;
                existing.Status = session.SubscriptionId != null ? SubscriptionStatus.Active : SubscriptionStatus.Trial;
                if (session.SubscriptionId != null)
                    existing.IsTrialUsed = true;

                // Stamp the cycle actually purchased BEFORE the period-end is
                // computed below. The trial row was created Monthly by default;
                // without this a yearly conversion kept Monthly, so CurrentPeriodEnd
                // (and every license extended to it) got +1 month instead of +1 year.
                existing.BillingCycle = purchasedCycle;

                // Re-point the sub at the plan matching the NEW total team count
                // (existing pool + purchased). Map by Plans.MaxTeams ==
                // newTeamCount — the same lookup AccountRegistration uses when it
                // first stamps PlanId — so the sub stops reflecting the old trial
                // plan. Falls back to the current PlanId when no matching active
                // plan exists (expected under the flat per-license model, which
                // has no fixed team tiers).
                var paidPlan = await _db.Plans
                    .Where(p => p.IsActive && p.MaxTeams == newTeamCount)
                    .OrderBy(p => p.SortOrder)
                    .FirstOrDefaultAsync();
                if (paidPlan != null)
                    existing.PlanId = paidPlan.Id;
                else
                    _logger.LogWarning(
                        "[CheckoutUpgrade] No active plan with MaxTeams={T}; PlanId left at {P}. SubId={S}",
                        newTeamCount, existing.PlanId, existing.Id);
                if (servicePlanId.HasValue)
                {
                    existing.ServicePlanId = servicePlanId;
                    existing.ServicePlanVersionId = servicePlanVersionId;
                    existing.ServicePlanUnitPrice = servicePlanUnitPrice;
                }

                // Renewal: set BOTH period dates. The completed checkout
                // represents the start of a fresh billing period — Stripe's
                // upcoming customer.subscription.updated event will overwrite
                // these with the Stripe-authoritative values from items[0]
                // (see HandleSubscriptionUpdated), but writing our own
                // computed values here guarantees the renewed window is
                // never left stuck at the pre-expiry CurrentPeriodEnd or at
                // the epoch sentinel (1970-01-01) when Stripe omits the
                // deprecated subscription-level fields.
                //
                // Must happen BEFORE ApplyExpiryIfDueAsync — otherwise the
                // old (past) CurrentPeriodEnd makes the helper re-fire the
                // expire cascade on a sub we're trying to restore.
                if (session.SubscriptionId != null)
                {
                    existing.CurrentPeriodStart = now;
                    existing.CurrentPeriodEnd = existing.BillingCycle == BillingCycle.Annual
                        ? now.AddYears(1)
                        : now.AddMonths(1);

                    _logger.LogInformation(
                        "[RENEWAL-DATE-FIX v3] SubId={SubId} BillingCycle={BC} " +
                        "NewCurrentPeriodStart={Start:O} NewCurrentPeriodEnd={End:O}",
                        existing.Id, existing.BillingCycle,
                        existing.CurrentPeriodStart, existing.CurrentPeriodEnd);
                }

                existing.UpdatedAt = now;
                await _subscriptionRepo.UpdateAsync(existing);

                // Paid seats are MINTED, never converted from the trial pool.
                // Only fires when Stripe actually created a subscription
                // (session.SubscriptionId != null) — the same condition that
                // flips the sub's own Status to Active above.
                if (session.SubscriptionId != null)
                {
                    // Seamless post-trial-expiry conversion. If the company's trial
                    // ALREADY LAPSED before it paid, its trial licenses were
                    // soft-deleted and their teams left unlicensed. Upgrade those
                    // EXPIRED trial licenses in place — revive, retype Paid, re-key
                    // CLD-TRI→CLD-PRO, extend to the new paid period, and reopen the
                    // team's assignment — up to the purchased count, BEFORE minting.
                    // The converted seats then count toward the pool so MintMissing
                    // only tops up the remainder (total = purchased count) and the
                    // previously-trialing teams keep their license with no manual
                    // reassignment.
                    //
                    // Self-gating: it only touches EXPIRED trial licenses, so a
                    // still-active trial (a purchase made DURING the trial) has none
                    // to convert and this is a no-op — trial and paid stay
                    // independent during the trial, and those live trial licenses
                    // keep their Trial type / TRI key / own expiry and lapse on their
                    // own date, exactly as before.
                    await ConvertExpiredTrialLicensesToPaidAsync(existing, existing.TeamCount, existing.CurrentPeriodEnd, now);

                    // Mint any remaining purchased seats as NEW Paid rows carrying
                    // their own start/expiry. New rows inherit the (now updated)
                    // sub.PlanId and sit UNASSIGNED until an admin applies them.
                    await MintMissingPaidLicensesAsync(existing, now);

                    // Renewal: push every live PAID seat out to the new paid period
                    // end so paid licenses don't lapse mid-subscription. Skips trial
                    // seats (see ExtendLicensesToAsync).
                    await ExtendLicensesToAsync(existing.Id, existing.CurrentPeriodEnd, now);
                }

                // Restore licenses/assignments inline so the company sees
                // their unit re-activated the moment Stripe confirms payment.
                await ApplyExpiryIfDueAsync(existing);

                // Record promo code usage after successful payment
                if (existing.PromoCodeId.HasValue)
                {
                    await _promoCodeService.RecordUsageAsync(existing.PromoCodeId.Value, profileId, existing.Id);
                }

                _logger.LogInformation("checkout.session.completed - Updated subscription {SubId} for company {CompanyId}", existing.Id, profileId);
            }
            else
            {
                var now = DateTime.UtcNow;

                // Map the paid team count to a Plan row (same lookup the paid
                // branch uses) so a brand-new paid company doesn't land on a
                // null plan.
                var paidPlan = await _db.Plans
                    .Where(p => p.IsActive && p.MaxTeams == teamCount)
                    .OrderBy(p => p.SortOrder)
                    .FirstOrDefaultAsync();

                // Same race as an additional purchase: customer.subscription.updated
                // may self-heal this Stripe subscription concurrently. Serialize on
                // its id and skip the insert when the other handler already did it.
                // Disposing without commit rolls back on an exception.
                await using (var newTx = await _db.Database.BeginTransactionAsync())
                {
                    CompanySubscription? provisioned = null;
                    if (session.SubscriptionId != null)
                    {
                        await LockStripeSubscriptionAsync(session.SubscriptionId);
                        provisioned = await _subscriptionRepo.GetByStripeSubscriptionIdAsync(session.SubscriptionId);
                    }

                    if (provisioned == null)
                    {
                        var sub = new CompanySubscription
                        {
                            ProfileId = profileId,
                            PlanId = paidPlan?.Id,
                            ServicePlanId = servicePlanId,
                            ServicePlanVersionId = servicePlanVersionId,
                            ServicePlanUnitPrice = servicePlanUnitPrice,
                            TeamCount = teamCount,
                            // Honor the cycle actually purchased (was hardcoded Monthly),
                            // so a brand-new yearly company gets Annual + a one-year period.
                            BillingCycle = purchasedCycle,
                            Status = SubscriptionStatus.Active,
                            StripeCustomerId = session.CustomerId,
                            StripeSubscriptionId = session.SubscriptionId,
                            MaxTeamsAllowed = teamCount,
                            MaxPeopleAllowed = teamCount * MaxPeoplePerTeam,
                            IsTrialUsed = true,
                            CurrentPeriodStart = now,
                            CurrentPeriodEnd = purchasedCycle == BillingCycle.Annual
                                ? now.AddYears(1)
                                : now.AddMonths(1)
                        };
                        await _subscriptionRepo.CreateAsync(sub);

                        // A company with no prior (trial) subscription has no license
                        // pool to promote — mint the paid licenses here so the pool
                        // matches the paid TeamCount instead of being left empty.
                        // Idempotent: MintMissingPaidLicensesAsync only tops up the gap.
                        if (session.SubscriptionId != null)
                            await MintMissingPaidLicensesAsync(sub, now);
                    }

                    await newTx.CommitAsync();
                }

                _logger.LogInformation("checkout.session.completed - Created new subscription for company {CompanyId}", profileId);
            }

            // Seed the paid Payment row the moment checkout succeeds. Leaving
            // this entirely to invoice.paid was fragile: under the Basil API
            // that event can't resolve the PaymentIntent (Invoice.payment_intent
            // is gone) and, in some setups, the row's invoice/card details never
            // landed. The checkout session reliably carries its own invoice id
            // (session.InvoiceId) plus the subscription/customer needed to read
            // the card on file, so we persist those here. invoice.paid /
            // invoice_payment.paid later backfill the PaymentIntent id. The
            // upsert is keyed on invoice id, so no duplicate row is created.
            if (session.SubscriptionId != null)
            {
                var paidSub = await _subscriptionRepo.GetActiveByProfileIdAsync(profileId);
                if (paidSub != null)
                {
                    var card = await _stripeService.GetCardUsedAsync(
                        session.PaymentIntentId, session.InvoiceId, session.SubscriptionId, session.CustomerId);

                    var amount = session.AmountTotal.HasValue ? session.AmountTotal.Value / 100m : 0m;
                    var (subtotal, discount) = ReadSessionDiscount(session);
                    var discountCode = discount.HasValue
                        ? await ResolveDiscountCodeAsync(session, ParsePromoCodeIdMetadata(session.Metadata))
                        : null;

                    await UpsertSubscriptionPaymentAsync(
                        profileId,
                        paidSub.Id,
                        session.InvoiceId,
                        session.PaymentIntentId,
                        amount,
                        (session.Currency ?? "usd").ToLowerInvariant(),
                        card,
                        $"Subscription payment - {paidSub.TeamCount} team(s)",
                        subtotal,
                        discount,
                        discountCode);

                    _logger.LogInformation(
                        "checkout.session.completed - Payment row seeded for company {CompanyId}, invoice {InvoiceId}, card {Brand} ****{Last4}",
                        profileId, session.InvoiceId, card.Brand ?? "(none)", card.Last4 ?? "----");
                }
            }
        }

        // Provisions an ADDITIONAL purchase as its OWN recurring subscription:
        // creates a brand-new CompanySubscription row for this Stripe subscription,
        // mints exactly `teamCount` paid licenses on it (each expiring at the new
        // sub's period end, so they renew with it), and seeds the Payment row — all
        // in one transaction. The company's other subscriptions and their licenses
        // are never read or written here, so this purchase is fully independent and
        // can later be canceled on its own SubscriptionId without side effects.
        //
        // Idempotent on the Stripe subscription id: a webhook re-delivery finds the
        // row already created and only re-seeds the payment, never a duplicate
        // subscription or license batch. The upcoming customer.subscription.updated
        // event for this Stripe subscription later overwrites the period dates with
        // Stripe's authoritative values (same as the first-purchase path).
        private async Task CreateAdditionalSubscriptionAsync(
            Session session, uint profileId, int teamCount, BillingCycle purchasedCycle)
        {
            var now = DateTime.UtcNow;

            var card = await _stripeService.GetCardUsedAsync(
                session.PaymentIntentId, session.InvoiceId, session.SubscriptionId, session.CustomerId);
            var amount = session.AmountTotal.HasValue ? session.AmountTotal.Value / 100m : 0m;
            var (sessionSubtotal, sessionDiscount) = ReadSessionDiscount(session);

            // Promo redemption belonging to THIS purchase, if any. The id travels
            // on the session metadata because under subscription-per-purchase the
            // CompanySubscription being created here did not exist at checkout
            // time — there was no row to stamp PromoCodeId on.
            //
            // Resolved up here, ahead of the re-delivery guard, because the
            // guard's payment upsert needs the code too — a re-delivered webhook
            // must not leave the discount unattributed.
            var redeemedPromoCodeId = ParsePromoCodeIdMetadata(session.Metadata);
            var sessionDiscountCode = sessionDiscount.HasValue
                ? await ResolveDiscountCodeAsync(session, redeemedPromoCodeId)
                : null;

            // Re-delivery guard: this Stripe subscription is already provisioned.
            var dupSub = await _subscriptionRepo.GetByStripeSubscriptionIdAsync(session.SubscriptionId!);
            if (dupSub != null)
            {
                await UpsertSubscriptionPaymentAsync(
                    profileId, dupSub.Id, session.InvoiceId, session.PaymentIntentId,
                    amount, (session.Currency ?? "usd").ToLowerInvariant(), card,
                    $"Subscription payment - {dupSub.TeamCount} team(s)",
                    sessionSubtotal, sessionDiscount, sessionDiscountCode);
                _logger.LogInformation(
                    "checkout.session.completed - Additional purchase for StripeSub {S} already provisioned (SubId={Id}); re-seeded payment only.",
                    session.SubscriptionId, dupSub.Id);
                return;
            }

            // Plan matching this batch size — same lookup the first conversion uses.
            // Null is expected under the flat per-license model (no fixed tiers).
            var addonPlan = await _db.Plans
                .Where(p => p.IsActive && p.MaxTeams == teamCount)
                .OrderBy(p => p.SortOrder)
                .FirstOrDefaultAsync();

            // Best-effort Stripe product id — a failure must never abort provisioning.
            string? addonProductId = null;
            try { addonProductId = await _stripeService.GetProductIdForSubscriptionAsync(session.SubscriptionId!); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "checkout.session.completed - GetProductIdForSubscriptionAsync failed for {SubId}; continuing to provision the new subscription.",
                    session.SubscriptionId);
            }

            decimal? lifetimePercent;
            if (redeemedPromoCodeId.HasValue)
            {
                var redeemed = await _promoCodeRepo.GetByIdAsync(redeemedPromoCodeId.Value);
                lifetimePercent = redeemed is { IsLifetime: true }
                    ? redeemed.DiscountPercent
                    : null;
            }
            else
            {
                // No new redemption on this purchase — inherit the company's
                // standing lifetime discount so this row is itself a valid source
                // for the NEXT purchase's company-wide lookup.
                lifetimePercent = await GetCompanyLifetimeDiscountAsync(profileId);
            }

            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                // customer.subscription.updated (self-heal) may be provisioning this
                // same Stripe subscription right now. Serialize on its id and re-check
                // under the lock: the guard above runs before this transaction and
                // cannot see the other handler's uncommitted row.
                await LockStripeSubscriptionAsync(session.SubscriptionId!);
                var provisioned = await _subscriptionRepo.GetByStripeSubscriptionIdAsync(session.SubscriptionId!);
                if (provisioned != null)
                {
                    await UpsertSubscriptionPaymentAsync(
                        profileId, provisioned.Id, session.InvoiceId, session.PaymentIntentId,
                        amount, (session.Currency ?? "usd").ToLowerInvariant(), card,
                        $"Subscription payment - {provisioned.TeamCount} team(s)",
                        sessionSubtotal, sessionDiscount, sessionDiscountCode);
                    await tx.CommitAsync();
                    _logger.LogInformation(
                        "checkout.session.completed - Additional purchase for StripeSub {S} provisioned concurrently (SubId={Id}); payment only.",
                        session.SubscriptionId, provisioned.Id);
                    return;
                }

                var newSub = new CompanySubscription
                {
                    ProfileId = profileId,
                    PlanId = addonPlan?.Id,
                    ServicePlanId = await ExistingServicePlanIdAsync(ParseServicePlanIdMetadata(session.Metadata)),
                    ServicePlanVersionId = await ExistingServicePlanVersionIdAsync(session.Metadata, ParseServicePlanIdMetadata(session.Metadata)),
                    ServicePlanUnitPrice = ParseServicePlanUnitPriceMetadata(session.Metadata),
                    TeamCount = teamCount,
                    MaxTeamsAllowed = teamCount,
                    MaxPeopleAllowed = teamCount * MaxPeoplePerTeam,
                    BillingCycle = purchasedCycle,
                    Status = SubscriptionStatus.Active,
                    StripeCustomerId = session.CustomerId,
                    StripeSubscriptionId = session.SubscriptionId,
                    StripeProductId = addonProductId,
                    IsTrialUsed = true,
                    PromoCodeId = redeemedPromoCodeId,
                    LifetimeDiscountPercent = lifetimePercent,
                    CurrentPeriodStart = now,
                    CurrentPeriodEnd = purchasedCycle == BillingCycle.Annual
                        ? now.AddYears(1)
                        : now.AddMonths(1)
                };
                await _subscriptionRepo.CreateAsync(newSub);

                // Record the redemption against the row it actually belongs to.
                // Without this an additional purchase never increments
                // CurrentUsageCount, so MaxUsageCount goes unenforced and a
                // single-use code can be redeemed by every later purchase.
                if (redeemedPromoCodeId.HasValue)
                    await _promoCodeService.RecordUsageAsync(redeemedPromoCodeId.Value, profileId, newSub.Id);

                // Pool is empty on the new sub, so this mints exactly `teamCount`
                // paid rows, each expiring at the new sub's period end.
                await MintMissingPaidLicensesAsync(newSub, now);

                await UpsertSubscriptionPaymentAsync(
                    profileId, newSub.Id, session.InvoiceId, session.PaymentIntentId,
                    amount, (session.Currency ?? "usd").ToLowerInvariant(), card,
                    $"Subscription payment - {teamCount} team(s)",
                    sessionSubtotal, sessionDiscount, sessionDiscountCode);

                await tx.CommitAsync();

                _logger.LogInformation(
                    "checkout.session.completed - Created NEW subscription {SubId} (StripeSub {S}) for company {CompanyId}: {N} team(s), cycle {Cycle}.",
                    newSub.Id, session.SubscriptionId, profileId, teamCount, purchasedCycle);
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        // Trial-licenses-to-paid promotion.
        //
        // DELIBERATELY UNCALLED. Trial and paid seats are now independent: a
        // trial license keeps its own window, stays on its team and lapses on its
        // own date, while a purchase mints separate paid rows. Nothing converts a
        // trial seat automatically — not checkout, not an add-on purchase, not a
        // subscription update. Kept as the promotion primitive (collision-safe
        // TRI→PRO rename + audit rows) in case an explicit, operator-driven
        // promotion is wired up later; wire it to a deliberate action, never back
        // into a webhook.
        //
        // When invoked, every CompanyLicense row attached to the subscription is
        // upgraded:
        //   * LicenseType "Trial" → "Paid"
        //   * LicenseKey prefix CLD-TRI… → CLD-PRO… (consistent with the
        //     minting code in HandleAddLicensesPaidAsync / trial setup)
        // One UPDATE audit row is written per upgraded license with a
        // monotonically-increasing VersionNumber (so the next edit reads
        // 1 → 2 instead of two 1s). Rows already marked Paid are skipped,
        // so re-firing the webhook is safe.
        private async Task UpgradeTrialLicensesToPaidAsync(int subscriptionId, DateTime now, int? actorUserId = null, int? newPlanId = null, DateTime? paidExpiry = null)
        {
            var trialLicenses = await _db.CompanyLicenses
                .Where(l => l.CompanySubscriptionId == subscriptionId
                         && l.LicenseType == "Trial"
                         && !l.IsDel)
                .ToListAsync();

            // The subscription's plan catalogue plan (set from the plan checkout); the promoted
            // seats give their managers that plan. FindAsync sees an unsaved value on the
            // tracked subscription too.
            var planSub = await _db.CompanySubscriptions.FindAsync(subscriptionId);
            var servicePlanId = planSub?.ServicePlanId;
            var servicePlanVersionId = planSub?.ServicePlanVersionId;

            if (trialLicenses.Count == 0)
            {
                _logger.LogInformation(
                    "[TrialUpgrade] No trial licenses on SubId={SubId}; skipping.", subscriptionId);
                return;
            }

            // A subscription belongs to exactly one company; all its trial rows
            // share that ProfileId. Load every key the company already holds
            // (soft-deleted included) so a TRI→PRO rename never collides with an
            // occupied PRO key on the unique LicenseKey index — which would throw
            // and abort the webhook. On collision, fall back to a fresh sequence.
            var promoProfileId = trialLicenses[0].ProfileId;
            var existingKeys = new HashSet<string>(await _db.CompanyLicenses
                .Where(l => l.ProfileId == promoProfileId && l.LicenseKey != null)
                .Select(l => l.LicenseKey!)
                .ToListAsync());
            var nextSeq = await NextLicenseSeqAsync(promoProfileId);

            foreach (var lic in trialLicenses)
            {
                lic.LicenseType = "Paid";
                if (!string.IsNullOrEmpty(lic.LicenseKey) && lic.LicenseKey.Contains("-TRI"))
                {
                    var candidate = lic.LicenseKey.Replace("-TRI", "-PRO");
                    // If that PRO key is already taken (e.g. by a soft-deleted
                    // row), assign the next free sequence instead.
                    if (existingKeys.Contains(candidate))
                        candidate = $"CLD-PRO{nextSeq++:000}-{promoProfileId}";
                    existingKeys.Add(candidate);
                    lic.LicenseKey = candidate;
                }
                // Re-point promoted rows at the paid plan when one was supplied,
                // so they stop carrying the old trial PlanId.
                if (newPlanId.HasValue) lic.PlanId = newPlanId;
                if (servicePlanId.HasValue)
                {
                    lic.ServicePlanId = servicePlanId;
                    lic.ServicePlanVersionId = servicePlanVersionId;
                }
                // On trial→paid conversion extend the seat to the paid period
                // end (only forward, never shorten a longer trial window).
                if (paidExpiry.HasValue && (!lic.ExpiryDate.HasValue || lic.ExpiryDate.Value < paidExpiry.Value))
                    lic.ExpiryDate = paidExpiry;
                lic.UpdatedAt = now;
                if (actorUserId.HasValue) lic.UpdatedBy = actorUserId;
            }
            await _db.SaveChangesAsync();

            // Audit: one UPDATE per license, VersionNumber = max(existing) + 1
            // so the row reads 1 (INSERT) → 2 (UPDATE) → … chronologically.
            var ids = trialLicenses.Select(l => l.Id).ToList();
            var maxVersions = await _db.CompanyLicenseAuditLogs
                .Where(a => ids.Contains(a.Id))
                .GroupBy(a => a.Id)
                .Select(g => new { Id = g.Key, Max = g.Max(a => a.VersionNumber) })
                .ToDictionaryAsync(x => x.Id, x => x.Max);

            _db.CompanyLicenseAuditLogs.AddRange(trialLicenses.Select(l => new CompanyLicenseAuditLog
            {
                Id = l.Id,
                ProfileId = l.ProfileId,
                LicenseKey = l.LicenseKey,
                LicenseType = l.LicenseType,
                CompanySubscriptionId = l.CompanySubscriptionId,
                PlanId = l.PlanId,
                ExpiryDate = l.ExpiryDate,
                CreatedBy = l.CreatedBy,
                UpdatedBy = l.UpdatedBy,
                CreatedAt = l.CreatedAt,
                UpdatedAt = l.UpdatedAt,
                IsDel = l.IsDel,
                LoggedAt = now,
                VersionNumber = (maxVersions.TryGetValue(l.Id, out var v) ? v : 0) + 1,
                OperationType = "UPDATE"
            }));
            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "[TrialUpgrade] Promoted {Count} licenses Trial→Paid on SubId={SubId}.",
                trialLicenses.Count, subscriptionId);
        }

        // Seamless post-trial-expiry conversion (see the call site in
        // HandleCheckoutSessionCompleted). When a company pays AFTER its trial has
        // lapsed, its EXPIRED trial licenses (soft-deleted on expiry, their teams
        // unlicensed) are upgraded IN PLACE to paid instead of being orphaned:
        //   * revived (IsDel=false) and retyped "Paid"
        //   * re-keyed CLD-TRI…→CLD-PRO… with a FRESH collision-proof sequence
        //     (NextLicenseSeqAsync spans TRI+PRO incl. soft-deleted, so the new key
        //     can never hit the unique LicenseKey index)
        //   * ExpiryDate set to the new paid period end (renews with the sub)
        //   * their most-recent assignment reopened so the team is re-licensed with
        //     NO manual reassignment (safe against the (CompanyLicenseId,IsActive)
        //     unique index — the expiry cascade left these with no active assignment)
        //
        // Only EXPIRED trial licenses are touched (IsDel OR ExpiryDate<=now) and
        // only up to `maxConvert` of them, so:
        //   * a purchase made DURING an active trial finds nothing expired → no-op,
        //     leaving the live trial licenses independent (as required);
        //   * a webhook re-delivery finds the rows already "Paid" (not "Trial") →
        //     no-op, so it's idempotent.
        // Runs BEFORE MintMissingPaidLicensesAsync so the converted seats count
        // toward the pool and only the remaining shortfall is minted. Returns the
        // number converted.
        private async Task<int> ConvertExpiredTrialLicensesToPaidAsync(
            CompanySubscription sub, int maxConvert, DateTime paidExpiry, DateTime now, int? actorUserId = null)
        {
            if (maxConvert <= 0) return 0;

            var expiredTrials = await _db.CompanyLicenses
                .Where(l => l.CompanySubscriptionId == sub.Id
                         && l.LicenseType == "Trial"
                         && l.TerminatedAt == null   // ended by a move to a purchased licence, not lapsed
                         && (l.IsDel || (l.ExpiryDate != null && l.ExpiryDate <= now)))
                .OrderBy(l => l.Id)
                .Take(maxConvert)
                .ToListAsync();

            if (expiredTrials.Count == 0) return 0;

            var seq = await NextLicenseSeqAsync(sub.ProfileId);

            foreach (var lic in expiredTrials)
            {
                lic.LicenseType = "Paid";
                lic.LicenseKey = $"CLD-PRO{seq++:000}-{sub.ProfileId}";
                lic.ExpiryDate = paidExpiry;
                lic.IsDel = false;                 // revive the lapsed seat
                lic.PlanId = sub.PlanId;
                if (sub.ServicePlanId.HasValue)
                {
                    lic.ServicePlanId = sub.ServicePlanId;
                    lic.ServicePlanVersionId = sub.ServicePlanVersionId;
                }
                lic.UpdatedAt = now;
                if (actorUserId.HasValue) lic.UpdatedBy = actorUserId;
            }
            await _db.SaveChangesAsync();

            // Re-license the teams: reopen each converted license's most-recent
            // assignment (closed by the expiry cascade). These licenses have no
            // active assignment right now, so reopening one can't violate the
            // (CompanyLicenseId, IsActive=true) unique index.
            var ids = expiredTrials.Select(l => l.Id).ToList();
            var lastAssignments = await _db.AssignedCompanyLicenses
                .Where(a => ids.Contains(a.CompanyLicenseId) && !a.IsDel)
                .GroupBy(a => a.CompanyLicenseId)
                .Select(g => g.OrderByDescending(a => a.AssignedAt).First())
                .ToListAsync();
            foreach (var a in lastAssignments)
            {
                a.IsActive = true;
                a.UnassignedAt = null;
                a.UpdatedAt = now;
            }
            if (lastAssignments.Count > 0) await _db.SaveChangesAsync();

            // Audit: one UPDATE per converted license, VersionNumber = max+1.
            var maxVersions = await _db.CompanyLicenseAuditLogs
                .Where(al => ids.Contains(al.Id))
                .GroupBy(al => al.Id)
                .Select(g => new { Id = g.Key, Max = g.Max(al => al.VersionNumber) })
                .ToDictionaryAsync(x => x.Id, x => x.Max);

            _db.CompanyLicenseAuditLogs.AddRange(expiredTrials.Select(l => new CompanyLicenseAuditLog
            {
                Id = l.Id,
                ProfileId = l.ProfileId,
                LicenseKey = l.LicenseKey,
                LicenseType = l.LicenseType,
                CompanySubscriptionId = l.CompanySubscriptionId,
                PlanId = l.PlanId,
                ExpiryDate = l.ExpiryDate,
                CreatedBy = l.CreatedBy,
                UpdatedBy = l.UpdatedBy,
                CreatedAt = l.CreatedAt,
                UpdatedAt = l.UpdatedAt,
                IsDel = l.IsDel,
                LoggedAt = now,
                VersionNumber = (maxVersions.TryGetValue(l.Id, out var v) ? v : 0) + 1,
                OperationType = "UPDATE"
            }));
            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "[TrialConvert] Converted {N} expired trial license(s) to Paid on SubId={SubId} (company {C}); {A} team assignment(s) re-licensed.",
                expiredTrials.Count, sub.Id, sub.ProfileId, lastAssignments.Count);

            return expiredTrials.Count;
        }

        private static readonly System.Text.RegularExpressions.Regex LicenseKeySeqRegex =
            new(@"^CLD-(?:TRI|PRO)(\d+)-", System.Text.RegularExpressions.RegexOptions.Compiled);

        // Next CLD license-key sequence number for a company: one past the
        // HIGHEST suffix already used by ANY of the company's license rows —
        // INCLUDING soft-deleted (IsDel=true) ones, which still occupy their key
        // under the unique LicenseKey index. The mint paths previously derived
        // the suffix from a live-only count (existingCount+1 / TeamCount+1); once
        // a company had any soft-deleted license, that reused an occupied key and
        // the INSERT hit the unique constraint, throwing a DB error that aborted
        // the whole webhook and left the company with NO new licenses. A
        // brand-new company (no prior rows) never collided — matching the
        // "new works, existing update fails" symptom exactly. Both TRI and PRO
        // prefixes are considered so a later trial→paid rename can't clash.
        private async Task<int> NextLicenseSeqAsync(uint profileId)
        {
            var keys = await _db.CompanyLicenses
                .Where(l => l.ProfileId == profileId)
                .Select(l => l.LicenseKey)
                .ToListAsync();

            int max = 0;
            foreach (var key in keys)
            {
                var m = LicenseKeySeqRegex.Match(key ?? string.Empty);
                if (m.Success && int.TryParse(m.Groups[1].Value, out var n) && n > max)
                    max = n;
            }
            return max + 1;
        }

        // Shared primitive: CREATE `count` brand-new Paid CompanyLicense rows on
        // `sub` — collision-safe keys (NextLicenseSeqAsync) + one INSERT audit row
        // each. This is the SINGLE place paid licenses are minted, used by BOTH
        // the Additional Subscription API (MintAddLicensesAsync, additive) and the
        // Checkout top-up (MintMissingPaidLicensesAsync). An existing company
        // therefore ALWAYS gets NEW license rows for the seats it buys, generated
        // identically by either flow. The caller owns the TeamCount counters and
        // the ambient transaction (if any). No-op when count <= 0.
        private async Task MintNewPaidLicensesAsync(
            CompanySubscription sub, int count, DateTime expiry, DateTime now, int? actorUserId)
        {
            if (count <= 0) return;

            var startSeq = await NextLicenseSeqAsync(sub.ProfileId);
            var newLicenses = new List<CompanyLicense>(count);
            for (int i = 0; i < count; i++)
            {
                newLicenses.Add(new CompanyLicense
                {
                    ProfileId = sub.ProfileId,
                    LicenseKey = $"CLD-PRO{(startSeq + i):000}-{sub.ProfileId}",
                    LicenseType = "Paid",
                    CompanySubscriptionId = sub.Id,
                    PlanId = sub.PlanId,
                    ServicePlanId = sub.ServicePlanId,
                    ServicePlanVersionId = sub.ServicePlanVersionId,
                    ExpiryDate = expiry,
                    CreatedBy = actorUserId,
                    UpdatedBy = actorUserId,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
            _db.CompanyLicenses.AddRange(newLicenses);
            await _db.SaveChangesAsync();   // populate identity Ids for the audit rows

            _db.CompanyLicenseAuditLogs.AddRange(newLicenses.Select(l => new CompanyLicenseAuditLog
            {
                Id = l.Id,
                ProfileId = l.ProfileId,
                LicenseKey = l.LicenseKey,
                LicenseType = l.LicenseType,
                CompanySubscriptionId = l.CompanySubscriptionId,
                PlanId = l.PlanId,
                ExpiryDate = l.ExpiryDate,
                CreatedBy = l.CreatedBy,
                UpdatedBy = l.UpdatedBy,
                CreatedAt = l.CreatedAt,
                UpdatedAt = l.UpdatedAt,
                IsDel = l.IsDel,
                LoggedAt = now,
                VersionNumber = 1,
                OperationType = "INSERT"
            }));
            await _db.SaveChangesAsync();
        }

        // Top up a subscription's CompanyLicense pool so it matches the
        // subscription's TeamCount by CREATING new paid rows for the shortfall —
        // the same rows the Additional Subscription API creates, so buying extra
        // seats via Checkout and via the add-on API behave identically. Needed
        // when a paid checkout raises TeamCount above what was originally minted
        // (e.g. an existing company with 5 seats buys up to 7 → 2 NEW rows; or a
        // 5-team trial pays for 10 → the 5 trial rows are promoted and 5 NEW rows
        // are created). Idempotent: when the pool already meets TeamCount it
        // returns without writing, so re-firing the webhook is safe.
        private async Task MintMissingPaidLicensesAsync(CompanySubscription sub, DateTime now, int? actorUserId = null)
        {
            var existingCount = await _db.CompanyLicenses
                .CountAsync(l => l.CompanySubscriptionId == sub.Id && !l.IsDel);

            var missing = sub.TeamCount - existingCount;
            if (missing <= 0)
            {
                _logger.LogInformation(
                    "[CheckoutUpgrade] License pool already matches TeamCount={T} (have {H}); nothing to mint. SubId={S}",
                    sub.TeamCount, existingCount, sub.Id);
                return;
            }

            // Newly-minted paid seats run to the current paid period end.
            await MintNewPaidLicensesAsync(sub, missing, sub.CurrentPeriodEnd, now, actorUserId);

            _logger.LogInformation(
                "[CheckoutUpgrade] Minted {N} new Paid licenses to match TeamCount={T}. SubId={S}",
                missing, sub.TeamCount, sub.Id);
        }

        // Webhook branch for op=addLicenses (one-time top-up created by
        // PurchaseAdditionalTeamsAsync). Mints N CompanyLicense rows on the
        // company's existing subscription, leaves them UNASSIGNED (admin
        // applies them via /api/license-assignments later), and bumps
        // TeamCount / MaxTeamsAllowed / MaxPeopleAllowed on the sub. Writes
        // an INSERT audit row per license and a Payment row for the charge.
        private async Task HandleAddLicensesPaidAsync(Session session)
        {
            if (session.Metadata == null) return;

            if (!session.Metadata.TryGetValue("companyId", out var profileIdStr) ||
                !uint.TryParse(profileIdStr, out var profileId))
            {
                _logger.LogWarning("[AddLicenses] Missing/invalid companyId metadata. SessionId={S}", session.Id);
                return;
            }
            if (!session.Metadata.TryGetValue("subscriptionId", out var subIdStr) ||
                !int.TryParse(subIdStr, out var subId))
            {
                _logger.LogWarning("[AddLicenses] Missing/invalid subscriptionId metadata. SessionId={S}", session.Id);
                return;
            }
            if (!session.Metadata.TryGetValue("additionalTeamsCount", out var addStr) ||
                !int.TryParse(addStr, out var additional) || additional < 1)
            {
                _logger.LogWarning("[AddLicenses] Missing/invalid additionalTeamsCount metadata. SessionId={S}", session.Id);
                return;
            }
            session.Metadata.TryGetValue("unitId", out var unitIdStr);

            // ActorUserId was threaded through metadata by PurchaseAdditionalTeamsAsync.
            // Null when the caller's token didn't yield a user id (background
            // jobs, etc.) — license rows tolerate that.
            int? actorUserId = null;
            if (session.Metadata.TryGetValue("actorUserId", out var actorStr) &&
                int.TryParse(actorStr, out var parsedActor))
            {
                actorUserId = parsedActor;
            }

            // One-time payments always carry a PaymentIntent; fall back to the
            // checkout session id so the idempotency key is never null.
            var dedupKey = !string.IsNullOrEmpty(session.PaymentIntentId)
                ? session.PaymentIntentId
                : session.Id;

            // Requested cycle stamped at checkout — governs the new license's
            // expiry (+1 month vs +1 year). Null when absent (legacy sessions);
            // the mint then falls back to the subscription's own cycle.
            var requestedCycle = ParseBillingCycleMetadata(session.Metadata);

            // A top-up checkout enables allow_promotion_codes, so this charge can
            // carry a customer-entered discount. Record the same breakdown the
            // subscription path records, or the row shows a reduced Amount with
            // nothing explaining it.
            var (topUpSubtotal, topUpDiscount) = ReadSessionDiscount(session);
            var topUpDiscountCode = topUpDiscount.HasValue
                ? await ResolveDiscountCodeAsync(session, ParsePromoCodeIdMetadata(session.Metadata))
                : null;

            await MintAddLicensesAsync(
                profileId, subId, additional, unitIdStr, actorUserId, dedupKey!,
                session.AmountTotal, session.Currency, session.PaymentIntentId, session.InvoiceId,
                requestedCycle, topUpSubtotal, topUpDiscount, topUpDiscountCode);
        }

        // Idempotent core mint for an addLicenses one-time payment, shared by the
        // live webhook (HandleAddLicensesPaidAsync) and the Stripe-sourced
        // reconcile/backfill (ReconcileAddLicensesAsync).
        //
        // Idempotency: keyed on the Stripe PaymentIntent (fallback: checkout
        // session id) recorded as Payment.StripePaymentIntentId. A webhook
        // re-delivery, a Stripe "Resend", or a reconcile pass over the same
        // charge finds the existing Payment row and mints nothing.
        //
        // The licenses, the subscription counter bump, and the Payment marker
        // commit in a SINGLE transaction, so a mid-write failure can never leave
        // licenses without their idempotency marker (which would let a retry
        // double-mint). The external card lookup runs BEFORE the transaction so
        // the tx isn't held open across a network call.
        //
        // Returns the number of licenses minted (0 when skipped as a duplicate,
        // or when the subscription can't be resolved).
        private async Task<int> MintAddLicensesAsync(
            uint profileId, int subId, int additional, string? unitIdStr,
            int? actorUserId, string dedupKey,
            long? amountTotalCents, string? currency,
            string? paymentIntentId, string? invoiceId,
            BillingCycle? requestedCycle,
            decimal? amountSubtotal = null, decimal? discountAmount = null,
            string? discountCode = null)
        {
            // Idempotency guard — we've already recorded this exact charge.
            var already = await _db.Payments.AnyAsync(p => p.StripePaymentIntentId == dedupKey);
            if (already)
            {
                _logger.LogInformation(
                    "[AddLicenses] Skip — charge {Key} already processed. CompanyId={C} SubId={S}",
                    dedupKey, profileId, subId);
                return 0;
            }

            var sub = await _db.CompanySubscriptions
                .FirstOrDefaultAsync(s => s.Id == subId && s.ProfileId == profileId);
            if (sub == null)
            {
                _logger.LogWarning(
                    "[AddLicenses] Subscription not found or company mismatch. companyId={C} subId={S}",
                    profileId, subId);
                return 0;
            }

            var now = DateTime.UtcNow;
            // A newly purchased seat starts its OWN independent lifecycle today:
            // ExpiryDate = today + one billing cycle (monthly → +1 month, annual
            // → +1 year). The cycle is the one REQUESTED for this purchase (from
            // checkout metadata); a yearly add-on therefore gets a 1-year expiry
            // even on a monthly base subscription. Legacy sessions with no cycle
            // metadata fall back to the subscription's cycle. It does not
            // inherit, and does not affect, any existing license's expiry.
            var cycle = requestedCycle ?? sub.BillingCycle;
            var addedExpiry = ComputeLicenseExpiry(cycle, now);

            // Card lookup is an external Stripe call — do it BEFORE opening the
            // transaction so we don't hold the tx open across the network.
            CardDetails? card = null;
            if (amountTotalCents.HasValue && amountTotalCents.Value > 0)
            {
                card = await _stripeService.GetCardUsedAsync(
                    paymentIntentId, invoiceId, sub.StripeSubscriptionId, sub.StripeCustomerId);
            }

            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                // CREATE the purchased seats as NEW paid rows via the shared
                // routine (the same one the Checkout top-up uses). Added seats
                // start their own lifecycle at addedExpiry.
                await MintNewPaidLicensesAsync(sub, additional, addedExpiry, now, actorUserId);

                sub.TeamCount += additional;
                sub.MaxTeamsAllowed += additional;
                sub.MaxPeopleAllowed = sub.MaxTeamsAllowed * MaxPeoplePerTeam;
                sub.UpdatedAt = now;

                // Payment row doubles as the idempotency marker — always written
                // (even when the card lookup came back empty) so the dedup key
                // exists for any future replay. One-time charges don't flow
                // through invoice.paid, so this is the sole payment record.
                // AmountTotal is cents per the Stripe contract. Created via the
                // repository (not a bare Add) so the PaymentAuditLog INSERT row
                // is written too — parity with the original handler. Its
                // SaveChanges runs inside this transaction and also flushes the
                // pending license-audit rows + the subscription counter bump.
                await _paymentRepo.CreateAsync(new Payment
                {
                    ProfileId = profileId,
                    CompanySubscriptionId = sub.Id,
                    Amount = amountTotalCents.HasValue ? amountTotalCents.Value / 100m : 0m,
                    // All null when the top-up carried no discount, and on the
                    // reconcile path, which replays a charge without its session.
                    AmountSubtotal = amountSubtotal,
                    DiscountAmount = discountAmount,
                    DiscountCode = discountCode,
                    Currency = (currency ?? "usd").ToLowerInvariant(),
                    PaymentMethod = PaymentMethodType.Card,
                    CardholderName = card?.CardholderName,
                    CardLast4 = card?.Last4,
                    CardBrand = card?.Brand,
                    CardExpMonth = card?.ExpMonth,
                    CardExpYear = card?.ExpYear,
                    StripePaymentMethodId = card?.PaymentMethodId,
                    CardFunding = card?.Funding,
                    CardCountry = card?.Country,
                    Status = PaymentStatus.Succeeded,
                    StripePaymentIntentId = dedupKey,
                    Description = $"Additional {additional} license(s) - CompanyId={profileId} UnitContext={unitIdStr}"
                });

                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }

            // Any leftover Trial licenses on this sub are intentionally left as-is.
            // Buying add-on seats does not convert a company's trial pool: each
            // trial seat keeps its own expiry and lapses on that date, while the
            // seats bought here were minted above with their own window.

            _logger.LogInformation(
                "[AddLicenses] Minted {N} licenses. CompanyId={C} SubId={S} NewTeamCount={T} Key={Key}",
                additional, profileId, sub.Id, sub.TeamCount, dedupKey);
            return additional;
        }

        // Stripe-sourced reconcile/backfill for a company's addLicenses top-ups.
        //
        // When a checkout.session.completed webhook fails before reaching the
        // mint (e.g. a signature or API-version error window), NOTHING lands in
        // our DB — no license, no TeamCount bump, no Payment row. The only record
        // is at Stripe, and Stripe stops retrying after ~3 days, so those paid
        // top-ups are stranded. This pulls the customer's PAID op=addLicenses
        // checkout sessions straight from Stripe and re-drives the idempotent
        // mint for any never processed. Safe to run repeatedly — the per-charge
        // idempotency guard turns already-minted sessions into a no-op.
        public async Task<AddLicenseReconcileResultDto> ReconcileAddLicensesAsync(uint profileId)
        {
            if (profileId == 0)
                throw new InvalidOperationException("ProfileId is required.");

            _ = await _subscriptionRepo.GetCompanyByIdAsync(profileId)
                ?? throw new InvalidOperationException("Invalid ProfileId. Profile does not exist.");

            // The Stripe customer id is needed to list the checkout sessions.
            // Take it from any of the company's subscriptions that carry one.
            var customerId = await _db.CompanySubscriptions
                .Where(s => s.ProfileId == profileId && !s.IsDel && s.StripeCustomerId != null)
                .OrderByDescending(s => s.CreatedAt)
                .Select(s => s.StripeCustomerId)
                .FirstOrDefaultAsync();

            if (string.IsNullOrEmpty(customerId))
                throw new InvalidOperationException(
                    "Company has no Stripe customer on file; there are no top-up charges to reconcile.");

            var sessions = await _stripeService.ListPaidAddLicenseSessionsAsync(customerId);

            var result = new AddLicenseReconcileResultDto
            {
                ProfileId = profileId,
                PaidSessionsFound = sessions.Count
            };

            foreach (var s in sessions)
            {
                // Only reconcile sessions that belong to THIS company. A Stripe
                // customer maps 1:1 to a company today, but guard anyway so a
                // shared/mis-mapped customer can't mint under the wrong company.
                if (!s.Metadata.TryGetValue("companyId", out var cidStr) ||
                    !uint.TryParse(cidStr, out var cid) || cid != profileId)
                    continue;
                if (!s.Metadata.TryGetValue("subscriptionId", out var sidStr) ||
                    !int.TryParse(sidStr, out var subId))
                    continue;
                if (!s.Metadata.TryGetValue("additionalTeamsCount", out var addStr) ||
                    !int.TryParse(addStr, out var additional) || additional < 1)
                    continue;

                s.Metadata.TryGetValue("unitId", out var unitIdStr);
                int? actorUserId = s.Metadata.TryGetValue("actorUserId", out var aStr) &&
                                   int.TryParse(aStr, out var parsedActor)
                    ? parsedActor
                    : (int?)null;

                var dedupKey = !string.IsNullOrEmpty(s.PaymentIntentId) ? s.PaymentIntentId : s.Id;

                var requestedCycle = ParseBillingCycleMetadata(s.Metadata);

                var minted = await MintAddLicensesAsync(
                    cid, subId, additional, unitIdStr, actorUserId, dedupKey!,
                    s.AmountTotal, s.Currency, s.PaymentIntentId, s.InvoiceId,
                    requestedCycle);

                if (minted > 0)
                {
                    result.SessionsMinted++;
                    result.LicensesMinted += minted;
                }
                else
                {
                    result.SessionsAlreadyProcessed++;
                }
            }

            _logger.LogInformation(
                "[AddLicenses][Reconcile] CompanyId={C} Found={F} Minted={M} Licenses={L} AlreadyDone={D}",
                profileId, result.PaidSessionsFound, result.SessionsMinted, result.LicensesMinted, result.SessionsAlreadyProcessed);

            return result;
        }

        // Product-purchase checkout (op=productPurchase from the Stripe product
        // listing page). Mirrors the trial→paid subscription conversion so the
        // same downstream records are created/updated across the existing
        // tables — no new tables:
        //   * CompanySubscription  → Active paid, TeamCount/MaxTeams set, PlanId
        //     resolved from teamCount (Plans.MaxTeams), StripeProductId stored.
        //   * CompanyLicenses      → existing trial rows promoted to Paid; the
        //     pool topped up to TeamCount (MintMissingPaidLicensesAsync).
        //   * AssignedCompanyLicenses → first license assigned to the default
        //     business unit if the company has no active assignment yet.
        // Idempotent: re-delivery promotes nothing new and mints nothing once
        // the pool already matches TeamCount.
        //
        // Required metadata: profileId, teamCount, productId. quantity optional.
        private async Task HandleProductPurchasePaidAsync(Session session)
        {
            if (session.Metadata == null) return;

            if (!session.Metadata.TryGetValue("companyId", out var profileIdStr) ||
                !uint.TryParse(profileIdStr, out var profileId))
            {
                _logger.LogWarning("[ProductPurchase] Missing/invalid companyId metadata. SessionId={S}", session.Id);
                return;
            }

            session.Metadata.TryGetValue("teamCount", out var teamCountStr);
            if (!int.TryParse(teamCountStr, out var teamCount) || teamCount < 1)
            {
                _logger.LogWarning(
                    "[ProductPurchase] Missing/invalid teamCount metadata (got '{T}'). CompanyId={C} SessionId={S}",
                    teamCountStr, profileId, session.Id);
                return;
            }

            session.Metadata.TryGetValue("productId", out var productId);

            var now = DateTime.UtcNow;

            // Same cycle resolution as the subscription checkout: metadata intent
            // → Stripe subscription's real interval → Monthly. Product-purchase
            // sessions carry no billingCycle metadata, so the Stripe-interval read
            // is what makes a yearly product purchase provision as Annual.
            var purchasedCycle =
                ParseBillingCycleMetadata(session.Metadata)
                ?? await _stripeService.GetSubscriptionBillingCycleAsync(session.SubscriptionId)
                ?? BillingCycle.Monthly;

            // Resolve the plan from the purchased team tier (same lookup used by
            // the subscription conversion and AccountRegistration).
            var plan = await _db.Plans
                .Where(p => p.IsActive && p.MaxTeams == teamCount)
                .OrderBy(p => p.SortOrder)
                .FirstOrDefaultAsync();
            if (plan == null)
                _logger.LogWarning(
                    "[ProductPurchase] No active plan with MaxTeams={T}; PlanId will be left unset/unchanged. CompanyId={C}",
                    teamCount, profileId);

            var existing = await _subscriptionRepo.GetActiveByProfileIdAsync(profileId);
            CompanySubscription sub;

            if (existing != null)
            {
                existing.StripeCustomerId = session.CustomerId;
                existing.StripeSubscriptionId = session.SubscriptionId;
                if (!string.IsNullOrEmpty(productId))
                    existing.StripeProductId = productId;
                existing.TeamCount = teamCount;
                existing.MaxTeamsAllowed = teamCount;
                existing.MaxPeopleAllowed = teamCount * MaxPeoplePerTeam;
                existing.Status = session.SubscriptionId != null ? SubscriptionStatus.Active : SubscriptionStatus.Trial;
                if (session.SubscriptionId != null)
                    existing.IsTrialUsed = true;
                if (plan != null)
                    existing.PlanId = plan.Id;

                // Stamp the purchased cycle before the period-end is computed.
                existing.BillingCycle = purchasedCycle;

                if (session.SubscriptionId != null)
                {
                    existing.CurrentPeriodStart = now;
                    existing.CurrentPeriodEnd = existing.BillingCycle == BillingCycle.Annual
                        ? now.AddYears(1)
                        : now.AddMonths(1);
                }

                existing.UpdatedAt = now;
                await _subscriptionRepo.UpdateAsync(existing);

                // Top the pool up to the purchased TeamCount with NEW paid seats,
                // then push the paid seats out to the paid period end. Trial rows
                // are left untouched — they keep their own expiry and are never
                // converted (see ExtendLicensesToAsync).
                await MintMissingPaidLicensesAsync(existing, now);
                await ExtendLicensesToAsync(existing.Id, existing.CurrentPeriodEnd, now);

                sub = existing;
            }
            else
            {
                sub = new CompanySubscription
                {
                    ProfileId = profileId,
                    PlanId = plan?.Id,
                    TeamCount = teamCount,
                    BillingCycle = purchasedCycle,
                    Status = SubscriptionStatus.Active,
                    StripeCustomerId = session.CustomerId,
                    StripeSubscriptionId = session.SubscriptionId,
                    StripeProductId = string.IsNullOrEmpty(productId) ? null : productId,
                    MaxTeamsAllowed = teamCount,
                    MaxPeopleAllowed = teamCount * MaxPeoplePerTeam,
                    IsTrialUsed = true,
                    CurrentPeriodStart = now,
                    CurrentPeriodEnd = purchasedCycle == BillingCycle.Annual
                        ? now.AddYears(1)
                        : now.AddMonths(1)
                };
                await _subscriptionRepo.CreateAsync(sub);

                // Fresh company with no prior licenses — mint the full pool.
                await MintMissingPaidLicensesAsync(sub, now);
            }

            // Assign the first license to the company's default business unit if
            // nothing is assigned yet (mirrors the trial-registration step so a
            // direct purchase still lands an active assignment).
            await EnsureDefaultUnitAssignmentAsync(profileId, sub.Id, now);

            // Restore/reactivate licenses + assignments the same way the
            // subscription conversion does.
            await ApplyExpiryIfDueAsync(sub);

            // Licences belong to Team Managers, not teams, so after a purchase
            // every live team still without licence coverage is given it (the
            // manager's licence, or a new free one). Best-effort per team.
            await CoverUncoveredTeamsAsync(profileId);

            _logger.LogInformation(
                "[ProductPurchase] Processed. CompanyId={C} SubId={S} TeamCount={T} PlanId={P} ProductId={Prod}",
                profileId, sub.Id, teamCount, sub.PlanId, productId);
        }

        private async Task CoverUncoveredTeamsAsync(uint profileId)
        {
            var teamIds = await _db.BusinessUnits.AsNoTracking()
                .Where(b => b.ProfileId == profileId && b.IsDel != true)
                .OrderBy(b => b.Id)
                .Select(b => b.Id)
                .ToListAsync();

            foreach (var teamId in teamIds)
            {
                try { await _licenseRepo.EnsureTeamCoverageAsync(teamId, null); }
                catch (Exception ex) { _logger.LogWarning(ex, "[ProductPurchase] Licence coverage failed for unit {UnitId}.", teamId); }
            }
        }

        // Assigns the first unassigned, active license on a company to its
        // default (lowest-Id) business unit — but only when the company has no
        // active assignment at all. No-op otherwise, so it never disturbs an
        // existing routing the admin set up. Writes the assignment + audit row.
        private async Task EnsureDefaultUnitAssignmentAsync(uint profileId, int subscriptionId, DateTime now)
        {
            var activeAssignments = await _subscriptionRepo.GetActiveLicenseAssignmentsByCompanyAsync(profileId);
            if (activeAssignments.Count > 0)
                return;

            var unit = (await _subscriptionRepo.GetBusinessUnitsByCompanyAsync(profileId))
                .OrderBy(u => u.Id)
                .FirstOrDefault();
            if (unit == null)
            {
                _logger.LogWarning(
                    "[ProductPurchase] No business unit found for CompanyId={C}; cannot auto-assign a license.", profileId);
                return;
            }

            var license = await _db.CompanyLicenses
                .Where(l => l.CompanySubscriptionId == subscriptionId && !l.IsDel)
                .OrderBy(l => l.Id)
                .FirstOrDefaultAsync();
            if (license == null)
                return;

            var assignment = new AssignedCompanyLicense
            {
                CompanyLicenseId = license.Id,
                BusinessUnitId = unit.Id,
                AssignedAt = now,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.AssignedCompanyLicenses.Add(assignment);
            await _db.SaveChangesAsync();

            _db.AssignedCompanyLicenseAuditLogs.Add(new AssignedCompanyLicenseAuditLog
            {
                Id = assignment.Id,
                CompanyLicenseId = assignment.CompanyLicenseId,
                BusinessUnitId = assignment.BusinessUnitId,
                AssignedAt = assignment.AssignedAt,
                UnassignedAt = assignment.UnassignedAt,
                IsActive = assignment.IsActive,
                CreatedBy = assignment.CreatedBy,
                UpdatedBy = assignment.UpdatedBy,
                CreatedAt = assignment.CreatedAt,
                UpdatedAt = assignment.UpdatedAt,
                IsDel = assignment.IsDel,
                LoggedAt = now,
                VersionNumber = 1,
                OperationType = "INSERT"
            });
            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "[ProductPurchase] Auto-assigned license {LicId} to BusinessUnit {UnitId}. CompanyId={C}",
                license.Id, unit.Id, profileId);
        }

        // A Stripe account sends every event to every webhook endpoint, so when
        // Dev, Pre-Prod (or another product) share one account each environment
        // also receives the others' events. With Stripe:EnvironmentTag set, an
        // event is ours only when its object (session / subscription) or else
        // its customer carries metadata rridaEnv = our tag. Untagged objects
        // are rejected too: they come from another product or predate tagging,
        // and acting on one could mint a licence for the wrong profile.
        // No tag configured → every event is ours (the original behaviour).
        private async Task<bool> IsEventForThisEnvironmentAsync(Stripe.Event ev)
        {
            var expected = _stripeSettings.EnvironmentTag;
            if (string.IsNullOrWhiteSpace(expected))
                return true;

            var raw = ev.Data.RawObject;
            string? ownTag = raw?["metadata"]?[StripeService.EnvironmentMetadataKey]?.ToString();
            if (!string.IsNullOrEmpty(ownTag))
                return ownTag == expected;

            string? customerId = raw?["customer"]?.ToString();
            if (string.IsNullOrEmpty(customerId))
                customerId = await _stripeService.GetInvoiceCustomerIdAsync(raw?["invoice"]?.ToString());

            return await _stripeService.GetCustomerEnvironmentTagAsync(customerId) == expected;
        }

        private async Task HandleInvoicePaid(Stripe.Event ev)
        {
            // Handle both invoice.paid (Invoice object) and invoice_payment.paid (InvoicePayment object)
            string? subscriptionId = null;
            string? customerId = null;
            string? paymentIntentId = null;
            string? invoiceId = null;
            long amountPaid = 0;
            string? currency = null;

            if (ev.Data.Object is Stripe.Invoice invoice)
            {
                subscriptionId = invoice.SubscriptionId;
                customerId = invoice.CustomerId;
                paymentIntentId = invoice.PaymentIntentId;
                invoiceId = invoice.Id;
                amountPaid = invoice.AmountPaid;
                currency = invoice.Currency;

                // API ≥2025-03-31 (e.g. 2026-01-28.clover) moved the subscription
                // id to parent.subscription_details.subscription, which Stripe.NET
                // 47 does not map, so read it from the raw JSON.
                if (string.IsNullOrEmpty(subscriptionId))
                    subscriptionId = ev.Data.RawObject?["parent"]?["subscription_details"]?["subscription"]?.ToString();
            }
            else
            {
                // invoice_payment.paid — extract from raw event data
                var dataObj = ev.Data.RawObject;
                invoiceId = dataObj?["invoice"]?.ToString();
                amountPaid = dataObj?["amount_paid"]?.Value<long>() ?? 0;
                currency = dataObj?["currency"]?.ToString();

                var paymentObj = dataObj?["payment"];
                paymentIntentId = paymentObj?["payment_intent"]?.ToString();

                // Fetch the invoice to get subscription and customer IDs
                if (!string.IsNullOrEmpty(invoiceId))
                {
                    try
                    {
                        var invoiceService = new Stripe.InvoiceService();
                        var fetchedInvoice = await invoiceService.GetAsync(invoiceId);
                        subscriptionId = fetchedInvoice.SubscriptionId;
                        customerId = fetchedInvoice.CustomerId;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "invoice_payment.paid - Failed to fetch invoice {InvoiceId}", invoiceId);
                    }
                }
            }

            _logger.LogInformation("{EventType} - InvoiceId: {InvoiceId}, CustomerId: {CustomerId}, SubscriptionId: {SubscriptionId}, Amount: {Amount}",
                ev.Type, invoiceId, customerId, subscriptionId, amountPaid);

            // Try finding subscription by StripeSubscriptionId first
            CompanySubscription? sub = null;
            if (!string.IsNullOrEmpty(subscriptionId))
            {
                sub = await _subscriptionRepo.GetByStripeSubscriptionIdAsync(subscriptionId);
            }

            // Fallback: find by StripeCustomerId. Only safe when we couldn't resolve
            // by the exact Stripe subscription id — otherwise, in a company with
            // MULTIPLE subscriptions, the customer's newest row could be a DIFFERENT
            // purchase and we'd attribute this invoice to the wrong subscription. So
            // accept the customer-resolved row only when the invoice carries no
            // subscription id at all, or that row has no Stripe subscription linked
            // yet (a stranded first checkout to backfill).
            if (sub == null && !string.IsNullOrEmpty(customerId))
            {
                _logger.LogInformation("{EventType} - Fallback lookup by CustomerId: {CustomerId}", ev.Type, customerId);
                var byCustomer = await _subscriptionRepo.GetByStripeCustomerIdAsync(customerId);

                // Adopt ONLY a row not yet linked to any Stripe subscription (a
                // stranded first checkout to backfill). Never adopt a row that
                // already carries a Stripe subscription id — in a multi-subscription
                // company that "newest" row is very likely a DIFFERENT purchase, and
                // attributing this invoice to it would put the payment on the wrong
                // subscription. This holds even when subscriptionId couldn't be
                // resolved (invoice_payment.paid fetch failure): if we can't say
                // which subscription the invoice is for and every row is already
                // linked, we skip rather than guess — the sibling invoice.paid (which
                // carries the id) or the checkout seed records the payment instead.
                if (byCustomer != null && string.IsNullOrEmpty(byCustomer.StripeSubscriptionId))
                {
                    sub = byCustomer;
                    if (!string.IsNullOrEmpty(subscriptionId))
                    {
                        sub.StripeSubscriptionId = subscriptionId;
                        sub.UpdatedAt = DateTime.UtcNow;
                        await _subscriptionRepo.UpdateAsync(sub);
                    }
                }
                else if (byCustomer != null)
                {
                    _logger.LogWarning(
                        "{EventType} - Customer {CustomerId} resolved to already-linked subscription {ResolvedSubId} but invoice subscription is {SubId}; skipping to avoid cross-subscription payment attribution.",
                        ev.Type, customerId, byCustomer.StripeSubscriptionId, subscriptionId);
                }
            }

            if (sub == null)
            {
                _logger.LogWarning("{EventType} - No subscription found for SubscriptionId: {StripeSubId} or CustomerId: {CustomerId}",
                    ev.Type, subscriptionId, customerId);
                return;
            }

            // Auto-cancel any pending invoices since card payment succeeded
            var pendingInvoices = await _paymentRepo.GetPendingInvoicesForSubscriptionAsync(sub.Id);
            foreach (var pendingInvoice in pendingInvoices)
            {
                pendingInvoice.Status = PaymentStatus.Failed;
                pendingInvoice.FailureReason = "Automatically canceled - card payment received via Stripe.";
                await _paymentRepo.UpdateAsync(pendingInvoice);
                _logger.LogInformation("{EventType} - Auto-canceled pending invoice {PaymentId} for company {CompanyId}", ev.Type, pendingInvoice.Id, sub.ProfileId);
            }

            // Resolve the card actually used for this invoice. The Basil API
            // drops Invoice.payment_intent so paymentIntentId is routinely null
            // here; the resolver falls back to the real charge on the customer
            // (and finally the card on file) so CardLast4 / CardBrand persist.
            var card = await _stripeService.GetCardUsedAsync(
                paymentIntentId, invoiceId, sub.StripeSubscriptionId, sub.StripeCustomerId);

            // Upsert by invoice id so this event backfills the row
            // checkout.session.completed already seeded (and so invoice.paid +
            // invoice_payment.paid don't insert duplicate rows for the same
            // payment). invoice_payment.paid is the one event that still carries
            // the PaymentIntent id under Basil, so it backfills that field here.
            await UpsertSubscriptionPaymentAsync(
                sub.ProfileId,
                sub.Id,
                invoiceId,
                paymentIntentId,
                amountPaid / 100m,
                currency ?? "usd",
                card,
                $"Subscription payment - {sub.TeamCount} team(s)");

            _logger.LogInformation("{EventType} - Payment record upserted for company {CompanyId}, amount: {Amount}", ev.Type, sub.ProfileId, amountPaid / 100m);
        }

        // One Payment row per Stripe invoice, upserted. checkout.session.completed
        // seeds the row the moment payment succeeds (invoice id + card on file);
        // invoice.paid / invoice_payment.paid then backfill whatever the earlier
        // event couldn't resolve — notably the PaymentIntent id, which the Basil
        // API strips from Invoice.payment_intent. Keying on StripeInvoiceId keeps
        // the multiple webhook events from inserting duplicate rows.
        private async Task UpsertSubscriptionPaymentAsync(
            uint profileId,
            int companySubscriptionId,
            string? invoiceId,
            string? paymentIntentId,
            decimal amount,
            string currency,
            CardDetails card,
            string description,
            decimal? amountSubtotal = null,
            decimal? discountAmount = null,
            string? discountCode = null)
        {
            Payment? existing = null;
            if (!string.IsNullOrEmpty(invoiceId))
                existing = await _db.Payments.FirstOrDefaultAsync(p => p.StripeInvoiceId == invoiceId);
            if (existing == null && !string.IsNullOrEmpty(paymentIntentId))
                existing = await _db.Payments.FirstOrDefaultAsync(p => p.StripePaymentIntentId == paymentIntentId);

            if (existing != null)
            {
                // Identity fields: backfill only when a prior event left them empty.
                existing.StripeInvoiceId ??= invoiceId;
                existing.StripePaymentIntentId ??= paymentIntentId;

                // Card details: REFRESH from the latest resolution, do NOT
                // backfill-only. The seeding event (checkout.session.completed)
                // has no PaymentIntent in subscription mode, so its card comes
                // from a best-effort fallback that can land on the customer's
                // OLD default card. A later invoice(_payment).paid carries the
                // real PaymentIntent and resolves the card actually charged —
                // let it OVERWRITE the stale value so the newest card the user
                // paid with is reflected. Each field is guarded on non-null so a
                // partial/failed lookup never wipes a good value already stored.
                if (!string.IsNullOrEmpty(card.CardholderName)) existing.CardholderName = card.CardholderName;
                if (!string.IsNullOrEmpty(card.Last4)) existing.CardLast4 = card.Last4;
                if (!string.IsNullOrEmpty(card.Brand)) existing.CardBrand = card.Brand;
                if (card.ExpMonth.HasValue) existing.CardExpMonth = card.ExpMonth;
                if (card.ExpYear.HasValue) existing.CardExpYear = card.ExpYear;
                if (!string.IsNullOrEmpty(card.PaymentMethodId)) existing.StripePaymentMethodId = card.PaymentMethodId;
                if (!string.IsNullOrEmpty(card.Funding)) existing.CardFunding = card.Funding;
                if (!string.IsNullOrEmpty(card.Country)) existing.CardCountry = card.Country;

                if (existing.Amount <= 0 && amount > 0)
                    existing.Amount = amount;

                // Discount breakdown: backfill only. The seeding event carries
                // the session totals; a later invoice event may not resolve them
                // at all, and must not blank out what was already recorded.
                existing.AmountSubtotal ??= amountSubtotal;
                existing.DiscountAmount ??= discountAmount;
                existing.DiscountCode ??= discountCode;

                existing.Status = PaymentStatus.Succeeded;
                existing.PaymentMethod = PaymentMethodType.Card;
                await _paymentRepo.UpdateAsync(existing);
                return;
            }

            await _paymentRepo.CreateAsync(new Payment
            {
                ProfileId = profileId,
                CompanySubscriptionId = companySubscriptionId,
                StripeInvoiceId = invoiceId,
                StripePaymentIntentId = paymentIntentId,
                Amount = amount,
                AmountSubtotal = amountSubtotal,
                DiscountAmount = discountAmount,
                DiscountCode = discountCode,
                Currency = currency,
                PaymentMethod = PaymentMethodType.Card,
                CardholderName = card.CardholderName,
                CardLast4 = card.Last4,
                CardBrand = card.Brand,
                CardExpMonth = card.ExpMonth,
                CardExpYear = card.ExpYear,
                StripePaymentMethodId = card.PaymentMethodId,
                CardFunding = card.Funding,
                CardCountry = card.Country,
                Status = PaymentStatus.Succeeded,
                Description = description
            });
        }

        // Recovery for a paid subscription whose checkout.session.completed never
        // provisioned it (signature / API-version failure, or any thrown handler).
        // Creates the missing CompanySubscription + its license batch from Stripe's
        // authoritative values (quantity, interval, period), in ONE transaction.
        // Idempotent on the Stripe subscription id: a concurrent delivery that
        // already created the row is returned as-is; a tight race is contained by
        // the CompanyLicense.LicenseKey unique index, which rolls the losing
        // transaction back whole (no duplicate row persists). Returns the resolved
        // subscription, or null when the owning company can't be resolved. The
        // Payment row is left to the following invoice.paid (which resolves this sub
        // by exact id once it exists).
        // First key of pg_advisory_xact_lock(int, int); the second is
        // hashtext(Stripe subscription id). Serializes the two webhook handlers
        // that may create the CompanySubscription for one Stripe subscription:
        // checkout.session.completed (CreateAdditionalSubscriptionAsync) and
        // customer.subscription.updated (SelfHealMissingSubscriptionAsync). Without
        // it both could insert, minting the purchase's licences twice.
        private const int StripeSubscriptionLockNamespace = 16025;

        private Task LockStripeSubscriptionAsync(string stripeSubscriptionId)
            => _db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({StripeSubscriptionLockNamespace}, hashtext({stripeSubscriptionId}))");

        private async Task<CompanySubscription?> SelfHealMissingSubscriptionAsync(Stripe.Event ev, Stripe.Subscription stripeSub)
        {
            if (string.IsNullOrEmpty(stripeSub.CustomerId)) return null;

            var profileId = await _stripeService.GetProfileIdForCustomerAsync(stripeSub.CustomerId);
            if (!profileId.HasValue) return null;

            // Idempotent: another delivery may have just provisioned it.
            var dup = await _subscriptionRepo.GetByStripeSubscriptionIdAsync(stripeSub.Id);
            if (dup != null) return dup;

            var now = DateTime.UtcNow;
            var cycle = await _stripeService.GetSubscriptionBillingCycleAsync(stripeSub.Id) ?? BillingCycle.Monthly;

            // Prefer Stripe's authoritative item period; fall back to now + cycle so
            // a self-healed row is never left immediately-expired.
            var (_, itemEnd) = TryReadStripeItemPeriod(ev);
            var periodEnd = (itemEnd.HasValue && itemEnd.Value > now)
                ? itemEnd.Value
                : (cycle == BillingCycle.Annual ? now.AddYears(1) : now.AddMonths(1));

            var qty = TryReadStripeItemQuantity(ev)
                ?? (int)(stripeSub.Items?.Data?.FirstOrDefault()?.Quantity ?? 0);
            if (qty < 1) qty = 1;

            var plan = await _db.Plans
                .Where(p => p.IsActive && p.MaxTeams == qty)
                .OrderBy(p => p.SortOrder)
                .FirstOrDefaultAsync();

            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                // checkout.session.completed may be provisioning this same Stripe
                // subscription right now (its transaction stays open across Stripe
                // calls). Serialize on the id and re-check under the lock.
                await LockStripeSubscriptionAsync(stripeSub.Id);
                var provisioned = await _subscriptionRepo.GetByStripeSubscriptionIdAsync(stripeSub.Id);
                if (provisioned != null)
                {
                    await tx.CommitAsync();
                    return provisioned;
                }

                var sub = new CompanySubscription
                {
                    ProfileId = profileId.Value,
                    PlanId = plan?.Id,
                    // A plan checkout puts its plan on the Stripe subscription too.
                    ServicePlanId = await ExistingServicePlanIdAsync(ParseServicePlanIdMetadata(stripeSub.Metadata)),
                    ServicePlanVersionId = await ExistingServicePlanVersionIdAsync(stripeSub.Metadata, ParseServicePlanIdMetadata(stripeSub.Metadata)),
                    ServicePlanUnitPrice = ParseServicePlanUnitPriceMetadata(stripeSub.Metadata),
                    TeamCount = qty,
                    MaxTeamsAllowed = qty,
                    MaxPeopleAllowed = qty * MaxPeoplePerTeam,
                    BillingCycle = cycle,
                    Status = SubscriptionStatus.Active,
                    StripeCustomerId = stripeSub.CustomerId,
                    StripeSubscriptionId = stripeSub.Id,
                    IsTrialUsed = true,
                    CurrentPeriodStart = now,
                    CurrentPeriodEnd = periodEnd
                };
                await _subscriptionRepo.CreateAsync(sub);

                // Pool is empty on the new sub → mints exactly `qty` paid rows,
                // each expiring at the sub's period end.
                await MintMissingPaidLicensesAsync(sub, now);

                await tx.CommitAsync();

                _logger.LogWarning(
                    "customer.subscription.updated - SELF-HEAL provisioned missing subscription {SubId} (StripeSub {S}) for company {C}: {N} team(s), cycle {Cycle}. checkout.session.completed had not provisioned this purchase.",
                    sub.Id, stripeSub.Id, profileId.Value, qty, cycle);

                return sub;
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        private async Task HandleSubscriptionUpdated(Stripe.Event ev)
        {
            var stripeSub = ev.Data.Object as Stripe.Subscription;
            if (stripeSub == null) return;

            // Resolve OUR subscription. Normally checkout.session.completed has
            // already stamped StripeSubscriptionId. When that event failed (e.g.
            // a signature / API-version window), it never did — so fall back to
            // the Stripe customer id, then to the customer's metadata.profileId.
            // This lets a SUCCEEDING subscription event fully provision a company
            // that a FAILED checkout left stranded, instead of no-oping.
            var sub = await FindByStripeSubscriptionId(stripeSub.Id);

            // A licence plan-change subscription (POST /api/license/change) is
            // never adopted or self-healed — both would attach a NEW licence
            // batch to it. Apply the plan change instead (no-op when
            // checkout.session.completed already did), then sync it as usual.
            if (sub == null && IsLicencePlanChange(stripeSub.Metadata))
            {
                if (!string.Equals(stripeSub.Status, "active", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation(
                        "customer.subscription.updated - Plan-change StripeSub {StripeSubId} is {Status}; waiting for checkout to complete.",
                        stripeSub.Id, stripeSub.Status);
                    return;
                }

                sub = await ApplyLicencePlanChangeAsync(stripeSub.Metadata, stripeSub.Id, stripeSub.CustomerId);
                if (sub == null) return;
            }

            // Fallbacks adopt a row our checkout webhook hasn't linked to a Stripe
            // subscription yet (a failed/slow checkout.session.completed). In a
            // company with MULTIPLE subscriptions these MUST NOT latch onto a
            // sibling: this handler overwrites status / period / cancel flag /
            // quantity, so adopting the wrong row would corrupt an unrelated
            // subscription. So only adopt a row that has NO Stripe subscription
            // linked yet; once a row already carries a different id we skip and let
            // the exact-id path handle it when the event is (re)delivered after the
            // proper row exists.
            if (sub == null && !string.IsNullOrEmpty(stripeSub.CustomerId))
            {
                var byCustomer = await _subscriptionRepo.GetByStripeCustomerIdAsync(stripeSub.CustomerId);
                if (byCustomer != null && string.IsNullOrEmpty(byCustomer.StripeSubscriptionId))
                    sub = byCustomer;
            }
            if (sub == null && !string.IsNullOrEmpty(stripeSub.CustomerId))
            {
                var profileId = await _stripeService.GetProfileIdForCustomerAsync(stripeSub.CustomerId);
                if (profileId.HasValue)
                {
                    var byCompany = await _subscriptionRepo.GetActiveByProfileIdAsync(profileId.Value);
                    if (byCompany != null && string.IsNullOrEmpty(byCompany.StripeSubscriptionId))
                        sub = byCompany;
                }
            }
            // Self-heal: no local row for this Stripe subscription and nothing
            // unlinked to adopt. If Stripe reports it ACTIVE, its
            // checkout.session.completed never provisioned it (e.g. a signature /
            // API-version failure). For an ADDITIONAL (2nd+) purchase there is no
            // other recovery path — reconcile-add-licenses only covers the legacy
            // one-time op=addLicenses flow, not mode=subscription add-ons — so
            // without this the customer is billed by Stripe with nothing delivered.
            // Provision the missing subscription + its license batch from Stripe's
            // authoritative values (transactional + idempotent on the Stripe
            // subscription id). The Payment row is seeded by the following
            // invoice.paid, which now resolves this sub by exact id.
            if (sub == null && string.Equals(stripeSub.Status, "active", StringComparison.OrdinalIgnoreCase))
                sub = await SelfHealMissingSubscriptionAsync(ev, stripeSub);

            if (sub == null)
            {
                _logger.LogWarning(
                    "customer.subscription.updated - No local subscription for StripeSubId={StripeSubId} CustomerId={CustomerId} (no row, none to adopt, not an active/resolvable purchase); skipping.",
                    stripeSub.Id, stripeSub.CustomerId);
                return;
            }

            sub.Status = MapStripeStatus(stripeSub.Status);

            // Keep BillingCycle in sync with what Stripe actually bills (the
            // subscription item's recurring interval). Authoritative on renewals
            // and a safety net when checkout.session.completed couldn't stamp it.
            // Guarded: only overwrites when the interval resolves, so a failed
            // lookup never corrupts the stored cycle.
            var stripeCycle = await _stripeService.GetSubscriptionBillingCycleAsync(stripeSub.Id);
            if (stripeCycle.HasValue)
                sub.BillingCycle = stripeCycle.Value;

            // Pull period dates from items[0] in raw JSON (Stripe's new
            // location since API 2025-04-30). Fall back to the deprecated
            // subscription-level SDK properties only when the item-level
            // values are missing.
            //
            // Two guards to never corrupt the row:
            //   (a) reject any value ≤ year 2000 — that's the epoch sentinel
            //       Stripe returns when the field isn't sent (→ 1970-01-01);
            //   (b) for CurrentPeriodEnd reject any value < now — Stripe
            //       should never report a past period end for an active sub,
            //       so a past value almost certainly means stale/duplicate
            //       event delivery. Preserving the existing future end date
            //       (set by HandleCheckoutSessionCompleted) is safer than
            //       overwriting it with garbage.
            var nowUtc = DateTime.UtcNow;
            var (rawStart, rawEnd) = TryReadStripeItemPeriod(ev);
            var newStart = rawStart;
            var newEnd = rawEnd;

            if (!newStart.HasValue && stripeSub.CurrentPeriodStart > new DateTime(2000, 1, 1))
                newStart = stripeSub.CurrentPeriodStart;
            if (!newEnd.HasValue && stripeSub.CurrentPeriodEnd > new DateTime(2000, 1, 1))
                newEnd = stripeSub.CurrentPeriodEnd;

            if (newStart.HasValue)
                sub.CurrentPeriodStart = newStart.Value;
            if (newEnd.HasValue && newEnd.Value > nowUtc)
                sub.CurrentPeriodEnd = newEnd.Value;

            _logger.LogInformation(
                "[SUB-UPDATED-DATE-FIX] SubId={SubId} StripeSubId={StripeSubId} " +
                "rawStart={RawStart:O} rawEnd={RawEnd:O} " +
                "sdkStart={SdkStart:O} sdkEnd={SdkEnd:O} " +
                "writtenStart={WrittenStart:O} writtenEnd={WrittenEnd:O}",
                sub.Id, stripeSub.Id,
                rawStart, rawEnd,
                stripeSub.CurrentPeriodStart, stripeSub.CurrentPeriodEnd,
                sub.CurrentPeriodStart, sub.CurrentPeriodEnd);

            sub.CancelAtPeriodEnd = stripeSub.CancelAtPeriodEnd;

            // Keep CanceledAt in step with the cancel flag — this is the only path
            // that maintains it for a cancellation/reactivation driven directly in
            // Stripe (our own licence cancel/reactivate already stamp it):
            //   * scheduled to cancel → record WHEN, once. Don't overwrite an
            //     existing stamp, so the original cancellation time is preserved
            //     (e.g. CancelLicenseAsync set it moments before this webhook arrived).
            //   * renewing / reactivated → clear it back to null.
            if (stripeSub.CancelAtPeriodEnd)
            {
                if (!sub.CanceledAt.HasValue)
                    sub.CanceledAt = DateTime.UtcNow;
            }
            else
            {
                sub.CanceledAt = null;
            }

            // Resilience provisioning. When the Stripe subscription is active,
            // treat this event as an authoritative "paid" signal and make sure
            // our row + license pool reflect it — so a company is never left
            // unprovisioned because checkout.session.completed failed.
            bool isActive = string.Equals(stripeSub.Status, "active", StringComparison.OrdinalIgnoreCase);
            bool grew = false;
            if (isActive)
            {
                // Backfill the Stripe linkage the failed checkout never stamped.
                if (string.IsNullOrEmpty(sub.StripeSubscriptionId)) sub.StripeSubscriptionId = stripeSub.Id;
                if (string.IsNullOrEmpty(sub.StripeCustomerId)) sub.StripeCustomerId = stripeSub.CustomerId;
                sub.IsTrialUsed = true;

                // Grow (never shrink) TeamCount to the Stripe-authoritative paid
                // quantity. Growth handles the failed-checkout case (our row is
                // still at the trial count) and any seat increase driven through
                // the subscription itself. Shrinking is intentionally avoided so
                // a stale/duplicate event can't strand paid seats — and so it
                // never fights the one-time addLicenses top-up, which raises our
                // TeamCount without changing the Stripe subscription quantity.
                var stripeQty = TryReadStripeItemQuantity(ev)
                    ?? (int)(stripeSub.Items?.Data?.FirstOrDefault()?.Quantity ?? 0);
                if (stripeQty > sub.TeamCount)
                {
                    sub.TeamCount = stripeQty;
                    sub.MaxTeamsAllowed = stripeQty;
                    sub.MaxPeopleAllowed = stripeQty * MaxPeoplePerTeam;

                    var paidPlan = await _db.Plans
                        .Where(p => p.IsActive && p.MaxTeams == stripeQty)
                        .OrderBy(p => p.SortOrder)
                        .FirstOrDefaultAsync();
                    if (paidPlan != null) sub.PlanId = paidPlan.Id;
                    // A plan checkout puts its plan on the Stripe subscription too.
                    sub.ServicePlanId ??= await ExistingServicePlanIdAsync(ParseServicePlanIdMetadata(stripeSub.Metadata));
                    sub.ServicePlanVersionId ??= await ExistingServicePlanVersionIdAsync(stripeSub.Metadata, sub.ServicePlanId);
                    sub.ServicePlanUnitPrice ??= ParseServicePlanUnitPriceMetadata(stripeSub.Metadata);

                    grew = true;
                }
            }

            sub.UpdatedAt = DateTime.UtcNow;
            await _subscriptionRepo.UpdateAsync(sub);

            if (isActive)
            {
                // Leftover trial rows are left as-is: a trial seat is never
                // converted by a subscription update. It runs to its own expiry
                // and the paid seats live alongside it.

                // Mint ONLY when genuinely under-provisioned: the pool is empty
                // (stranded by a failed checkout) or we just grew the seat count.
                // This deliberately does NOT refill seats that lapsed under a
                // healthy sub — matching prior behavior so a routine renewal /
                // metadata update never resurrects intentionally-expired
                // licenses. MintMissingPaidLicensesAsync is itself idempotent.
                var activePoolCount = await _db.CompanyLicenses
                    .CountAsync(l => l.CompanySubscriptionId == sub.Id && !l.IsDel);
                if (grew || activePoolCount == 0)
                    await MintMissingPaidLicensesAsync(sub, nowUtc);
            }

            // Renewal: extend live seats to the new period end so paid licenses
            // don't lapse mid-subscription, then reconcile per-license state.
            if (newEnd.HasValue && newEnd.Value > nowUtc)
                await ExtendLicensesToAsync(sub.Id, sub.CurrentPeriodEnd, nowUtc);
            await ApplyExpiryIfDueAsync(sub);
        }

        private async Task HandleSubscriptionDeleted(Stripe.Event ev)
        {
            var stripeSub = ev.Data.Object as Stripe.Subscription;
            if (stripeSub == null) return;

            var sub = await FindByStripeSubscriptionId(stripeSub.Id);
            if (sub == null) return;

            sub.Status = SubscriptionStatus.Expired;
            sub.UpdatedAt = DateTime.UtcNow;
            await _subscriptionRepo.UpdateAsync(sub);
        }

        private async Task<CompanySubscription?> FindByStripeSubscriptionId(string stripeSubId)
        {
            return await _subscriptionRepo.GetByStripeSubscriptionIdAsync(stripeSubId);
        }

        private static SubscriptionStatus MapStripeStatus(string status) => status switch
        {
            "active" => SubscriptionStatus.Active,
            "trialing" => SubscriptionStatus.Trial,
            "past_due" => SubscriptionStatus.PastDue,
            "canceled" => SubscriptionStatus.Canceled,
            "unpaid" => SubscriptionStatus.PastDue,
            _ => SubscriptionStatus.Active
        };

        private CurrentSubscriptionDto MapToDto(CompanySubscription sub)
        {
            var pricePerTeam = _stripeSettings.PricePerLicenseMonthly;
            var monthlyTotal = pricePerTeam * sub.TeamCount;

            return new CurrentSubscriptionDto
            {
                Id = sub.Id,
                BillingCycle = sub.BillingCycle.ToString(),
                // true only when live (Active AND not scheduled to cancel); a
                // canceled/expired/ended subscription returns false.
                Status = sub.Status == SubscriptionStatus.Active && !sub.CancelAtPeriodEnd,
                TeamCount = sub.TeamCount,
                MaxTeamsAllowed = sub.MaxTeamsAllowed,
                MaxPeopleAllowed = sub.MaxPeopleAllowed,
                MonthlyTotal = monthlyTotal,
                AnnualTotal = monthlyTotal * 10,
                PricePerTeam = pricePerTeam,
                TrialEndDate = sub.TrialEndDate,
                CurrentPeriodEnd = sub.CurrentPeriodEnd,
                CancelAtPeriodEnd = sub.CancelAtPeriodEnd,
                CanceledAt = sub.CanceledAt,
                IsTrialUsed = sub.IsTrialUsed,
                LifetimeDiscountPercent = sub.LifetimeDiscountPercent
            };
        }

        private async Task<Infrastructure.Models.PromoCode?> GetPromoCodeByCode(string code)
        {
            return await _promoCodeRepo.GetByCodeAsync(code.Trim().ToUpper());
        }

        // A lifetime promo code is a promise made to the COMPANY, not to the one
        // subscription row that happened to redeem it.
        //
        // Under the subscription-per-purchase model every later purchase — an
        // extra team batch, or a re-subscribe after expiry — creates a NEW
        // CompanySubscription backed by a NEW Stripe subscription. A Stripe
        // coupon attaches to the subscription it was applied to, so the original
        // row's `forever` coupon has no effect on any of them, and the new rows
        // carry no LifetimeDiscountPercent of their own. Resolving the discount
        // company-wide is what keeps "forever" actually meaning forever.
        //
        // Pulls Stripe's own discount arithmetic off a completed Checkout Session:
        // the pre-discount total and what the coupon actually took off.
        //
        // Returns (null, null) when the session carried no discount — that is what
        // distinguishes "paid full price" from "discounted by zero" in the stored
        // Payment row. We never recompute these locally: Stripe applies percent-off
        // in minor currency units per line item (and after any Adaptive Pricing
        // conversion), so its figures are the only ones that match the charge.
        private static (decimal? Subtotal, decimal? Discount) ReadSessionDiscount(Session session)
        {
            var discountCents = session.TotalDetails?.AmountDiscount;
            if (discountCents is not > 0)
                return (null, null);

            var subtotal = session.AmountSubtotal.HasValue
                ? session.AmountSubtotal.Value / 100m
                : (decimal?)null;

            return (subtotal, discountCents.Value / 100m);
        }

        // Names the code behind a discounted session, so a Payment row says WHICH
        // code took the money off and not merely how much.
        //
        // Two independent discount paths now exist and this collapses them to one
        // string:
        //
        //   1. Our own promo — the admin-created code was validated in-app, its id
        //      rode the session metadata, and an ad-hoc Stripe coupon carried the
        //      percentage. The readable code lives in OUR PromoCodes table.
        //   2. A Stripe Dashboard promotion code the customer typed on the hosted
        //      page (allow_promotion_codes). Nothing about it exists locally, so
        //      it has to be read back out of Stripe.
        //
        // Checked in that order because path 1 is authoritative when present: the
        // session was created with `discounts`, which Stripe forbids combining
        // with `allow_promotion_codes`, so the two can never both apply.
        //
        // Best-effort throughout — returns null rather than throwing. A discount
        // whose code can't be named still records its AMOUNT correctly, and a
        // webhook must not fail over promo bookkeeping.
        private async Task<string?> ResolveDiscountCodeAsync(Session session, int? ourPromoCodeId)
        {
            if (ourPromoCodeId.HasValue)
            {
                try
                {
                    var ours = await _promoCodeRepo.GetByIdAsync(ourPromoCodeId.Value);
                    if (!string.IsNullOrWhiteSpace(ours?.Code))
                        return ours!.Code;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "ResolveDiscountCodeAsync - lookup of promo code {PromoCodeId} failed; payment will record the discount without a code.",
                        ourPromoCodeId.Value);
                }
            }

            // Customer-entered Stripe promotion code.
            var code = await _stripeService.GetSessionPromotionCodeAsync(session.Id);
            if (!string.IsNullOrWhiteSpace(code))
            {
                _logger.LogInformation(
                    "Checkout session {SessionId} redeemed Stripe Dashboard promotion code {Code}. No local PromoCodes row exists for it; recorded on the Payment only.",
                    session.Id, code);
                return code;
            }

            return null;
        }

        // Reads the promo code id stamped onto the checkout session at creation
        // time. Best-effort: unparseable/absent metadata means "no redemption",
        // never an exception — a webhook must not fail over promo bookkeeping.
        private static int? ParsePromoCodeIdMetadata(IDictionary<string, string>? metadata)
        {
            if (metadata != null
                && metadata.TryGetValue("promoCodeId", out var raw)
                && int.TryParse(raw, out var id)
                && id > 0)
                return id;
            return null;
        }

        // Returns null when the company has never redeemed a lifetime code.
        private async Task<decimal?> GetCompanyLifetimeDiscountAsync(uint profileId)
        {
            return await _db.CompanySubscriptions
                .Where(s => s.ProfileId == profileId
                         && !s.IsDel
                         && s.LifetimeDiscountPercent != null
                         && s.LifetimeDiscountPercent > 0)
                .MaxAsync(s => s.LifetimeDiscountPercent);
        }

        private static string GetTierDisplayName(int teamCount) => teamCount switch
        {
            1 => "Single Team",
            >= 2 and <= 9 => $"{teamCount} Teams (Small)",
            >= 10 and <= 24 => $"{teamCount} Teams (Medium)",
            >= 25 => $"{teamCount} Teams (Large)",
            _ => "Subscription"
        };
    }
}
