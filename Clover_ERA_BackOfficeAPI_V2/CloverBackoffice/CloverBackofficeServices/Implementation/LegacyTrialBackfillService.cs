using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CloverBackoffice.Services.Implementation
{
    // Ensures every business unit holds a license. Two jobs in one pass:
    //
    //   A) LEGACY companies (no subscription) → create a 30-day Trial capped to
    //      the current unit count, then mint + assign one Trial license per unit.
    //   B) ANY company whose units are missing license assignments → assign the
    //      company's existing FREE (unassigned) licenses to those units. This
    //      catches normal trial/paid companies where registration only ever
    //      assigned license #1 to the default unit, leaving later units bare.
    //
    // Minting only happens for LEGACY and AUTO-TRIAL companies (the ones this
    // migration owns). Normal trial / paid companies are only ever *assigned*
    // their already-owned free licenses — their LicenseType, ExpiryDate, and
    // tier caps are never modified, so paid pools are never corrupted or
    // over-provisioned beyond what they already bought.
    //
    // Idempotent & safe to re-run: units that already hold an active license are
    // skipped; companies with nothing to do are no-ops.
    public class LegacyTrialBackfillService : ILegacyTrialBackfillService
    {
        private readonly ApplicationDbContext _db;
        private readonly ICompanySubscriptionRepository _subscriptionRepo;
        private readonly ILogger<LegacyTrialBackfillService> _logger;

        private const int MaxPeoplePerTeam = 20;

        public LegacyTrialBackfillService(
            ApplicationDbContext db,
            ICompanySubscriptionRepository subscriptionRepo,
            ILogger<LegacyTrialBackfillService> logger)
        {
            _db = db;
            _subscriptionRepo = subscriptionRepo;
            _logger = logger;
        }

        public async Task<LegacyTrialBackfillResult> RunAsync(int trialDays = 30, bool dryRun = false)
        {
            if (trialDays < 1) trialDays = 30;

            var now = DateTime.UtcNow;
            var defaultTrialEnd = now.AddDays(trialDays);

            var result = new LegacyTrialBackfillResult
            {
                DryRun = dryRun,
                TrialDays = trialDays,
                TrialEndUtc = defaultTrialEnd
            };

            var companies = await _db.Profiles
                .Where(c => !c.IsDel)
                .OrderBy(c => c.Id)
                .Select(c => new { c.Id, Name = c.ProfileName })
                .ToListAsync();

            foreach (var c in companies)
            {
                var sub = await _db.CompanySubscriptions
                    .Where(s => s.ProfileId == c.Id && !s.IsDel)
                    .OrderByDescending(s => s.CreatedAt)
                    .FirstOrDefaultAsync();

                bool isLegacy = sub == null;
                // Auto-trial signature: the trial THIS migration creates.
                bool isAutoTrial = sub != null
                                   && sub.PlanId == null
                                   && sub.Status == SubscriptionStatus.Trial
                                   && string.IsNullOrEmpty(sub.StripeSubscriptionId);
                // Only legacy + auto-trial companies may have NEW licenses minted
                // and their caps reset. Everyone else is assign-only.
                bool canMint = isLegacy || isAutoTrial;

                var unitIds = await _db.BusinessUnits
                    .Where(bu => bu.Profile.Id == c.Id && (bu.IsDel == null || bu.IsDel == false))
                    .OrderBy(bu => bu.Id)
                    .Select(bu => bu.Id)
                    .ToListAsync();

                // Units already holding an active, non-deleted license of THIS company.
                var coveredUnitIds = await (
                    from a in _db.AssignedCompanyLicenses
                    join l in _db.CompanyLicenses on a.CompanyLicenseId equals l.Id
                    where l.ProfileId == c.Id && a.IsActive && !a.IsDel && !l.IsDel
                    select a.BusinessUnitId
                ).Distinct().ToListAsync();

                // Units covered by an active complimentary grant ride another team's
                // purchased license. They are NOT unlicensed — without this, the
                // backfill would assign them their own purchased license on every
                // startup, silently defeating the complimentary grant.
                var complimentaryUnitIds = await (
                    from g in _db.ComplimentaryLicenseGrants
                    join l in _db.CompanyLicenses on g.CompanyLicenseId equals l.Id
                    where l.ProfileId == c.Id && g.IsActive && !g.IsDel && !l.IsDel
                    select g.ComplimentaryBusinessUnitId
                ).Distinct().ToListAsync();

                var covered = new HashSet<uint>(coveredUnitIds);
                covered.UnionWith(complimentaryUnitIds);

                var missingUnitIds = unitIds.Where(id => !covered.Contains(id)).ToList();

                // Nothing to do unless: we can mint (legacy/auto-trial with units),
                // or there are free licenses we can assign to the missing units.
                bool hasFreeLicense = await _db.CompanyLicenses.AnyAsync(l =>
                    l.ProfileId == c.Id && !l.IsDel &&
                    !_db.AssignedCompanyLicenses.Any(a => a.IsActive && !a.IsDel && a.CompanyLicenseId == l.Id));

                bool needsWork = (isLegacy)                                   // must create sub
                                 || (missingUnitIds.Count > 0 && (canMint || hasFreeLicense));

                if (!needsWork)
                {
                    result.Profiles.Add(new LegacyCompanyResult
                    {
                        ProfileId = c.Id,
                        CompanyName = c.Name,
                        UnitCount = unitIds.Count,
                        LicensesCreated = 0,
                        Status = "AlreadyCovered"
                    });
                    continue;
                }

                result.LegacyCompanyCount++;

                if (dryRun)
                {
                    result.Profiles.Add(new LegacyCompanyResult
                    {
                        ProfileId = c.Id,
                        CompanyName = c.Name,
                        UnitCount = unitIds.Count,
                        LicensesCreated = missingUnitIds.Count,
                        Status = isLegacy ? "Preview(create)" : (canMint ? "Preview(repair)" : "Preview(assign)")
                    });
                    continue;
                }

                try
                {
                    var assigned = await EnsureCoverageAsync(
                        c.Id, sub, isLegacy, canMint, defaultTrialEnd, now, unitIds, missingUnitIds);

                    result.TotalLicensesCreated += assigned;
                    result.Profiles.Add(new LegacyCompanyResult
                    {
                        ProfileId = c.Id,
                        CompanyName = c.Name,
                        UnitCount = unitIds.Count,
                        LicensesCreated = assigned,
                        Status = isLegacy ? "Migrated" : (canMint ? "Repaired" : "Assigned")
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "[LegacyTrialBackfill] Failed for CompanyId={CompanyId} ({Name}).", c.Id, c.Name);
                    result.Profiles.Add(new LegacyCompanyResult
                    {
                        ProfileId = c.Id,
                        CompanyName = c.Name,
                        UnitCount = unitIds.Count,
                        Status = $"Error: {ex.GetBaseException().Message}"
                    });
                }
            }

            if (!dryRun)
                _logger.LogInformation(
                    "[LegacyTrialBackfill] Processed {Count} company(ies); {Licenses} unit(s) newly licensed. TrialDays={Days}.",
                    result.LegacyCompanyCount, result.TotalLicensesCreated, trialDays);

            return result;
        }

        // On-demand single-company bootstrap (see interface). Fires ONLY when the
        // company has no subscription — mirrors the legacy branch of RunAsync but
        // scoped to one company, so it can be triggered lazily from the Status API.
        public async Task<bool> EnsureTrialForCompanyAsync(uint profileId, int trialDays = 30)
        {
            if (trialDays < 1) trialDays = 30;

            // Guard: never touch a company that already has a subscription (trial
            // or paid). Those keep their pools/assignments exactly as-is.
            bool hasSub = await _db.CompanySubscriptions
                .AnyAsync(s => s.ProfileId == profileId && !s.IsDel);
            if (hasSub) return false;

            var now = DateTime.UtcNow;
            var trialEnd = now.AddDays(trialDays);

            var unitIds = await _db.BusinessUnits
                .Where(bu => bu.Profile.Id == profileId && (bu.IsDel == null || bu.IsDel == false))
                .OrderBy(bu => bu.Id)
                .Select(bu => bu.Id)
                .ToListAsync();

            // Legacy company → all units are uncovered. EnsureCoverageAsync mints +
            // assigns one Trial license per unit and creates the Trial subscription
            // (even when the company has zero units, so it still lands on a trial).
            await EnsureCoverageAsync(
                profileId,
                existingSub: null,
                isLegacy: true,
                canMint: true,
                defaultTrialEnd: trialEnd,
                now: now,
                allUnitIds: unitIds,
                missingUnitIds: unitIds);

            _logger.LogInformation(
                "[AutoTrial] Bootstrapped {Days}-day trial for CompanyId={CompanyId}; licensed {Units} unit(s).",
                trialDays, profileId, unitIds.Count);

            return true;
        }

        // Returns the number of units newly covered. One transaction per company.
        private async Task<int> EnsureCoverageAsync(
            uint profileId,
            CompanySubscription? existingSub,
            bool isLegacy,
            bool canMint,
            DateTime defaultTrialEnd,
            DateTime now,
            List<uint> allUnitIds,
            List<uint> missingUnitIds)
        {
            var unitCount = allUnitIds.Count;

            int? actorUserId = await _db.Users
                .Where(u => u.ProfileId == (int)profileId && u.UserGroupId == 1 && !u.IsDel)
                .Select(u => (int?)u.UserId)
                .FirstOrDefaultAsync();

            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                CompanySubscription sub;
                DateTime licenseExpiry;

                if (isLegacy)
                {
                    licenseExpiry = defaultTrialEnd;
                    sub = new CompanySubscription
                    {
                        ProfileId = profileId,
                        PlanId = null,
                        BillingCycle = BillingCycle.Monthly,
                        Status = SubscriptionStatus.Trial,
                        TeamCount = unitCount,
                        MaxTeamsAllowed = unitCount,                      // cap at current units
                        MaxPeopleAllowed = unitCount * MaxPeoplePerTeam,
                        TrialStartDate = now,
                        TrialEndDate = defaultTrialEnd,
                        CurrentPeriodStart = now,
                        CurrentPeriodEnd = defaultTrialEnd,
                        IsTrialUsed = true,
                        CreatedAt = now,
                        UpdatedAt = now
                    };
                    await _subscriptionRepo.CreateAsync(sub); // writes CompanySubscriptionAuditLog
                }
                else
                {
                    sub = await _db.CompanySubscriptions.FirstAsync(s => s.Id == existingSub!.Id);
                    licenseExpiry = sub.CurrentPeriodEnd != default ? sub.CurrentPeriodEnd : defaultTrialEnd;

                    // Re-apply the unit-count cap ONLY for auto-trial companies we
                    // own. Never touch a paid / normal-trial company's tier.
                    if (canMint)
                    {
                        sub.TeamCount = unitCount;
                        sub.MaxTeamsAllowed = unitCount;
                        sub.MaxPeopleAllowed = unitCount * MaxPeoplePerTeam;
                        sub.UpdatedAt = now;
                        await _db.SaveChangesAsync();
                    }
                }

                if (missingUnitIds.Count == 0)
                {
                    await tx.CommitAsync();
                    return 0;
                }

                // Unique-key generator continuing past any existing keys.
                var keySet = new HashSet<string>(await _db.CompanyLicenses
                    .Where(l => l.ProfileId == profileId)
                    .Select(l => l.LicenseKey)
                    .ToListAsync());
                int seq = 1;
                string NextKey()
                {
                    string k;
                    do { k = $"CLD-TRI{seq:000}-{profileId}"; seq++; } while (!keySet.Add(k));
                    return k;
                }

                // Company's free (no active assignment) licenses — assigned first.
                var activeLicenseIds = await _db.AssignedCompanyLicenses
                    .Where(a => a.IsActive && !a.IsDel)
                    .Select(a => a.CompanyLicenseId)
                    .ToListAsync();
                var freeQueue = new Queue<CompanyLicense>(await _db.CompanyLicenses
                    .Where(l => l.ProfileId == profileId && !l.IsDel && l.TerminatedAt == null && !activeLicenseIds.Contains(l.Id))
                    .OrderBy(l => l.Id)
                    .ToListAsync());

                // Build (unit → license) pairs.
                var pairs = new List<(uint unitId, CompanyLicense lic, bool isNew)>(missingUnitIds.Count);
                foreach (var unitId in missingUnitIds)
                {
                    CompanyLicense lic;
                    bool isNew;
                    if (freeQueue.Count > 0)
                    {
                        lic = freeQueue.Dequeue();
                        if (lic.CompanySubscriptionId == null)
                            lic.CompanySubscriptionId = sub.Id;
                        // Only retune the license when we own it (legacy/auto-trial).
                        // Paid/normal licenses keep their existing type + expiry.
                        if (canMint)
                        {
                            lic.LicenseType = "Trial";
                            lic.ExpiryDate = licenseExpiry;
                        }
                        lic.UpdatedAt = now;
                        lic.UpdatedBy = actorUserId;
                        isNew = false;
                    }
                    else if (canMint)
                    {
                        lic = new CompanyLicense
                        {
                            ProfileId = profileId,
                            LicenseKey = NextKey(),
                            LicenseType = "Trial",
                            CompanySubscriptionId = sub.Id,
                            PlanId = null,
                            ExpiryDate = licenseExpiry,
                            CreatedBy = actorUserId,
                            UpdatedBy = actorUserId,
                            CreatedAt = now,
                            UpdatedAt = now
                        };
                        _db.CompanyLicenses.Add(lic);
                        isNew = true;
                    }
                    else
                    {
                        // Normal company out of free licenses — can't cover more
                        // units without buying. Stop here.
                        break;
                    }
                    pairs.Add((unitId, lic, isNew));
                }

                if (pairs.Count == 0)
                {
                    await tx.CommitAsync();
                    return 0;
                }

                await _db.SaveChangesAsync(); // populates Ids on new licenses, persists reused updates

                // Audit INSERT for newly minted licenses.
                var newLicenses = pairs.Where(p => p.isNew).Select(p => p.lic).ToList();
                if (newLicenses.Count > 0)
                {
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

                // One active assignment per pair.
                var assignments = pairs.Select(p => new AssignedCompanyLicense
                {
                    CompanyLicenseId = p.lic.Id,
                    BusinessUnitId = p.unitId,
                    AssignedAt = now,
                    IsActive = true,
                    CreatedBy = actorUserId,
                    UpdatedBy = actorUserId,
                    CreatedAt = now,
                    UpdatedAt = now
                }).ToList();
                _db.AssignedCompanyLicenses.AddRange(assignments);
                await _db.SaveChangesAsync();

                _db.AssignedCompanyLicenseAuditLogs.AddRange(assignments.Select(a => new AssignedCompanyLicenseAuditLog
                {
                    Id = a.Id,
                    CompanyLicenseId = a.CompanyLicenseId,
                    BusinessUnitId = a.BusinessUnitId,
                    AssignedAt = a.AssignedAt,
                    UnassignedAt = a.UnassignedAt,
                    IsActive = a.IsActive,
                    CreatedBy = a.CreatedBy,
                    UpdatedBy = a.UpdatedBy,
                    CreatedAt = a.CreatedAt,
                    UpdatedAt = a.UpdatedAt,
                    IsDel = a.IsDel,
                    LoggedAt = now,
                    VersionNumber = 1,
                    OperationType = "INSERT"
                }));
                await _db.SaveChangesAsync();

                await tx.CommitAsync();
                return pairs.Count;
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }
    }
}
