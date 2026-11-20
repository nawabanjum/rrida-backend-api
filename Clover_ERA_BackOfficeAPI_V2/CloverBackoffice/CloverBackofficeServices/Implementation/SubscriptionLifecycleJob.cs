using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Services.Implementation
{
    // Runs on schedule. License lifecycle is now driven by each license's OWN
    // ExpiryDate — INDEPENDENT of the subscription period. For every non-deleted
    // CompanySubscription:
    //   1. Subscription Status (billing/UI ONLY): flip Expired/Active based on
    //      CurrentPeriodEnd + grace. This NEVER touches licenses — a lapsed
    //      subscription does not disable any license.
    //   2. License expiry: any CompanyLicense whose own (grace-adjusted)
    //      ExpiryDate has passed → IsDel=true and its active assignment closed
    //      immediately; the notification follows ExpiryAnnounceDelayHours later
    //      (naming unit + license key), so a renewal landing right after period
    //      end never produces an "expired" mail.
    //   3. License reactivation: any license back in the future (renewal / admin
    //      extension) that WAS announced as expired → IsDel=false, last
    //      assignment reopened, one notification per restored unit. Never fires
    //      for a healthy recurring renewal, which extends the license before it
    //      is ever announced.
    //   4. Pre-expiry warning at {3,2,1} days before the NEAREST upcoming
    //      license expiry → per-unit in-app + email.
    //
    // Idempotent: each branch re-checks state before mutating.
    public class SubscriptionLifecycleJob
    {
        private const string JobName = "SubscriptionLifecycle";

        // Per-stage warning thresholds (days remaining). Listed high-to-low so
        // monotonic gating against LastWarningDaysSent reads naturally — each
        // stage fires only once per subscription as time decreases.
        private static readonly int[] WarningOffsets = new[] { 3, 2, 1 };

        // Quiet window between a license actually lapsing and ANNOUNCING that it
        // lapsed. Access still ends exactly on the expiry date — only the
        // notification waits.
        //
        // Why: a paid license expires at CurrentPeriodEnd, which is the same
        // moment Stripe bills the renewal. The customer.subscription.updated
        // webhook lands seconds-to-minutes later and extends the license
        // (SubscriptionService.ExtendLicensesToAsync). A tick falling in that gap
        // would mail "License Expired" for a subscription that renewed perfectly,
        // followed by "License Reactivated" on the next tick. The same gap covers
        // Stripe's dunning retries on a soft card decline.
        //
        // Six hours swallows webhook lag and same-day payment retries while still
        // announcing a genuine expiry on the following tick (job runs 00:00/12:00
        // UTC). Licenses renewed inside the window are never announced at all —
        // ExpiryNotifiedAt is never stamped, so the reactivation pass stays quiet
        // too.
        private const int ExpiryAnnounceDelayHours = 6;

        private readonly ApplicationDbContext _db;
        private readonly ISystemNotificationService _notifications;
        private readonly IAuditLogsService _auditLogs;
        private readonly SendGridSettings _sendGridSettings;

        public SubscriptionLifecycleJob(
            ApplicationDbContext db,
            ISystemNotificationService notifications,
            IAuditLogsService auditLogs,
            SendGridSettings sendGridSettings)
        {
            _db = db;
            _notifications = notifications;
            _auditLogs = auditLogs;
            _sendGridSettings = sendGridSettings;
        }

        // Per-unit context used to build clear, unit-named notification messages.
        private sealed record UnitContext(int LicenseId, uint UnitId, string UnitName, string LicenseKey);

        public async Task RunAsync()
        {
            try
            {
                await _auditLogs.LogHangfireJobAsync(0, JobName, "Started",
                    $"Tick at {DateTime.UtcNow:O}");

                var now = DateTime.UtcNow;

                var subs = await _db.CompanySubscriptions
                    .Where(s => !s.IsDel && s.Status != SubscriptionStatus.Canceled)
                    .ToListAsync();

                if (subs.Count == 0)
                {
                    await _auditLogs.LogHangfireJobAsync(0, JobName, "NoSubscriptions",
                        "No candidate subscriptions found.");
                    return;
                }

                foreach (var sub in subs)
                {
                    try
                    {
                        await ProcessSubscriptionAsync(sub, now);
                    }
                    catch (Exception ex)
                    {
                        await _auditLogs.LogHangfireJobAsync(0, JobName, "Error",
                            $"SubscriptionId={sub.Id} CompanyId={sub.ProfileId}: {ex.Message}");
                    }
                }

                await _auditLogs.LogHangfireJobAsync(0, JobName, "Finished",
                    $"Processed {subs.Count} subscriptions.");
            }
            catch (Exception ex)
            {
                await _auditLogs.LogHangfireJobAsync(0, JobName, "FatalError", ex.Message);
            }
        }

        private async Task ProcessSubscriptionAsync(CompanySubscription sub, DateTime now)
        {
            var subExpiry = ComputeSubscriptionExpiry(sub);

            await _auditLogs.LogHangfireJobAsync(0, JobName, "Visited",
                $"SubscriptionId={sub.Id} CompanyId={sub.ProfileId} Status={sub.Status} " +
                $"CurrentPeriodEnd={sub.CurrentPeriodEnd:O} GracePeriod={sub.GracePeriod ?? 0} " +
                $"SubExpiry={subExpiry:O} Now={now:O}");

            // 1. Subscription Status — billing/UI ONLY. A lapsed subscription
            //    never disables a license; licenses live by their own dates.
            if (sub.Status != SubscriptionStatus.Expired && subExpiry <= now)
            {
                sub.Status = SubscriptionStatus.Expired;
                sub.UpdatedAt = now;
                await _db.SaveChangesAsync();
                await _auditLogs.LogHangfireJobAsync(0, JobName, "SubExpired",
                    $"SubscriptionId={sub.Id} (billing only; licenses unaffected)");
            }
            else if (sub.Status == SubscriptionStatus.Expired && subExpiry > now)
            {
                sub.Status = SubscriptionStatus.Active;
                sub.LastWarningDaysSent = null;
                sub.UpdatedAt = now;
                await _db.SaveChangesAsync();
                await _auditLogs.LogHangfireJobAsync(0, JobName, "SubReactivated",
                    $"SubscriptionId={sub.Id} (billing only)");
            }

            // 2. License lifecycle — each license by its OWN ExpiryDate.
            await ProcessLicenseExpiryAsync(sub, now);
            await ProcessLicenseReactivationAsync(sub, now);

            // 3. Pre-expiry warnings — driven by the nearest upcoming license
            //    expiry on this subscription.
            await ProcessLicenseWarningsAsync(sub, now);
        }

        // Subscription billing expiry (CurrentPeriodEnd + grace). Drives only
        // the subscription's own Status flip — NOT the licenses.
        private static DateTime ComputeSubscriptionExpiry(CompanySubscription sub)
        {
            var baseEnd = sub.CurrentPeriodEnd;
            var grace = sub.GracePeriod ?? 0;
            return grace > 0 ? baseEnd.AddDays(grace) : baseEnd;
        }

        // Effective per-license expiry = the license's own ExpiryDate + the
        // subscription-level grace. A license with no ExpiryDate never expires.
        private static DateTime? ComputeLicenseExpiry(CompanyLicense lic, CompanySubscription sub)
        {
            if (!lic.ExpiryDate.HasValue) return null;
            var grace = sub.GracePeriod ?? 0;
            return grace > 0 ? lic.ExpiryDate.Value.AddDays(grace) : lic.ExpiryDate.Value;
        }

        // Two distinct steps, deliberately on different clocks:
        //   1. STATE — a license whose (grace-adjusted) expiry has passed is
        //      soft-deleted and its assignment closed IMMEDIATELY. Access must
        //      end on the date the customer paid through, not hours later.
        //   2. ANNOUNCEMENT — the notification/email waits
        //      ExpiryAnnounceDelayHours past that expiry, so a renewal webhook
        //      arriving moments after period end (or a same-day dunning retry)
        //      extends the license before anyone is told it lapsed.
        private async Task ProcessLicenseExpiryAsync(CompanySubscription sub, DateTime now)
        {
            await ExpireDueLicensesAsync(sub, now);
            await AnnounceExpiredLicensesAsync(sub, now);
        }

        // STEP 1 — state only, no notification. Idempotent: licenses the read-path
        // reconciler already expired are simply not in the candidate set.
        private async Task ExpireDueLicensesAsync(CompanySubscription sub, DateTime now)
        {
            var candidates = await _db.CompanyLicenses
                .Where(l => l.CompanySubscriptionId == sub.Id && !l.IsDel && l.ExpiryDate != null)
                .ToListAsync();

            var due = candidates
                .Where(l => ComputeLicenseExpiry(l, sub) is DateTime e && e <= now)
                .ToList();
            if (due.Count == 0) return;

            var ids = due.Select(l => l.Id).ToList();

            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                foreach (var lic in due) { lic.IsDel = true; lic.UpdatedAt = now; }

                var active = await _db.AssignedCompanyLicenses
                    .Where(a => ids.Contains(a.CompanyLicenseId) && a.IsActive && !a.IsDel)
                    .ToListAsync();
                foreach (var a in active)
                {
                    a.IsActive = false;
                    a.UnassignedAt = now;
                    a.UpdatedAt = now;
                }

                await _db.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }

            await _auditLogs.LogHangfireJobAsync(0, JobName, "LicensesExpired",
                $"SubscriptionId={sub.Id} CompanyId={sub.ProfileId} count={due.Count} (state only)");
        }

        // STEP 2 — announce once per license, ExpiryAnnounceDelayHours after the
        // license actually lapsed.
        //
        // Selection is driven by ExpiryNotifiedAt, NOT by IsDel: the read-path
        // reconciler (SubscriptionService.ReconcileLicenseExpiryAsync) also flips
        // IsDel whenever a subscription page is loaded, so a license is very often
        // already soft-deleted by the time this job ticks. Keying off IsDel meant
        // those licenses were never announced. Any lapsed license that has not
        // been announced yet is picked up here, whoever expired it.
        //
        // A license renewed inside the quiet window is never announced: its
        // ExpiryDate has moved into the future, so it drops out of this query.
        private async Task AnnounceExpiredLicensesAsync(CompanySubscription sub, DateTime now)
        {
            var candidates = await _db.CompanyLicenses
                .Where(l => l.CompanySubscriptionId == sub.Id
                         && l.ExpiryDate != null
                         && l.ExpiryNotifiedAt == null)
                .ToListAsync();

            var due = candidates
                .Where(l => ComputeLicenseExpiry(l, sub) is DateTime e
                            && e.AddHours(ExpiryAnnounceDelayHours) <= now)
                .ToList();
            if (due.Count == 0) return;

            var ids = due.Select(l => l.Id).ToList();
            // activeOnly:false — by the time a license is announced its assignment
            // is already closed (by step 1 or by the read path), and the unit still
            // has to be named in the notification.
            var affectedUnits = await LoadUnitsForLicensesAsync(ids, activeOnly: false);

            // Stamp before dispatching so a crash mid-fan-out can't re-announce
            // the whole batch on the next tick. Clearing ReactivationNotifiedAt
            // re-arms the reactivation notification for the next renewal.
            foreach (var lic in due)
            {
                lic.ExpiryNotifiedAt = now;
                lic.ReactivationNotifiedAt = null;
            }
            await _db.SaveChangesAsync();

            await _auditLogs.LogHangfireJobAsync(0, JobName, "LicensesExpiredAnnounced",
                $"SubscriptionId={sub.Id} CompanyId={sub.ProfileId} count={due.Count} " +
                $"units={affectedUnits.Count} delayHours={ExpiryAnnounceDelayHours}");

            var unitByLicenseId = affectedUnits.ToDictionary(u => u.LicenseId, u => u);

            foreach (var lic in due)
            {
                // The license's OWN effective expiry, not the tick time — rows the
                // read path expired earlier would otherwise be reported as having
                // expired today.
                var expiredOn = (ComputeLicenseExpiry(lic, sub) ?? now).ToString("yyyy-MM-dd");

                // A license that was never assigned to a unit still has to be
                // announced — otherwise an expiring spare seat dies silently.
                if (unitByLicenseId.TryGetValue(lic.Id, out var unit))
                {
                    await DispatchNotificationsAsync(
                        sub,
                        title: $"License Expired - {unit.UnitName}",
                        message:
                            $"The license assigned to unit '{unit.UnitName}' (key: {unit.LicenseKey}) " +
                            $"reached its expiry date on {expiredOn} and is now unassigned. " +
                            "Purchase or renew a license to reactivate this unit's access.",
                        templateName: "SubscriptionExpired",
                        templateId: _sendGridSettings.CompanyNotificationTemplateId,
                        subject: $"License Expired for {unit.UnitName} - RRIDA",
                        extraEmailData: new Dictionary<string, object>
                        {
                            { "unit_name", unit.UnitName },
                            { "license_key", unit.LicenseKey },
                            { "expiry_date", expiredOn }
                        });
                }
                else
                {
                    await DispatchNotificationsAsync(
                        sub,
                        title: "License Expired",
                        message:
                            $"The unassigned license (key: {lic.LicenseKey}) reached its expiry " +
                            $"date on {expiredOn}. Renew the subscription to keep this seat available.",
                        templateName: "SubscriptionExpired",
                        templateId: _sendGridSettings.CompanyNotificationTemplateId,
                        subject: "License Expired - RRIDA",
                        extraEmailData: new Dictionary<string, object>
                        {
                            { "license_key", lic.LicenseKey },
                            { "expiry_date", expiredOn }
                        });
                }
            }
        }

        // Revive any license whose ExpiryDate has been pushed into the future
        // (renewal / admin extension) and reopen its last assignment.
        //
        // Same rule as the expiry pass: selection keys off the notification
        // stamps, not IsDel, so a license the read path already revived is still
        // announced. Only licenses that were previously announced as expired
        // (ExpiryNotifiedAt != null) qualify — a seat that silently expired and
        // came back before anyone was told needs no "reactivated" message.
        private async Task ProcessLicenseReactivationAsync(CompanySubscription sub, DateTime now)
        {
            var candidates = await _db.CompanyLicenses
                .Where(l => l.CompanySubscriptionId == sub.Id
                         && l.ExpiryDate != null
                         && l.ExpiryNotifiedAt != null
                         && l.ReactivationNotifiedAt == null
                         && l.TerminatedAt == null)
                .ToListAsync();

            var revivable = candidates
                .Where(l => ComputeLicenseExpiry(l, sub) is DateTime e && e > now)
                .ToList();
            if (revivable.Count == 0) return;

            var ids = revivable.Select(l => l.Id).ToList();
            var toFlip = revivable.Where(l => l.IsDel).ToList();

            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                foreach (var lic in toFlip) { lic.IsDel = false; lic.UpdatedAt = now; }

                if (toFlip.Count > 0)
                {
                    var flipIds = toFlip.Select(l => l.Id).ToList();
                    var last = await _db.AssignedCompanyLicenses
                        .Where(a => flipIds.Contains(a.CompanyLicenseId) && !a.IsDel)
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

                // Re-arm the expiry notification for the next cycle.
                foreach (var lic in revivable)
                {
                    lic.ReactivationNotifiedAt = now;
                    lic.ExpiryNotifiedAt = null;
                }

                await _db.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }

            var restoredUnits = await LoadUnitsForLicensesAsync(ids, activeOnly: false);

            await _auditLogs.LogHangfireJobAsync(0, JobName, "LicensesReactivated",
                $"SubscriptionId={sub.Id} CompanyId={sub.ProfileId} count={revivable.Count} " +
                $"flippedHere={toFlip.Count} alreadyRevivedByReadPath={revivable.Count - toFlip.Count} " +
                $"units={restoredUnits.Count}");

            var unitByLicenseId = restoredUnits.ToDictionary(u => u.LicenseId, u => u);

            foreach (var lic in revivable)
            {
                if (unitByLicenseId.TryGetValue(lic.Id, out var unit))
                {
                    await DispatchNotificationsAsync(
                        sub,
                        title: $"License Reactivated - {unit.UnitName}",
                        message:
                            $"The license for unit '{unit.UnitName}' (key: {unit.LicenseKey}) " +
                            "has been reactivated and is usable again across the system.",
                        templateName: "SubscriptionReactivated",
                        templateId: _sendGridSettings.CompanyNotificationTemplateId,
                        subject: $"License Reactivated for {unit.UnitName} - RRIDA",
                        extraEmailData: new Dictionary<string, object>
                        {
                            { "unit_name", unit.UnitName },
                            { "license_key", unit.LicenseKey }
                        });
                }
                else
                {
                    await DispatchNotificationsAsync(
                        sub,
                        title: "License Reactivated",
                        message:
                            $"The license (key: {lic.LicenseKey}) has been reactivated and is " +
                            "available to assign to a team again.",
                        templateName: "SubscriptionReactivated",
                        templateId: _sendGridSettings.CompanyNotificationTemplateId,
                        subject: "License Reactivated - RRIDA",
                        extraEmailData: new Dictionary<string, object>
                        {
                            { "license_key", lic.LicenseKey }
                        });
                }
            }
        }

        // Pre-expiry warning at {3,2,1} days before the NEAREST upcoming license
        // expiry on this subscription. Reuses sub.LastWarningDaysSent as the
        // idempotency gate (monotonic 3→2→1).
        private async Task ProcessLicenseWarningsAsync(CompanySubscription sub, DateTime now)
        {
            var live = await _db.CompanyLicenses
                .Where(l => l.CompanySubscriptionId == sub.Id && !l.IsDel && l.ExpiryDate != null)
                .ToListAsync();
            if (live.Count == 0) return;

            var upcoming = live
                .Select(l => ComputeLicenseExpiry(l, sub))
                .Where(e => e.HasValue && e.Value > now)
                .Select(e => e!.Value)
                .ToList();
            if (upcoming.Count == 0) return;

            var nearest = upcoming.Min();
            var daysRemaining = (int)Math.Floor((nearest - now).TotalDays);
            if (!WarningOffsets.Contains(daysRemaining)) return;

            if (sub.LastWarningDaysSent.HasValue && daysRemaining >= sub.LastWarningDaysSent.Value)
            {
                await _auditLogs.LogHangfireJobAsync(0, JobName, "Skipped",
                    $"SubscriptionId={sub.Id} reason=WarningAlreadySent " +
                    $"(daysRemaining={daysRemaining} LastWarningDaysSent={sub.LastWarningDaysSent})");
                return;
            }

            await SendExpiryWarningAsync(sub, nearest, daysRemaining);
        }

        // Loads the units currently holding one of the given licenses. Joins
        // AssignedCompanyLicenses ⋈ CompanyLicense ⋈ BusinessUnit so the
        // notification can name the exact unit + license key affected.
        // `activeOnly=true` filters to currently-assigned rows; `false` returns
        // the latest assignment per license regardless of IsActive.
        private async Task<List<UnitContext>> LoadUnitsForLicensesAsync(List<int> licenseIds, bool activeOnly)
        {
            if (licenseIds.Count == 0) return new List<UnitContext>();

            var query =
                from a in _db.AssignedCompanyLicenses
                join l in _db.CompanyLicenses on a.CompanyLicenseId equals l.Id
                join u in _db.BusinessUnits on a.BusinessUnitId equals u.Id
                where licenseIds.Contains(a.CompanyLicenseId)
                      && !a.IsDel
                      && (!activeOnly || a.IsActive)
                select new
                {
                    a.CompanyLicenseId,
                    a.AssignedAt,
                    UnitId = u.Id,
                    UnitName = u.Name,
                    l.LicenseKey
                };

            var rows = await query.ToListAsync();

            return rows
                .GroupBy(r => r.CompanyLicenseId)
                .Select(g => g.OrderByDescending(r => r.AssignedAt).First())
                .Select(r => new UnitContext(r.CompanyLicenseId, r.UnitId, r.UnitName, r.LicenseKey))
                .ToList();
        }

        // Loads units holding ANY license under the subscription (used by the
        // warning path). Active assignments only.
        private async Task<List<UnitContext>> LoadAffectedUnitsAsync(int subscriptionId, bool activeOnly)
        {
            var licenseIds = await _db.CompanyLicenses
                .Where(l => l.CompanySubscriptionId == subscriptionId)
                .Select(l => l.Id)
                .ToListAsync();
            return await LoadUnitsForLicensesAsync(licenseIds, activeOnly);
        }

        private async Task SendExpiryWarningAsync(CompanySubscription sub, DateTime expiry, int daysRemaining)
        {
            var affectedUnits = await LoadAffectedUnitsAsync(sub.Id, activeOnly: true);

            // Persist the stage BEFORE dispatching so a crash mid-fan-out can't
            // produce a re-send on the next tick. Worst case is one missed
            // notification, which is better than spamming the user.
            sub.LastWarningDaysSent = daysRemaining;
            sub.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            await _auditLogs.LogHangfireJobAsync(0, JobName, "Warning",
                $"SubscriptionId={sub.Id} CompanyId={sub.ProfileId} " +
                $"daysRemaining={daysRemaining} units={affectedUnits.Count}");

            var companyName = await GetCompanyNameAsync(sub.ProfileId);
            var expiryStr = expiry.ToString("yyyy-MM-dd");

            if (affectedUnits.Count == 0)
            {
                await DispatchNotificationsAsync(
                    sub,
                    title: $"Subscription Expiring in {daysRemaining} day(s)",
                    message: $"The subscription for {companyName} expires on {expiryStr}. " +
                             "Please renew to avoid service interruption.",
                    templateName: "SubscriptionExpiryWarning",
                    templateId: _sendGridSettings.CompanyNotificationTemplateId,
                    subject: $"Subscription Expiring in {daysRemaining} Day(s) - RRIDA",
                    extraEmailData: new Dictionary<string, object>
                    {
                        { "days_remaining", daysRemaining },
                        { "expiry_date", expiryStr }
                    });
                return;
            }

            foreach (var unit in affectedUnits)
            {
                var title = $"License Expiring in {daysRemaining} day(s) - {unit.UnitName}";
                var message =
                    $"The license assigned to unit '{unit.UnitName}' (key: {unit.LicenseKey}) " +
                    $"will expire on {expiryStr} ({daysRemaining} day(s) remaining). " +
                    "Please renew the subscription to keep this unit active.";

                await DispatchNotificationsAsync(
                    sub,
                    title: title,
                    message: message,
                    templateName: "SubscriptionExpiryWarning",
                    templateId: _sendGridSettings.CompanyNotificationTemplateId,
                    subject: $"License Expiring for {unit.UnitName} - RRIDA",
                    extraEmailData: new Dictionary<string, object>
                    {
                        { "unit_name", unit.UnitName },
                        { "license_key", unit.LicenseKey },
                        { "days_remaining", daysRemaining },
                        { "expiry_date", expiryStr }
                    });
            }
        }

        private async Task<string> GetCompanyNameAsync(uint profileId)
        {
            return await _db.Profiles
                .Where(c => c.Id == profileId)
                .Select(c => c.ProfileName)
                .FirstOrDefaultAsync() ?? string.Empty;
        }

        // Writes one SystemNotification row per Clovera admin and per company admin,
        // then best-effort emails the company admins. Title/message already include
        // unit name when applicable, so recipients see exactly what's affected.
        private async Task DispatchNotificationsAsync(
            CompanySubscription sub,
            string title,
            string message,
            string templateName,
            string templateId,
            string subject,
            Dictionary<string, object>? extraEmailData = null)
        {
            var cloveraAdminCount = await _db.Users
                .CountAsync(u => u.UserGroupId == 2 && !u.IsDel);

            await _notifications.CreateNotificationForAdminsAsync(new CreateSystemNotificationDto
            {
                Title = title,
                Message = $"[Company {sub.ProfileId}] {message}",
                NotificationType = "Subscription"
            });

            var companyAdmins = await _db.Users
                .Where(u => u.ProfileId == (int)sub.ProfileId && u.UserGroupId == 1 && !u.IsDel)
                .Select(u => new { u.UserId, u.Username })
                .ToListAsync();

            await _auditLogs.LogHangfireJobAsync(0, JobName, "NotifyRecipients",
                $"SubscriptionId={sub.Id} CompanyId={sub.ProfileId} " +
                $"cloveraAdmins={cloveraAdminCount} companyAdmins={companyAdmins.Count} title=\"{title}\"");

            if (companyAdmins.Count == 0) return;

            await _notifications.CreateNotificationForCompanyAdminsAsync(
                companyAdmins.First().UserId,
                new CreateSystemNotificationDto
                {
                    Title = title,
                    Message = message,
                    NotificationType = "Subscription"
                });

            var companyName = await GetCompanyNameAsync(sub.ProfileId);

            foreach (var admin in companyAdmins)
            {
                if (string.IsNullOrWhiteSpace(admin.Username)) continue;

                var dynamicData = new Dictionary<string, object>
                {
                    { "company_name", companyName },
                    { "message", message },
                    { "title", title }
                };
                if (extraEmailData != null)
                {
                    foreach (var kv in extraEmailData) dynamicData[kv.Key] = kv.Value;
                }

                try
                {
                    await EmailHelper.SendEmailAsync(
                        _sendGridSettings,
                        admin.Username,
                        subject,
                        templateName,
                        templateId,
                        dynamicData);
                }
                catch (Exception ex)
                {
                    await _auditLogs.LogHangfireJobAsync(0, JobName, "EmailError",
                        $"SubscriptionId={sub.Id} to={admin.Username}: {ex.Message}");
                }
            }
        }
    }
}
