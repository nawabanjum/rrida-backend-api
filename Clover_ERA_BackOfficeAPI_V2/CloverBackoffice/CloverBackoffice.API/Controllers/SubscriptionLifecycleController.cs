using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Implementation;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.API.Controllers
{
    // Manual trigger + diagnostics for SubscriptionLifecycleJob. Lets an admin
    // force the expiry/reactivation sweep and inspect what the job would do
    // without hitting the DB directly.
    [Route("api/subscription-lifecycle")]
    [ApiExplorerSettings(IgnoreApi = true)]
    [ApiController]
    [Authorize]
    public class SubscriptionLifecycleController : ControllerBase
    {
        private readonly SubscriptionLifecycleJob _job;
        private readonly ApplicationDbContext _db;
        private readonly ILegacyTrialBackfillService _legacyBackfill;

        public SubscriptionLifecycleController(
            SubscriptionLifecycleJob job,
            ApplicationDbContext db,
            ILegacyTrialBackfillService legacyBackfill)
        {
            _job = job;
            _db = db;
            _legacyBackfill = legacyBackfill;
        }

        [HttpPost("run")]
        public async Task<IActionResult> Run()
        {
            try
            {
                await _job.RunAsync();
                return Ok(ApiResponseHandler.Success<string>(
                    "ok",
                    "Subscription lifecycle sweep finished. Check LLMLogs (Endpoint=Hangfire/SubscriptionLifecycle) for per-subscription outcomes."));
            }
            catch (Exception ex)
            {
                return Ok(ApiResponseHandler.Fail<string>(
                    $"{ResponseMessages.Errors.ExceptionMsg} :: {ex.GetBaseException().Message}"));
            }
        }

        // Read-only snapshot of what the job WOULD do right now for every non-deleted
        // subscription. Mirrors ComputeEffectiveExpiry + ProcessSubscriptionAsync
        // logic without writing anything. Includes recipient counts so you can see
        // when "no notification" is caused by zero matching admins, not a job bug.
        [HttpGet("diagnose")]
        public async Task<IActionResult> Diagnose()
        {
            try
            {
                var now = DateTime.UtcNow;
                int[] warningOffsets = { 7, 3, 1 };

                var cloveraAdminCount = await _db.Users
                    .CountAsync(u => u.UserGroupId == 2 && !u.IsDel);

                var subs = await _db.CompanySubscriptions
                    .Where(s => !s.IsDel)
                    .OrderBy(s => s.Id)
                    .ToListAsync();

                var rows = new List<object>();
                foreach (var s in subs)
                {
                    var baseEnd = s.CurrentPeriodEnd;
                    var grace = s.GracePeriod ?? 0;
                    var expiry = grace > 0 ? baseEnd.AddDays(grace) : baseEnd;
                    var daysRemaining = (int)Math.Floor((expiry - now).TotalDays);

                    string action;
                    string reason;
                    if (s.Status == SubscriptionStatus.Canceled)
                    {
                        action = "Skip";
                        reason = "Status=Canceled (lifecycle job ignores canceled subs)";
                    }
                    else if (s.Status == SubscriptionStatus.Expired && expiry > now)
                    {
                        action = "Reactivate";
                        reason = "Status=Expired but period-end moved past now (renewal detected)";
                    }
                    else if (s.Status != SubscriptionStatus.Expired && expiry <= now)
                    {
                        action = "Expire";
                        reason = $"Status={s.Status} and expiry {expiry:O} <= now {now:O}";
                    }
                    else if ((s.Status == SubscriptionStatus.Active ||
                              s.Status == SubscriptionStatus.Trial ||
                              s.Status == SubscriptionStatus.PastDue) &&
                             warningOffsets.Contains(daysRemaining))
                    {
                        action = $"Warning({daysRemaining}d)";
                        reason = $"daysRemaining={daysRemaining} matches warning threshold";
                    }
                    else if (s.Status == SubscriptionStatus.Expired)
                    {
                        action = "Skip";
                        reason = "Already Expired and no renewal yet";
                    }
                    else
                    {
                        action = "Skip";
                        reason = $"daysRemaining={daysRemaining} not in warning window {string.Join(",", warningOffsets)}";
                    }

                    var companyAdminCount = await _db.Users
                        .CountAsync(u => u.ProfileId == (int)s.ProfileId && u.UserGroupId == 1 && !u.IsDel);

                    var assignedUnits = await (
                        from a in _db.AssignedCompanyLicenses
                        join l in _db.CompanyLicenses on a.CompanyLicenseId equals l.Id
                        join u in _db.BusinessUnits on a.BusinessUnitId equals u.Id
                        where l.CompanySubscriptionId == s.Id && !a.IsDel && a.IsActive
                        select new { UnitId = u.Id, UnitName = u.Name, l.LicenseKey, l.IsDel, a.IsActive, a.UnassignedAt }
                    ).ToListAsync();

                    rows.Add(new
                    {
                        s.Id,
                        s.ProfileId,
                        Status = s.Status.ToString(),
                        s.CurrentPeriodStart,
                        s.CurrentPeriodEnd,
                        s.TrialStartDate,
                        s.TrialEndDate,
                        s.GracePeriod,
                        ComputedExpiry = expiry,
                        DaysRemaining = daysRemaining,
                        Action = action,
                        Reason = reason,
                        CloveraAdminCount = cloveraAdminCount,
                        CompanyAdminCount = companyAdminCount,
                        ActiveAssignments = assignedUnits
                    });
                }

                return Ok(ApiResponseHandler.Success(new
                {
                    NowUtc = now,
                    TotalSubscriptions = subs.Count,
                    Subscriptions = rows
                }, "Diagnose snapshot."));
            }
            catch (Exception ex)
            {
                return Ok(ApiResponseHandler.Fail<string>(
                    $"{ResponseMessages.Errors.ExceptionMsg} :: {ex.GetBaseException().Message}"));
            }
        }

        // ONE-TIME migration for legacy companies that predate the subscription
        // system — i.e. a non-deleted Company with NO non-deleted row in
        // CompanySubscriptions. Each such company is moved onto a Trial:
        //   * Create a Trial CompanySubscription (30 days by default), with
        //     TeamCount / MaxTeamsAllowed sized to the company's CURRENT unit
        //     count so NO tier limit (1/5/10) is applied — whatever they have
        //     is covered.
        //   * Mint one Trial CompanyLicense per existing unit, ExpiryDate = the
        //     trial end (each license owns its lifecycle).
        //   * Assign each license to its unit (IsActive=true).
        // Idempotent: companies that already have a subscription are skipped, so
        // re-running is safe. Pass dryRun=true to preview without writing.
        [HttpPost("backfill-legacy-trial")]
        public async Task<IActionResult> BackfillLegacyTrial(
            [FromQuery] int trialDays = 30,
            [FromQuery] bool dryRun = false)
        {
            try
            {
                var result = await _legacyBackfill.RunAsync(trialDays, dryRun);
                return Ok(ApiResponseHandler.Success(result,
                    dryRun
                        ? "Dry run — no changes written."
                        : $"Backfill finished. Migrated {result.LegacyCompanyCount} legacy company(ies)."));
            }
            catch (Exception ex)
            {
                return Ok(ApiResponseHandler.Fail<string>(
                    $"{ResponseMessages.Errors.ExceptionMsg} :: {ex.GetBaseException().Message}"));
            }
        }
    }
}
