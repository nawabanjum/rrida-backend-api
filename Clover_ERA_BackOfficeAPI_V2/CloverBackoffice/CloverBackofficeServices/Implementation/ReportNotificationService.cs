using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CloverBackoffice.Components;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CloverBackoffice.Services.Implementation
{
    // Extracted from FeedbackAnalysisService.CreateReportAsync, which was the only
    // report-save path that notified anyone. The scheduler wrote its reports
    // straight to the DB and returned, so automatically generated reports reached
    // no manager at all. Every report path now shares this.
    //
    // This is the mechanism, not the policy: WHEN a manager may be told is
    // decided by the callers, and for scheduler-generated reports that is
    // ReportApprovalService.ApproveAndReleaseAsync alone — an admin approval.
    // Every caller claims the send through
    // IReportApprovalRepository.TryClaimManagerNotificationAsync first, so this
    // method runs at most once per report however many times its caller is
    // retried.
    //
    // Behaviour is deliberately identical to the original inline block: same
    // manager lookup, same email helper, same OneSignal call, and failures are
    // contained — the report is already committed by the time this runs and must
    // not be undone by a provider outage. The difference is that the reason is
    // returned instead of silently swallowed.
    public class ReportNotificationService : IReportNotificationService
    {
        private readonly ApplicationDbContext _context;
        private readonly SendGridSettings _sendGridSettings;
        private readonly IPushNotificationService _pushNotificationService;
        private readonly ISystemNotificationService _systemNotificationService;
        private readonly ILogger<ReportNotificationService> _logger;

        public ReportNotificationService(
            ApplicationDbContext context,
            SendGridSettings sendGridSettings,
            IPushNotificationService pushNotificationService,
            ISystemNotificationService systemNotificationService,
            ILogger<ReportNotificationService> logger)
        {
            _context = context;
            _sendGridSettings = sendGridSettings;
            _pushNotificationService = pushNotificationService;
            _systemNotificationService = systemNotificationService;
            _logger = logger;
        }

        public async Task<ReportNotificationResult> NotifyReportReadyAsync(
            long unitId,
            long reportId,
            DateTime fromDate,
            DateTime toDate,
            CancellationToken ct = default)
        {
            try
            {
                // The unit's current manager: newest still-assigned UserTeams row.
                var userTeam = await _context.UserTeams
                    .Where(ut => ut.UnitId == unitId && ut.IsUnAssigned == false)
                    .OrderByDescending(ut => ut.CreatedDate)
                    .ThenByDescending(ut => ut.Id)
                    .FirstOrDefaultAsync(ct);

                if (userTeam == null)
                    return new ReportNotificationResult { Reason = $"No assigned manager in UserTeams for UnitId={unitId}." };

                var assignedUser = await _context.Users
                    .AsNoTracking()
                    .FirstOrDefaultAsync(u => u.UserId == userTeam.UserId, ct);

                if (assignedUser == null)
                    return new ReportNotificationResult { Reason = $"UserTeams points at UserId={userTeam.UserId}, which has no Users row." };

                if (string.IsNullOrWhiteSpace(assignedUser.Username))
                    return new ReportNotificationResult { Reason = $"Manager UserId={assignedUser.UserId} has no email address." };

                var businessUnit = await _context.BusinessUnits
                    .AsNoTracking()
                    .Include(bu => bu.Profile)
                    .FirstOrDefaultAsync(bu => bu.Id == unitId, ct);

                if (businessUnit?.Profile == null)
                    return new ReportNotificationResult { Reason = $"UnitId={unitId} has no BusinessUnit/Company row." };

                var unitName = businessUnit.Name ?? "N/A";
                var profileId = (long)businessUnit.Profile.Id;

                var reportLink = $"{_sendGridSettings.FrontendBaseUrl}/reports/{profileId}/{unitId}/{reportId}";

                // Same wording as the email body, shortened for a notification
                // row: the manager should read the same sentence wherever the
                // report reaches them.
                var reportMessage =
                    $"You have a new team engagement report for {unitName} with insights for " +
                    $"{fromDate:MMMM d, yyyy} through {toDate:MMMM d, yyyy}.";

                bool emailSent = false;
                string? failure = null;

                try
                {
                    emailSent = await BussinessManager.SendReportNotificationEmailAsync(
                        _sendGridSettings,
                        assignedUser.Username,
                        profileId,
                        unitId,
                        reportId,
                        unitName,
                        fromDate,
                        toDate);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[ReportNotification] Email failed for UnitId={UnitId}, ReportId={ReportId}", unitId, reportId);
                    failure = $"email failed: {ex.GetType().Name}: {ex.Message}";
                }

                // In-app notification — the SystemNotifications bell list the
                // frontend polls via /api/SystemNotification. This is the piece no
                // report path had: manual generation sent email + OneSignal push
                // only, so a manager with no registered device saw nothing in the
                // product itself. Unlike push, this needs no device registration.
                // Targeted at the same manager the email went to, deliberately not
                // CreateNotificationForTeamManagersAsync — that one walks up the
                // parent-unit chain and would notify every ancestor manager for
                // each child unit's report, which the email does not do.
                bool inAppCreated = false;
                try
                {
                    var created = await _systemNotificationService.CreateNotificationAsync(
                        new CreateSystemNotificationDto
                        {
                            UserId = assignedUser.UserId,
                            Title = "New Report Available",
                            Message = reportMessage,
                            NotificationType = "Report",
                            NotificationUrl = reportLink
                        });

                    inAppCreated = created != null;
                    if (!inAppCreated) failure = Combine(failure, "in-app notification insert returned null");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[ReportNotification] In-app notification failed for UnitId={UnitId}, ReportId={ReportId}", unitId, reportId);
                    failure = Combine(failure, $"in-app failed: {ex.GetType().Name}: {ex.Message}");
                }

                // Push is best-effort on top of the email: a manager with no
                // registered device still gets the report by mail and in-app.
                bool pushSent = false;
                try
                {
                    var latestSystemDetails = await _context.UserSystemDetails
                        .Where(u => u.UserId == assignedUser.UserId)
                        .OrderByDescending(u => u.CreatedAt)
                        .FirstOrDefaultAsync(ct);

                    if (latestSystemDetails == null || string.IsNullOrWhiteSpace(latestSystemDetails.OneSignalPlayerId))
                    {
                        failure = Combine(failure, "no registered device for push");
                    }
                    else
                    {
                        var securityRequest = new SecurityNotificationRequestDto
                        {
                            UserId = assignedUser.UserId,
                            IPAddress = latestSystemDetails.IPAddress,
                            UserAgent = latestSystemDetails.UserAgent,
                            CustomTitle = "New Report Available",
                            CustomMessage = reportMessage
                        };

                        pushSent = await _pushNotificationService.SendSecurityNotificationIfMatchFoundAsync(
                            securityRequest,
                            latestSystemDetails.DeviceModel,
                            latestSystemDetails.MachineName,
                            latestSystemDetails.OneSignalPlayerId,
                            reportLink);

                        // Returns false both when no stored device matched and when
                        // OneSignal rejected the send; it doesn't distinguish.
                        if (!pushSent) failure = Combine(failure, "push not delivered (no matching device or OneSignal refused)");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[ReportNotification] Push failed for UnitId={UnitId}, ReportId={ReportId}", unitId, reportId);
                    failure = Combine(failure, $"push failed: {ex.GetType().Name}: {ex.Message}");
                }

                return new ReportNotificationResult
                {
                    EmailSent = emailSent,
                    InAppCreated = inAppCreated,
                    PushSent = pushSent,
                    Recipient = assignedUser.Username,
                    Reason = failure
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ReportNotification] Unhandled failure for UnitId={UnitId}, ReportId={ReportId}", unitId, reportId);
                return new ReportNotificationResult { Reason = $"{ex.GetType().Name}: {ex.Message}" };
            }
        }

        private static string Combine(string? existing, string addition)
            => string.IsNullOrEmpty(existing) ? addition : $"{existing}; {addition}";
    }
}
