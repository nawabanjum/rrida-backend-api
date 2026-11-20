using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CloverBackoffice.Components;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CloverBackoffice.Services.Implementation
{
    // Sends the two new-team nudges to the team's manager:
    //
    //   Day 2 — "you've had X responses yesterday"
    //   Day 5 — "your average is X responses per day, log in to see your scores"
    //
    // Design notes worth knowing before changing anything here:
    //
    // * "Response" means the same thing it means everywhere else in the product
    //   — one DailyFeedback row, counted only while the team was licensed. The
    //   count is taken through ILicensedPeriodService so the number in the email
    //   matches the TotalResponses the manager sees on the Team Structure page.
    //   A different count here would look like a bug to whoever compares them.
    //
    // * Only PAID teams are notified: a team riding a complimentary grant is
    //   excluded, as is a team with no live purchased licence.
    //
    // * "Already sent" is recorded as an LLMLogs audit row rather than a new
    //   column, so this feature needs no database change. The same rows double
    //   as the audit trail for support.
    public class TeamOnboardingNotificationService : ITeamOnboardingNotificationService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILicensedPeriodService _licensedPeriod;
        private readonly ISystemNotificationService _systemNotification;
        private readonly IAuditLogsService _auditLogs;
        private readonly SendGridSettings _sendGridSettings;
        private readonly TeamOnboardingNotificationOptions _options;
        private readonly ILogger<TeamOnboardingNotificationService> _logger;

        public TeamOnboardingNotificationService(
            ApplicationDbContext context,
            ILicensedPeriodService licensedPeriod,
            ISystemNotificationService systemNotification,
            IAuditLogsService auditLogs,
            SendGridSettings sendGridSettings,
            IOptions<TeamOnboardingNotificationOptions> options,
            ILogger<TeamOnboardingNotificationService> logger)
        {
            _context = context;
            _licensedPeriod = licensedPeriod;
            _systemNotification = systemNotification;
            _auditLogs = auditLogs;
            _sendGridSettings = sendGridSettings;
            _options = options?.Value ?? new TeamOnboardingNotificationOptions();
            _logger = logger;
        }

        public async Task<int> RunAsync(CancellationToken ct = default)
        {
            if (!_options.Enabled)
                return 0;

            var now = DateTime.UtcNow;
            var sent = 0;

            // The clock starts when the team FIRST gets a manager, not when the
            // team is created.
            //
            // upsert-team creates a team with no manager at all — the manager is
            // assigned afterwards through assign-user-to-team. Anchoring on
            // BusinessUnit.RegistrationDate would therefore count days during
            // which there was nobody to email, and a manager assigned on day 4
            // would get "you've had X responses yesterday" as their very first
            // contact, or miss the sequence entirely.
            //
            // Taking the EARLIEST assignment (not the current one) means a later
            // manager swap does not restart onboarding for an established team.
            var oldestDue = _options.Day5AfterDays + _options.CatchUpDays;
            var earliestAnchor = now.Date.AddDays(-(oldestDue + 1));

            var anchors = await _context.UserTeams
                .AsNoTracking()
                .GroupBy(ut => ut.UnitId)
                .Select(g => new
                {
                    UnitId = g.Key,
                    FirstAssignedAt = g.Min(x => x.CreatedDate),
                    // A team whose manager has since been removed has nobody to
                    // notify. Without this it stayed "due" and logged a skip on
                    // every single run until its window closed.
                    HasManagerNow = g.Any(x => !x.IsUnAssigned)
                })
                .Where(x => x.FirstAssignedAt >= earliestAnchor && x.HasManagerNow)
                .ToListAsync(ct);

            if (anchors.Count == 0)
                return 0;

            var anchoredUnitIds = anchors.Select(a => a.UnitId).ToList();

            // ── Assignment notifications: catch-up pass ──────────────────────
            //
            // The "a team has been assigned to you" message is normally sent
            // in-request by CompanyTeamService.AssignUserToTeamAsync. This pass
            // re-sends any that never landed — an exception, a mail provider
            // outage, or a deploy that happened between the assignment and the
            // notification. NotifyManagerAssignedAsync is idempotent per
            // (team, manager), so a message already sent is not repeated.
            //
            // Deliberately NOT paid-gated and NOT limited to the paid candidate
            // set below: knowing you have been given a team does not depend on
            // the team being licensed.
            var assignmentCutoff = now.Date.AddDays(-(_options.CatchUpDays + 1));

            // Joined to BusinessUnits on purpose: UserTeams contains rows
            // pointing at units that no longer exist (or never did). Without the
            // join those stale rows re-entered this loop on every run and logged
            // a "no BusinessUnits row" line each time, burying the real entries.
            var recentAssignments = await _context.UserTeams
                .AsNoTracking()
                .Where(ut => !ut.IsUnAssigned && ut.CreatedDate >= assignmentCutoff)
                .Join(_context.BusinessUnits.Where(bu => bu.IsDel != true),
                      ut => ut.UnitId,
                      bu => bu.Id,
                      (ut, bu) => new { ut.UnitId, ut.UserId, ut.CreatedDate, ut.Id })
                .ToListAsync(ct);

            // ── Unassignment notifications: catch-up pass ────────────────────
            //
            // Mirrors the assignment catch-up. Bounded by UserTeams.CreatedDate
            // because the table has no UnassignedAt column — so this covers a
            // manager assigned and then removed within the window (which is the
            // common churn case, and every test), but NOT a long-standing
            // manager removed today. The in-request hook on the unassign
            // endpoint is what covers that; this is the safety net.
            //
            // Without the date bound the first run would email every historical
            // unassignment in the database, so the bound is deliberate.
            var recentlyUnassigned = await _context.UserTeams
                .AsNoTracking()
                .Where(ut => ut.IsUnAssigned && ut.CreatedDate >= assignmentCutoff)
                .Join(_context.BusinessUnits.Where(bu => bu.IsDel != true),
                      ut => ut.UnitId,
                      bu => bu.Id,
                      (ut, bu) => new { ut.UnitId, ut.UserId })
                .Distinct()
                .ToListAsync(ct);

            foreach (var row in recentlyUnassigned)
            {
                try
                {
                    // A user re-assigned to the same team after being removed is
                    // currently active there — they have not lost it, so no
                    // "you have been unassigned" message.
                    var stillActive = await _context.UserTeams
                        .AsNoTracking()
                        .AnyAsync(ut => ut.UnitId == row.UnitId && ut.UserId == row.UserId && !ut.IsUnAssigned, ct);

                    if (stillActive)
                        continue;

                    if (await AlreadyNotifiedOfUnassignmentAsync(row.UnitId, row.UserId, ct))
                        continue;

                    if (await NotifyManagerUnassignedAsync(row.UserId, row.UnitId, ct))
                        sent++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[TeamOnboarding] Unassignment catch-up failed for UnitId={UnitId}", row.UnitId);
                }
            }

            foreach (var group in recentAssignments.GroupBy(a => a.UnitId))
            {
                // Only the current manager of each unit, not everyone who has
                // ever held it.
                var current = group.OrderByDescending(a => a.CreatedDate).ThenByDescending(a => a.Id).First();

                try
                {
                    // Checked here and skipped silently: this pass revisits every
                    // recent assignment on every run, so letting the already-sent
                    // case fall through would write an audit row per team per
                    // run. The audited skip inside NotifyManagerAssignedAsync is
                    // for the in-request hook, where somebody is watching.
                    if (await AlreadyNotifiedOfAssignmentAsync(current.UnitId, current.UserId, ct))
                        continue;

                    if (await NotifyManagerAssignedAsync(current.UserId, current.UnitId, ct))
                        sent++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[TeamOnboarding] Assignment catch-up failed for UnitId={UnitId}", current.UnitId);
                }
            }

            var candidates = await _context.BusinessUnits
                .AsNoTracking()
                .Where(bu => bu.IsDel != true && anchoredUnitIds.Contains(bu.Id))
                .Select(bu => new
                {
                    UnitId = bu.Id,
                    bu.Name,
                    ProfileId = (long)bu.Profile.Id
                })
                .ToListAsync(ct);

            if (candidates.Count == 0)
                return 0;

            var anchorByUnit = anchors.ToDictionary(a => a.UnitId, a => a.FirstAssignedAt);

            // Paid-only gate. GetActivelyLicensedUnitIdsAsync covers purchased
            // assignments AND complimentary grants, so the complimentary set is
            // subtracted to leave the teams actually paying for a licence.
            var candidateIds = candidates.Select(c => (long)c.UnitId).ToList();
            var licensed = await _licensedPeriod.GetActivelyLicensedUnitIdsAsync(candidateIds);
            var complimentary = await _licensedPeriod.GetActivelyComplimentaryUnitIdsAsync(candidateIds);
            var paidUnitIds = licensed.Except(complimentary).ToHashSet();

            if (paidUnitIds.Count == 0)
                return 0;

            var paidCandidates = candidates.Where(c => paidUnitIds.Contains((long)c.UnitId)).ToList();

            // Licensed intervals for the response counts, fetched once for the
            // whole batch rather than per team.
            var intervals = await _licensedPeriod.GetLicensedIntervalsAsync(
                paidCandidates.Select(c => (long)c.UnitId));

            foreach (var team in paidCandidates)
            {
                ct.ThrowIfCancellationRequested();

                var anchor = anchorByUnit[team.UnitId];
                var age = (now.Date - anchor.Date).TotalDays;

                try
                {
                    if (IsDue(age, _options.Day2AfterDays) &&
                        !await AlreadySentAsync(team.UnitId, TeamOnboardingNotificationOptions.Day2SentStatus, ct))
                    {
                        if (await SendDay2Async(team.UnitId, team.Name, team.ProfileId, intervals, now, ct))
                            sent++;
                    }

                    if (IsDue(age, _options.Day5AfterDays) &&
                        !await AlreadySentAsync(team.UnitId, TeamOnboardingNotificationOptions.Day5SentStatus, ct))
                    {
                        if (await SendDay5Async(team.UnitId, team.Name, team.ProfileId, anchor, intervals, now, ct))
                            sent++;
                    }
                }
                catch (Exception ex)
                {
                    // One team's failure must not stop the rest of the batch.
                    // Nothing is marked as sent, so the next run retries it —
                    // within the catch-up window.
                    _logger.LogError(ex, "[TeamOnboarding] Failed for UnitId={UnitId}", team.UnitId);
                    await _auditLogs.LogHangfireJobAsync(team.UnitId, TeamOnboardingNotificationOptions.JobName,
                        "Error", $"UnitId={team.UnitId}: {ex.GetType().Name}: {ex.Message}");
                }
            }

            sent += await SendNoResponseAlertsAsync(now, ct);
            sent += await SendActionHubIdleAlertsAsync(now, ct);

            return sent;
        }

        // ── Action Hub inactivity ────────────────────────────────────────────
        //
        // "Nothing has moved on your actions for N days."
        //
        // Activity is the MAX of ActionHub.CreatedAt, ActionHub.UpdatedAt and
        // its events' CreatedAt. UpdatedAt alone is not enough: creating an
        // action sets only CreatedAt (ActionHubService leaves UpdatedAt null),
        // so a manager who added an action yesterday would otherwise be told
        // they had been idle all week. Adding or editing an EVENT does stamp the
        // parent's UpdatedAt, so events are covered — they are included anyway
        // so the alert doesn't depend on that side effect staying in place.
        //
        // Licensed-period filtering matches PopulateLastActionHubUpdatedAtAsync,
        // which is what the Team Structure page shows as LastActionHubUpdatedAt.
        private async Task<int> SendActionHubIdleAlertsAsync(DateTime now, CancellationToken ct)
        {
            if (!_options.ActionHubIdleAlertEnabled)
                return 0;

            var idleDays = Math.Max(1, _options.ActionHubIdleDays);
            var cutoff = now.AddDays(-idleDays);
            var sent = 0;

            var managedUnitIds = await _context.UserTeams
                .AsNoTracking()
                .Where(ut => !ut.IsUnAssigned)
                .Select(ut => (long)ut.UnitId)
                .Distinct()
                .ToListAsync(ct);

            if (managedUnitIds.Count == 0)
                return 0;

            var licensed = await _licensedPeriod.GetActivelyLicensedUnitIdsAsync(managedUnitIds);
            var complimentary = await _licensedPeriod.GetActivelyComplimentaryUnitIdsAsync(managedUnitIds);
            var paidUnitIds = licensed.Except(complimentary).ToList();

            if (paidUnitIds.Count == 0)
                return 0;

            var teams = await _context.BusinessUnits
                .AsNoTracking()
                .Where(bu => bu.IsDel != true && paidUnitIds.Contains((long)bu.Id))
                .Select(bu => new { UnitId = bu.Id, bu.Name })
                .ToListAsync(ct);

            if (teams.Count == 0)
                return 0;

            var unitIds = teams.Select(t => t.UnitId).ToList();
            var intervals = await _licensedPeriod.GetLicensedIntervalsAsync(unitIds.Select(id => (long)id));

            // Every activity timestamp per unit, in two reads rather than one
            // per team.
            var hubActivity = await _context.ActionHubs
                .AsNoTracking()
                .Where(ah => !ah.IsDel && unitIds.Contains(ah.UnitId))
                .Select(ah => new { ah.UnitId, ah.CreatedAt, ah.UpdatedAt })
                .ToListAsync(ct);

            var eventActivity = await _context.ActionEvents
                .AsNoTracking()
                .Where(ev => !ev.IsDel && !ev.ActionHub.IsDel && unitIds.Contains(ev.ActionHub.UnitId))
                .Select(ev => new { ev.ActionHub.UnitId, ev.CreatedAt })
                .ToListAsync(ct);

            var stamps = new Dictionary<uint, List<DateTime>>();
            void Record(uint unitId, DateTime? at)
            {
                if (!at.HasValue) return;
                if (!_licensedPeriod.IsLicensed(intervals, unitId, at.Value)) return;
                if (!stamps.TryGetValue(unitId, out var list))
                    stamps[unitId] = list = new List<DateTime>();
                list.Add(at.Value);
            }

            foreach (var h in hubActivity) { Record(h.UnitId, h.CreatedAt); Record(h.UnitId, h.UpdatedAt); }
            foreach (var e in eventActivity) Record(e.UnitId, e.CreatedAt);

            var unitsWithActions = hubActivity.Select(h => h.UnitId).ToHashSet();

            foreach (var team in teams)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    if (_options.ActionHubAlertOnlyIfActionsExist && !unitsWithActions.Contains(team.UnitId))
                        continue;

                    var lastActivity = stamps.TryGetValue(team.UnitId, out var list) && list.Count > 0
                        ? list.Max()
                        : (DateTime?)null;

                    if (lastActivity.HasValue && lastActivity.Value > cutoff)
                        continue; // active within the window

                    if (await AlertedRecentlyAsync(team.UnitId,
                            TeamOnboardingNotificationOptions.ActionHubIdleAlertStatus,
                            _options.ActionHubIdleCooldownDays, now, ct))
                        continue;

                    var dayWord = idleDays == 1 ? "day" : "days";
                    var sentence = lastActivity.HasValue
                        ? $"There has been no activity on your Action Hub for {team.Name} in the last {idleDays} {dayWord} " +
                          $"(last update {lastActivity.Value:MMMM d, yyyy})."
                        : $"There has been no activity on your Action Hub for {team.Name} in the last {idleDays} {dayWord}.";

                    var encouragement =
                        "Recording what you've tried, and how it went, is what turns your team's feedback into " +
                        "progress. A few minutes in the Action Hub keeps it moving.";

                    if (await DeliverAlertAsync(team.UnitId, team.Name, sentence, encouragement,
                            subject: "No activity on your Action Hub – RRIDA",
                            title: "No Action Hub activity",
                            notificationType: "ActionHubIdle",
                            sentStatus: TeamOnboardingNotificationOptions.ActionHubIdleAlertStatus,
                            auditDetail: $"idleDays={idleDays}, lastActivity={(lastActivity.HasValue ? lastActivity.Value.ToString("O") : "never")}",
                            // This alert is about actions, so it points at the
                            // Action Hub rather than the scores page.
                            link: BuildLink(_options.ActionHubUrlPath),
                            linkIntro: "Click on the link to login and update your Action Hub:",
                            linkLabel: "Click to View Your Action Hub",
                            ct: ct))
                        sent++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[TeamOnboarding] Action Hub idle alert failed for UnitId={UnitId}", team.UnitId);
                    await AuditAsync(team.UnitId, "ActionHubIdleAlertError", $"{ex.GetType().Name}: {ex.Message}");
                }
            }

            return sent;
        }

        // ── Daily response monitoring ────────────────────────────────────────
        //
        // "Your team had no responses yesterday." Runs every day, for every paid
        // team, independent of the onboarding sequence above.
        //
        // Same paid-only gate as the day 2 / day 5 nudges: the email points at
        // scores, and an unlicensed team has none to look at. Same
        // licensed-period response counting, so "no responses" here means
        // exactly what a zero would mean on the Team Structure page.
        private async Task<int> SendNoResponseAlertsAsync(DateTime now, CancellationToken ct)
        {
            if (!_options.NoResponseAlertEnabled)
                return 0;

            var yesterday = now.Date.AddDays(-1);
            var today = now.Date;
            var sent = 0;

            // Teams with a live manager. A team nobody manages has nobody to
            // alert, and would otherwise be re-evaluated every single day.
            var managedUnitIds = await _context.UserTeams
                .AsNoTracking()
                .Where(ut => !ut.IsUnAssigned)
                .Select(ut => (long)ut.UnitId)
                .Distinct()
                .ToListAsync(ct);

            if (managedUnitIds.Count == 0)
                return 0;

            var licensed = await _licensedPeriod.GetActivelyLicensedUnitIdsAsync(managedUnitIds);
            var complimentary = await _licensedPeriod.GetActivelyComplimentaryUnitIdsAsync(managedUnitIds);
            var paidUnitIds = licensed.Except(complimentary).ToList();

            if (paidUnitIds.Count == 0)
                return 0;

            var teams = await _context.BusinessUnits
                .AsNoTracking()
                .Where(bu => bu.IsDel != true && paidUnitIds.Contains((long)bu.Id))
                .Select(bu => new { UnitId = bu.Id, bu.Name })
                .ToListAsync(ct);

            var intervals = await _licensedPeriod.GetLicensedIntervalsAsync(teams.Select(t => (long)t.UnitId));

            // First-assignment dates, to skip teams still inside onboarding.
            var firstAssigned = await _context.UserTeams
                .AsNoTracking()
                .GroupBy(ut => ut.UnitId)
                .Select(g => new { UnitId = g.Key, FirstAssignedAt = g.Min(x => x.CreatedDate) })
                .ToDictionaryAsync(x => x.UnitId, x => x.FirstAssignedAt, ct);

            foreach (var team in teams)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    if (_options.SkipNoResponseAlertDuringOnboarding &&
                        firstAssigned.TryGetValue(team.UnitId, out var assignedAt) &&
                        (now.Date - assignedAt.Date).TotalDays <= _options.Day5AfterDays)
                    {
                        // Day 2 already covers "no responses yet" for these.
                        continue;
                    }

                    if (await AlertedRecentlyAsync(team.UnitId,
                            TeamOnboardingNotificationOptions.NoResponseAlertStatus,
                            _options.NoResponseAlertCooldownDays, now, ct))
                        continue;

                    var count = await CountResponsesAsync(team.UnitId, yesterday, today, intervals, ct);
                    if (count > 0)
                        continue;

                    var sentence =
                        $"Your team {team.Name} had no responses yesterday ({yesterday:MMMM d, yyyy}).";
                    var encouragement =
                        "Without daily responses your scores can't move. A quick reminder to the team " +
                        "is usually all it takes to get them going again.";

                    if (await DeliverAlertAsync(team.UnitId, team.Name, sentence, encouragement,
                            subject: "Your team had no responses yesterday – RRIDA",
                            title: "No responses yesterday",
                            notificationType: "TeamNoResponse",
                            sentStatus: TeamOnboardingNotificationOptions.NoResponseAlertStatus,
                            auditDetail: $"date={yesterday:yyyy-MM-dd}, responses=0",
                            link: BuildLink(_options.ScoresUrlPath),
                            linkIntro: "Click on the link to login and see your scores:",
                            linkLabel: "Click to View Your Scores",
                            ct: ct))
                        sent++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[TeamOnboarding] No-response alert failed for UnitId={UnitId}", team.UnitId);
                    await AuditAsync(team.UnitId, "NoResponseAlertError",
                        $"{ex.GetType().Name}: {ex.Message}");
                }
            }

            return sent;
        }

        // Cooldown check, read from the audit rows this service writes. Shared
        // by every recurring alert — each passes its own status and window.
        private async Task<bool> AlertedRecentlyAsync(
            uint unitId,
            string sentStatus,
            int cooldownDays,
            DateTime now,
            CancellationToken ct)
        {
            var endpoint = $"Hangfire/{TeamOnboardingNotificationOptions.JobName}";
            var cooldownFrom = now.Date.AddDays(-Math.Max(1, cooldownDays));

            return await _context.LLMLogs
                .AsNoTracking()
                .AnyAsync(l => l.UnitId == (int)unitId
                            && l.Endpoint == endpoint
                            && l.Status == sentStatus
                            && l.Timestamp >= cooldownFrom, ct);
        }

        // Email + in-app to the team's manager, then the cooldown marker.
        // Shared by the no-response and Action Hub alerts.
        private async Task<bool> DeliverAlertAsync(
            uint unitId,
            string teamName,
            string sentence,
            string encouragement,
            string subject,
            string title,
            string notificationType,
            string sentStatus,
            string auditDetail,
            string link,
            string linkIntro,
            string linkLabel,
            CancellationToken ct)
        {
            var manager = await GetManagerAsync(unitId, ct);
            if (manager == null)
                return false;

            var scoresLink = link;

            bool emailSent = false;
            string? emailError = null;
            try
            {
                if (string.IsNullOrWhiteSpace(manager.Email))
                    emailError = "manager has no email address";
                else
                {
                    emailSent = await BussinessManager.SendTeamAlertEmailAsync(
                        _sendGridSettings, manager.Email, subject, sentence, encouragement,
                        scoresLink, linkIntro, linkLabel);

                    if (!emailSent)
                        emailError = _sendGridSettings.UseSendGrid
                            ? "SendGrid rejected the send"
                            : "Resend rejected the send";
                }
            }
            catch (Exception ex)
            {
                emailError = $"{ex.GetType().Name}: {ex.Message}";
            }

            bool inAppCreated = false;
            try
            {
                var created = await _systemNotification.CreateNotificationAsync(new CreateSystemNotificationDto
                {
                    UserId = manager.UserId,
                    Title = title,
                    Message = $"{sentence} {encouragement}",
                    NotificationType = notificationType,
                    NotificationUrl = scoresLink
                });

                inAppCreated = created != null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[TeamOnboarding] {Type} in-app failed for UnitId={UnitId}", notificationType, unitId);
            }

            // Only stamp the cooldown marker when something was delivered, so a
            // provider outage doesn't silently consume the alert.
            if (!emailSent && !inAppCreated)
            {
                await AuditAsync(unitId, $"{sentStatus}Failed",
                    $"team='{teamName}', to={manager.Email}, {auditDetail}" +
                    (emailError == null ? "" : $", emailError={emailError}"));
                return false;
            }

            await AuditAsync(unitId, sentStatus,
                $"team='{teamName}', to={manager.Email}, {auditDetail}, " +
                $"email={(emailSent ? "sent" : "no")}, inApp={(inAppCreated ? "created" : "no")}" +
                (emailError == null ? "" : $", emailError={emailError}"));

            return true;
        }

        // "This team is now yours", sent the moment a manager is assigned.
        //
        // Deliberately NOT gated on licensing, unlike the day 2 / day 5 nudges:
        // this is an operational notice about a responsibility the manager has
        // just been given, not an invitation to go and look at scores. A manager
        // must know they own a team whether or not it is paid for.
        // Every exit path below writes an LLMLogs row. An earlier version
        // returned silently on three of them, which made "no email arrived"
        // indistinguishable from "the code never ran" — the single most
        // expensive kind of bug to chase. Silence is now impossible: if this
        // method is entered, it leaves a trace.
        public async Task<bool> NotifyManagerAssignedAsync(int managerUserId, uint unitId, CancellationToken ct = default)
        {
            try
            {
                // Gated only by its own switch. "Enabled" is the master switch of
                // the day 2 / day 5 nudge job; an assignment notice is not a nudge,
                // and switching the job off must not silence it.
                if (!_options.NotifyOnManagerAssignment)
                {
                    await AuditAsync(unitId, "ManagerAssignedDisabled",
                        $"managerUserId={managerUserId}: " +
                        $"NotifyOnManagerAssignment={_options.NotifyOnManagerAssignment}.");
                    return false;
                }

                // Idempotency, per (team, manager): the in-request hook and the
                // background catch-up can both reach this for the same
                // assignment, and a manager must not be mailed twice. A NEW
                // manager on the same team is a different pair, so they still
                // get their own message.
                //
                // The skip is audited rather than silent — "we already told this
                // manager" is the single most confusing reason for an email not
                // to arrive during testing, and it must be visible.
                if (!_options.ResendAssignmentNotifications &&
                    await AlreadyNotifiedOfAssignmentAsync(unitId, managerUserId, ct))
                {
                    await AuditAsync(unitId, "ManagerAssignedAlreadyNotified",
                        $"managerUserId={managerUserId}: this manager was already notified about this team; " +
                        $"not sending again. Set TeamOnboardingNotifications:ResendAssignmentNotifications=true " +
                        $"to force a resend, or delete that LLMLogs row.");
                    return false;
                }

                // The unit is read WITHOUT touching bu.Company.
                //
                // Company is a required navigation with a shadow foreign key, so
                // projecting bu.Company.Id makes EF emit an INNER JOIN to
                // Companies — and a unit whose company row can't be joined then
                // disappears entirely, reporting "team not found". That blocked
                // the manager's email on data the email does not use: the
                // company id appears only in the audit line below.
                var team = await _context.BusinessUnits
                    .AsNoTracking()
                    .Where(bu => bu.Id == unitId)
                    .Select(bu => new { bu.Name, bu.RegistrationDate })
                    .FirstOrDefaultAsync(ct);

                if (team == null)
                {
                    // Genuinely no BusinessUnit row — usually a UserTeams row
                    // pointing at a unit that no longer exists.
                    await AuditAsync(unitId, "ManagerAssignedNoTeam",
                        $"managerUserId={managerUserId}: no BusinessUnits row exists for UnitId={unitId} " +
                        $"(stale UserTeams assignment pointing at a deleted or never-created unit).");
                    return false;
                }

                // Best-effort, and never a blocker.
                long profileId = await _context.BusinessUnits
                    .AsNoTracking()
                    .Where(bu => bu.Id == unitId)
                    .Select(bu => (long?)bu.Profile.Id)
                    .FirstOrDefaultAsync(ct) ?? 0;

                var manager = await _context.Users
                    .AsNoTracking()
                    .Where(u => u.UserId == managerUserId && !u.IsDel)
                    .Select(u => new { u.UserId, u.Name, u.Username })
                    .FirstOrDefaultAsync(ct);

                if (manager == null)
                {
                    await AuditAsync(unitId, "ManagerAssignedNoUser",
                        $"managerUserId={managerUserId}: no non-deleted Users row.");
                    return false;
                }

                // Whether this is a brand-new team or an existing one changing
                // hands changes what the manager needs to be told.
                var isNewTeam = (DateTime.UtcNow - team.RegistrationDate).TotalDays <= _options.NewTeamGraceDays;

                var sentence = isNewTeam
                    ? $"A new team, {team.Name}, has been created and assigned to you."
                    : $"The team {team.Name} has been assigned to you.";

                var scoresLink = BuildScoresLink();

                bool emailSent = false;
                string? emailError = null;
                try
                {
                    if (string.IsNullOrWhiteSpace(manager.Username))
                        emailError = "manager has no email address on Users.Username";
                    else
                    {
                        emailSent = await BussinessManager.SendTeamAssignedEmailAsync(
                            _sendGridSettings, manager.Username, sentence, scoresLink);

                        // The provider returning false is the common real-world
                        // case (bad/missing API key) and it used to vanish. Name
                        // the provider so the next person knows where to look.
                        if (!emailSent)
                            emailError = _sendGridSettings.UseSendGrid
                                ? "SendGrid rejected the send (check SendGrid:ApiKey)"
                                : "Resend rejected the send (check SendGrid:ResendApiKey — it is loaded from Key Vault at startup and overwrites appsettings)";
                    }
                }
                catch (Exception ex)
                {
                    emailError = $"{ex.GetType().Name}: {ex.Message}";
                    _logger.LogWarning(ex, "[TeamOnboarding] Assignment email failed for UnitId={UnitId}", unitId);
                }

                bool inAppCreated = false;
                try
                {
                    var created = await _systemNotification.CreateNotificationAsync(new CreateSystemNotificationDto
                    {
                        UserId = manager.UserId,
                        Title = isNewTeam ? "New Team Assigned" : "Team Assigned",
                        Message = sentence,
                        NotificationType = "TeamAssigned",
                        NotificationUrl = scoresLink
                    });

                    inAppCreated = created != null;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[TeamOnboarding] Assignment in-app notification failed for UnitId={UnitId}", unitId);
                }

                // managerUserId is written into the message on purpose: it is the
                // key AlreadyNotifiedOfAssignmentAsync matches on.
                await AuditAsync(unitId,
                    emailSent || inAppCreated ? "ManagerAssignedNotified" : "ManagerAssignedNotifyFailed",
                    $"managerUserId={manager.UserId}, team='{team.Name}', companyId={profileId}, " +
                    $"to={manager.Username}, newTeam={isNewTeam}, " +
                    $"email={(emailSent ? "sent" : "no")}, inApp={(inAppCreated ? "created" : "no")}" +
                    (emailError == null ? "" : $", emailError={emailError}"));

                return emailSent || inAppCreated;
            }
            catch (Exception ex)
            {
                // Contract: the assignment is already committed. Nothing here may
                // surface as a failure of that operation — but it must not vanish
                // either, so the reason goes to the audit trail as well as the log.
                _logger.LogError(ex, "[TeamOnboarding] NotifyManagerAssignedAsync failed for UnitId={UnitId}", unitId);

                try
                {
                    await AuditAsync(unitId, "ManagerAssignedError",
                        $"managerUserId={managerUserId}: {ex.GetType().Name}: {ex.Message}");
                }
                catch { /* audit itself failed; the ILogger entry above stands */ }

                return false;
            }
        }

        // "You have been unassigned from this team."
        //
        // Not licence-gated, for the same reason as the assignment notice: it
        // reports a change of responsibility, not a scores invitation. Every
        // exit path audits, so a missing email is always explainable.
        public async Task<bool> NotifyManagerUnassignedAsync(int managerUserId, uint unitId, CancellationToken ct = default)
        {
            try
            {
                if (!_options.Enabled || !_options.NotifyOnManagerUnassignment)
                {
                    await AuditAsync(unitId, "ManagerUnassignedDisabled",
                        $"managerUserId={managerUserId}: Enabled={_options.Enabled}, " +
                        $"NotifyOnManagerUnassignment={_options.NotifyOnManagerUnassignment}.");
                    return false;
                }

                var teamName = await _context.BusinessUnits
                    .AsNoTracking()
                    .Where(bu => bu.Id == unitId)
                    .Select(bu => bu.Name)
                    .FirstOrDefaultAsync(ct);

                if (teamName == null)
                {
                    await AuditAsync(unitId, "ManagerUnassignedNoTeam",
                        $"managerUserId={managerUserId}: no BusinessUnits row for UnitId={unitId}.");
                    return false;
                }

                var manager = await _context.Users
                    .AsNoTracking()
                    .Where(u => u.UserId == managerUserId && !u.IsDel)
                    .Select(u => new { u.UserId, u.Username })
                    .FirstOrDefaultAsync(ct);

                if (manager == null)
                {
                    // Expected when a team is unassigned BECAUSE its manager was
                    // deleted — there is nobody left to email, which is correct.
                    await AuditAsync(unitId, "ManagerUnassignedNoUser",
                        $"managerUserId={managerUserId}: no non-deleted Users row (deleted manager?).");
                    return false;
                }

                var sentence = $"You have been unassigned from the team {teamName}.";
                var encouragement =
                    "You will no longer receive reports or updates for this team. " +
                    "If this wasn't expected, please contact your administrator.";

                var link = BuildLink(_options.ScoresUrlPath);

                bool emailSent = false;
                string? emailError = null;
                try
                {
                    if (string.IsNullOrWhiteSpace(manager.Username))
                        emailError = "manager has no email address";
                    else
                    {
                        emailSent = await BussinessManager.SendTeamAlertEmailAsync(
                            _sendGridSettings, manager.Username,
                            "You have been unassigned from a team – RRIDA",
                            sentence, encouragement, link,
                            "Click on the link to login and see your teams:",
                            "Click to View Your Teams");

                        if (!emailSent)
                            emailError = _sendGridSettings.UseSendGrid
                                ? "SendGrid rejected the send"
                                : "Resend rejected the send";
                    }
                }
                catch (Exception ex)
                {
                    emailError = $"{ex.GetType().Name}: {ex.Message}";
                    _logger.LogWarning(ex, "[TeamOnboarding] Unassignment email failed for UnitId={UnitId}", unitId);
                }

                bool inAppCreated = false;
                try
                {
                    var created = await _systemNotification.CreateNotificationAsync(new CreateSystemNotificationDto
                    {
                        UserId = manager.UserId,
                        Title = "Team Unassigned",
                        Message = sentence,
                        NotificationType = "TeamUnassigned",
                        NotificationUrl = link
                    });

                    inAppCreated = created != null;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[TeamOnboarding] Unassignment in-app failed for UnitId={UnitId}", unitId);
                }

                await AuditAsync(unitId,
                    emailSent || inAppCreated ? "ManagerUnassignedNotified" : "ManagerUnassignedNotifyFailed",
                    $"managerUserId={manager.UserId}, team='{teamName}', to={manager.Username}, " +
                    $"email={(emailSent ? "sent" : "no")}, inApp={(inAppCreated ? "created" : "no")}" +
                    (emailError == null ? "" : $", emailError={emailError}"));

                return emailSent || inAppCreated;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[TeamOnboarding] NotifyManagerUnassignedAsync failed for UnitId={UnitId}", unitId);

                try
                {
                    await AuditAsync(unitId, "ManagerUnassignedError",
                        $"managerUserId={managerUserId}: {ex.GetType().Name}: {ex.Message}");
                }
                catch { /* audit itself failed; the ILogger entry above stands */ }

                return false;
            }
        }

        /// <summary>
        /// Records that this manager has already been told about this team,
        /// WITHOUT sending anything.
        ///
        /// Used when a manager is created together with their team: the invitation
        /// email already names the team, and the manager cannot sign in yet, so
        /// "a team has been assigned to you" would be a second email about an
        /// account they have not set up.
        ///
        /// Written as the same LLMLogs marker row a real send writes, rather than
        /// as a new rule inside NotifyManagerAssignedAsync. That keeps every
        /// existing path behaving exactly as it does today — this suppresses one
        /// specific assignment, not a category of them — and it is the row the
        /// catch-up pass already checks before it sends.
        ///
        /// Scoped to the current assignment like every other marker here: if this
        /// manager is later re-assigned to this team, that new UserTeams row moves
        /// the anchor past this marker and notification works normally again.
        ///
        /// Best-effort by contract. The assignment is already committed, and a
        /// failure here costs at most one extra email, never a failed operation.
        /// </summary>
        public async Task<bool> SuppressAssignmentNotificationAsync(
            int managerUserId, uint unitId, string reason, CancellationToken ct = default)
        {
            try
            {
                // AlreadyNotifiedOfAssignmentAsync matches on "managerUserId=<id>,"
                // and "email=sent", so both appear here. An email WAS sent to this
                // manager about this team — the invitation — which is what makes
                // the assignment notice redundant rather than missing.
                await AuditAsync(unitId, "ManagerAssignedNotified",
                    $"managerUserId={managerUserId}, email=sent (invitation at manager creation), " +
                    $"inApp=no, suppressed=assignment notice deliberately not sent: {reason}");

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[TeamOnboarding] SuppressAssignmentNotificationAsync failed for UnitId={UnitId}, managerUserId={ManagerUserId}",
                    unitId, managerUserId);
                return false;
            }
        }

        private Task AuditAsync(uint unitId, string status, string message)
            => _auditLogs.LogHangfireJobAsync(unitId, TeamOnboardingNotificationOptions.JobName, status, message);

        // Marker is per (team, manager): "already told THIS manager about THIS
        // team". Stored in the audit message rather than a column, keeping the
        // feature schema-free.
        // "Already notified" means the EMAIL actually went out — not merely that
        // something was delivered.
        //
        // The audit row is written when email OR in-app succeeds, and the in-app
        // row is a database insert that practically always succeeds. So an
        // attempt where the mail provider rejected the send still produced a
        // "Notified" row, and that row then suppressed every future attempt —
        // permanently blocking an email that had never actually been sent.
        // Matching on "email=sent" makes a failed email retryable, which is the
        // whole point of the marker being about delivery.
        private async Task<bool> AlreadyNotifiedOfAssignmentAsync(uint unitId, int managerUserId, CancellationToken ct)
        {
            var endpoint = $"Hangfire/{TeamOnboardingNotificationOptions.JobName}";
            var key = $"managerUserId={managerUserId},";

            // Scoped to the CURRENT assignment, not to the pair for all time.
            //
            // This exists only to stop the in-request hook and the background
            // catch-up both mailing about the SAME assignment. Keyed on
            // (team, manager) with no time bound it did far more than that: once
            // a manager had ever been told about a team, assigning them to it
            // again — after an unassignment, a handover, or a re-organisation —
            // silently sent nothing, because the marker from months earlier
            // still matched. Re-assignment is a new event and deserves its own
            // email.
            //
            // Anchoring on the latest UserTeams.CreatedDate for the pair means
            // each fresh assignment row starts a new window that old markers
            // cannot fall into.
            var latestAssignedAt = await _context.UserTeams
                .AsNoTracking()
                .Where(ut => ut.UnitId == unitId && ut.UserId == managerUserId)
                .OrderByDescending(ut => ut.CreatedDate)
                .Select(ut => (DateTime?)ut.CreatedDate)
                .FirstOrDefaultAsync(ct);

            var query = _context.LLMLogs
                .AsNoTracking()
                .Where(l => l.UnitId == (int)unitId
                         && l.Endpoint == endpoint
                         && l.Status == "ManagerAssignedNotified"
                         && l.ResponseData.Contains(key)
                         // Only a row that recorded an actual send counts; an
                         // attempt where the provider refused must stay retryable.
                         && l.ResponseData.Contains("email=sent"));

            if (latestAssignedAt.HasValue)
                query = query.Where(l => l.Timestamp >= latestAssignedAt.Value);

            return await query.AnyAsync(ct);
        }

        // Unassignment marker is scoped to the CURRENT assignment cycle: it only
        // counts rows written after this manager's latest assignment to the team.
        // Without that, a manager assigned → unassigned → assigned → unassigned
        // would be silenced on the second removal by the first one's marker.
        private async Task<bool> AlreadyNotifiedOfUnassignmentAsync(uint unitId, int managerUserId, CancellationToken ct)
        {
            var endpoint = $"Hangfire/{TeamOnboardingNotificationOptions.JobName}";
            var key = $"managerUserId={managerUserId},";

            var latestAssignedAt = await _context.UserTeams
                .AsNoTracking()
                .Where(ut => ut.UnitId == unitId && ut.UserId == managerUserId)
                .OrderByDescending(ut => ut.CreatedDate)
                .Select(ut => (DateTime?)ut.CreatedDate)
                .FirstOrDefaultAsync(ct);

            // Same "the email actually went out" rule as the assignment marker,
            // for the same reason.
            var query = _context.LLMLogs
                .AsNoTracking()
                .Where(l => l.UnitId == (int)unitId
                         && l.Endpoint == endpoint
                         && l.Status == "ManagerUnassignedNotified"
                         && l.ResponseData.Contains(key)
                         && l.ResponseData.Contains("email=sent"));

            if (latestAssignedAt.HasValue)
                query = query.Where(l => l.Timestamp >= latestAssignedAt.Value);

            return await query.AnyAsync(ct);
        }

        private Task<bool> AlreadyNotifiedAsync(uint unitId, int managerUserId, string status, CancellationToken ct)
        {
            var endpoint = $"Hangfire/{TeamOnboardingNotificationOptions.JobName}";
            var key = $"managerUserId={managerUserId},";

            return _context.LLMLogs
                .AsNoTracking()
                .AnyAsync(l => l.UnitId == (int)unitId
                            && l.Endpoint == endpoint
                            && l.Status == status
                            && l.ResponseData.Contains(key), ct);
        }

        // Due from its day number until the catch-up window closes. The window
        // covers a manager assigned after the team was created and a job run
        // missed to downtime; past it the nudge is dropped rather than arriving
        // stale.
        private bool IsDue(double ageInDays, int dueDay) =>
            ageInDays >= dueDay && ageInDays <= dueDay + _options.CatchUpDays;

        private async Task<bool> SendDay2Async(
            uint unitId,
            string teamName,
            long profileId,
            Dictionary<uint, List<LicensedInterval>> intervals,
            DateTime now,
            CancellationToken ct)
        {
            // "Yesterday" = the whole previous UTC day.
            var from = now.Date.AddDays(-1);
            var to = now.Date;

            var count = await CountResponsesAsync(unitId, from, to, intervals, ct);

            var sentence = count == 0
                ? $"Your team {teamName} is set up, but there were no responses yesterday."
                : count == 1
                    ? $"You've had 1 response for {teamName} yesterday."
                    : $"You've had {count} responses for {teamName} yesterday.";

            var encouragement = count == 0
                ? "It only takes a moment for each person to answer. A quick nudge to the team today " +
                  "is usually all it takes to get the first scores flowing."
                : "Keep the momentum going — the more your team responds, the clearer your scores become.";

            return await DeliverAsync(unitId, profileId, teamName,
                title: "Your team's first responses",
                sentence: sentence,
                encouragement: encouragement,
                sendEmail: (email, link) => BussinessManager.SendTeamSetupDay2EmailAsync(
                    _sendGridSettings, email, sentence, encouragement, link),
                sentStatus: TeamOnboardingNotificationOptions.Day2SentStatus,
                auditDetail: $"responsesYesterday={count}, window={from:O}→{to:O}",
                ct: ct);
        }

        private async Task<bool> SendDay5Async(
            uint unitId,
            string teamName,
            long profileId,
            DateTime managerAssignedAt,
            Dictionary<uint, List<LicensedInterval>> intervals,
            DateTime now,
            CancellationToken ct)
        {
            // Whole days of data so far: from the day the team got its manager
            // up to the end of yesterday. Today is excluded because it is still
            // in progress and would drag the average down.
            var from = managerAssignedAt.Date;
            var to = now.Date;

            var days = Math.Max(1, (int)(to - from).TotalDays);
            var total = await CountResponsesAsync(unitId, from, to, intervals, ct);
            var average = Math.Round((double)total / days, 1);

            var sentence = total == 0
                ? $"Your team {teamName} hasn't recorded any responses in its first {days} days."
                : $"{teamName} is averaging {average.ToString("0.#", CultureInfo.InvariantCulture)} " +
                  $"responses per day ({total} in {days} days).";

            var encouragement = total == 0
                ? "Your scores build from daily responses, so a reminder to the team is the quickest way to start seeing them."
                : "Log in to see how those responses translate into your team's scores.";

            return await DeliverAsync(unitId, profileId, teamName,
                title: "Your team's first week",
                sentence: sentence,
                encouragement: encouragement,
                sendEmail: (email, link) => BussinessManager.SendTeamSetupDay5EmailAsync(
                    _sendGridSettings, email, sentence, encouragement, link),
                sentStatus: TeamOnboardingNotificationOptions.Day5SentStatus,
                auditDetail: $"totalResponses={total}, days={days}, avgPerDay={average}",
                ct: ct);
        }

        // Email + in-app notification to the team's current manager, then the
        // "sent" marker.
        //
        // The marker is written only when something actually reached the
        // manager. A team with no manager, or a mail provider outage, leaves it
        // unmarked so the next run retries inside the catch-up window.
        private async Task<bool> DeliverAsync(
            uint unitId,
            long profileId,
            string teamName,
            string title,
            string sentence,
            string encouragement,
            Func<string, string, Task<bool>> sendEmail,
            string sentStatus,
            string auditDetail,
            CancellationToken ct)
        {
            var manager = await GetManagerAsync(unitId, ct);

            if (manager == null)
            {
                await _auditLogs.LogHangfireJobAsync(unitId, TeamOnboardingNotificationOptions.JobName,
                    "Skipped", $"{sentStatus}: team '{teamName}' has no assigned manager; will retry within the catch-up window.");
                return false;
            }

            var scoresLink = BuildScoresLink();

            bool emailSent = false;
            try
            {
                if (!string.IsNullOrWhiteSpace(manager.Email))
                    emailSent = await sendEmail(manager.Email, scoresLink);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[TeamOnboarding] Email failed for UnitId={UnitId}", unitId);
            }

            bool inAppCreated = false;
            try
            {
                var created = await _systemNotification.CreateNotificationAsync(new CreateSystemNotificationDto
                {
                    UserId = manager.UserId,
                    Title = title,
                    Message = $"{sentence} {encouragement}",
                    NotificationType = "TeamOnboarding",
                    NotificationUrl = scoresLink
                });

                inAppCreated = created != null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[TeamOnboarding] In-app notification failed for UnitId={UnitId}", unitId);
            }

            if (!emailSent && !inAppCreated)
            {
                await _auditLogs.LogHangfireJobAsync(unitId, TeamOnboardingNotificationOptions.JobName,
                    "NotifyFailed", $"{sentStatus}: nothing delivered to {manager.Email}; not marked as sent, will retry.");
                return false;
            }

            // This row is the "already sent" marker AND the audit trail.
            await _auditLogs.LogHangfireJobAsync(unitId, TeamOnboardingNotificationOptions.JobName,
                sentStatus,
                $"team='{teamName}', companyId={profileId}, to={manager.Email}, " +
                $"email={(emailSent ? "sent" : "no")}, inApp={(inAppCreated ? "created" : "no")}, {auditDetail}");

            return true;
        }

        // Responses in [from, to), counted only while the team was licensed —
        // the same rule behind TotalResponses on the Team Structure page.
        private async Task<int> CountResponsesAsync(
            uint unitId,
            DateTime from,
            DateTime to,
            Dictionary<uint, List<LicensedInterval>> intervals,
            CancellationToken ct)
        {
            var rows = await _context.DailyFeedbacks
                .AsNoTracking()
                .Where(df => df.BusinessUnitId == unitId
                          && df.SubmissionDateTime >= from
                          && df.SubmissionDateTime < to)
                .Select(df => df.SubmissionDateTime)
                .ToListAsync(ct);

            return rows.Count(ts => _licensedPeriod.IsLicensed(intervals, unitId, ts));
        }

        // The team's current manager: newest active assignment whose user is not
        // soft-deleted. Same rule as the team hierarchy and the report
        // notification, so all three agree on who "the manager" is.
        private sealed record TeamManagerContact(int UserId, string? Name, string? Email);

        private async Task<TeamManagerContact?> GetManagerAsync(uint unitId, CancellationToken ct)
        {
            var manager = await _context.UserTeams
                .AsNoTracking()
                .Where(ut => ut.UnitId == unitId && !ut.IsUnAssigned)
                // Deliberately NOT filtered on Users.IsActive. Nowhere else in
                // the product does — the team hierarchy and the report
                // notification both key on !IsDel alone — and the extra
                // condition made teams with a perfectly good manager report
                // "no assigned manager" and skip every run.
                .Join(_context.Users.Where(u => !u.IsDel),
                      ut => ut.UserId,
                      u => u.UserId,
                      (ut, u) => new { ut.CreatedDate, ut.Id, u.UserId, u.Name, u.Username })
                .OrderByDescending(x => x.CreatedDate)
                .ThenByDescending(x => x.Id)
                .FirstOrDefaultAsync(ct);

            return manager == null
                ? null
                : new TeamManagerContact(manager.UserId, manager.Name, manager.Username);
        }

        // Has this nudge already gone out for this team? Read from the audit
        // rows this service writes, which is why no schema change is needed.
        private Task<bool> AlreadySentAsync(uint unitId, string sentStatus, CancellationToken ct)
        {
            var endpoint = $"Hangfire/{TeamOnboardingNotificationOptions.JobName}";

            return _context.LLMLogs
                .AsNoTracking()
                .AnyAsync(l => l.UnitId == (int)unitId
                            && l.Endpoint == endpoint
                            && l.Status == sentStatus, ct);
        }

        private string BuildScoresLink() => BuildLink(_options.ScoresUrlPath);

        // FrontendBaseUrl + a configured path, tolerating a trailing slash on
        // one and a leading slash on the other.
        private string BuildLink(string? configuredPath)
        {
            var baseUrl = (_sendGridSettings?.FrontendBaseUrl ?? string.Empty).TrimEnd('/');
            var path = (configuredPath ?? string.Empty).Trim();

            if (string.IsNullOrEmpty(path))
                return baseUrl;

            return $"{baseUrl}/{path.TrimStart('/')}";
        }
    }
}
