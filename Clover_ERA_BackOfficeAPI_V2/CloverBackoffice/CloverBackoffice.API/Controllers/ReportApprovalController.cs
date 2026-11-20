using CloverBackoffice.Components.Enums;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CloverBackoffice.API.Controllers
{
    // Admin review queue: everything between "the scheduler generated a report"
    // and "the manager was told about it".
    //
    // A report's state is not stored — it is derived from its InsightPublishLog
    // rows, the same marker the workbench flow uses for its own
    // InsightsPublished flag. Approving writes that row; nothing else about the
    // schema changes.
    //
    // Access is gated twice, in EnsureReviewerAsync:
    //   1. the caller must be a RridaAdmin, and
    //   2. their email must appear in ReportApproval:ReviewerEmails.
    // Configuration alone can't grant the product's highest role, and holding
    // that role isn't enough to sign reports off.
    [Route("api/report-approvals")]
    [ApiController]
    [Authorize]
    public class ReportApprovalController : ControllerBase
    {
        private readonly IReportApprovalService _service;
        private readonly ITokenReaderService _tokenReader;

        public ReportApprovalController(
            IReportApprovalService service,
            ITokenReaderService tokenReader)
        {
            _service = service;
            _tokenReader = tokenReader;
        }

        // GET /api/report-approvals/config
        // Lets the UI decide whether to show the queue at all, without probing a
        // protected endpoint and reading a 403.
        [HttpGet("config")]
        public async Task<IActionResult> GetConfig()
        {
            try
            {
                var user = await _tokenReader.GetCurrentUserAsync();
                if (user == null)
                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.Invalidtoken));

                var config = _service.GetConfig(user.Username);

                // Only a reviewer gets to see who the other reviewers are.
                if (!config.CallerIsReviewer)
                    config.ReviewerEmails = new List<string>();

                return Ok(ApiResponseHandler.Success(config, ResponseMessages.Success.OperationSuccess));
            }
            catch (Exception ex)
            {
                return Ok(ApiResponseHandler.Fail<string>($"{ResponseMessages.Errors.ExceptionMsg} :: {ex.GetBaseException().Message}"));
            }
        }

        // GET /api/report-approvals?state=Pending&profileId=&unitId=&page=1&pageSize=25
        // state: Pending | Released | Rejected. Omitted → all, newest first.
        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> GetQueue(
            [FromQuery] string? state = null,
            [FromQuery] long? profileId = null,
            [FromQuery] long? unitId = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 25)
        {
            try
            {
                var gate = await EnsureReviewerAsync();
                if (gate != null) return gate;

                if (!IsKnownState(state))
                    return Ok(ApiResponseHandler.Fail<string>(
                        $"Unknown state '{state}'. Use Pending, Released or Rejected."));

                Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";

                var result = await _service.GetQueueAsync(Normalise(state), profileId, unitId, page, pageSize);
                return Ok(ApiResponseHandler.Success(result, ResponseMessages.Success.OperationSuccess));
            }
            catch (Exception ex)
            {
                return Ok(ApiResponseHandler.Fail<string>($"{ResponseMessages.Errors.ExceptionMsg} :: {ex.GetBaseException().Message}"));
            }
        }

        // GET /api/report-approvals/{reportId}
        // The report as the reviewer reads it: header, destination manager, and
        // the body in the same list shape the workbench editor already uses.
        [HttpGet("{reportId:long}")]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> GetDetail(long reportId)
        {
            try
            {
                var gate = await EnsureReviewerAsync();
                if (gate != null) return gate;

                var detail = await _service.GetDetailAsync(reportId);
                if (detail == null)
                    return Ok(ApiResponseHandler.Fail<string>($"Report {reportId} not found."));

                return Ok(ApiResponseHandler.Success(detail, ResponseMessages.Success.OperationSuccess));
            }
            catch (Exception ex)
            {
                return Ok(ApiResponseHandler.Fail<string>($"{ResponseMessages.Errors.ExceptionMsg} :: {ex.GetBaseException().Message}"));
            }
        }

        // PUT /api/report-approvals/{reportId}/insights
        // Corrections before release. Same payload shape as the workbench
        // save-run-insights endpoint; each supplied list fully replaces that
        // section, omit a list to leave it untouched.
        [HttpPut("{reportId:long}/insights")]
        public async Task<IActionResult> UpdateInsights(long reportId, [FromBody] UpdateReportInsightsDto dto)
        {
            try
            {
                var gate = await EnsureReviewerAsync();
                if (gate != null) return gate;

                if (dto == null)
                    return Ok(ApiResponseHandler.Fail<string>("Body is required."));

                var (detail, error) = await _service.UpdateInsightsAsync(reportId, dto);
                if (error != null)
                    return Ok(ApiResponseHandler.Fail<string>(error));

                return Ok(ApiResponseHandler.Success(detail, "Report updated."));
            }
            catch (Exception ex)
            {
                return Ok(ApiResponseHandler.Fail<string>($"{ResponseMessages.Errors.ExceptionMsg} :: {ex.GetBaseException().Message}"));
            }
        }

        // POST /api/report-approvals/{reportId}/approve
        // The release: publishes the report and notifies its manager. This is
        // the only endpoint that sends a scheduled report to a manager.
        //
        // Idempotent — a second call finds the publish-log row and sends nothing.
        [HttpPost("{reportId:long}/approve")]
        public async Task<IActionResult> Approve(long reportId)
        {
            try
            {
                var gate = await EnsureReviewerAsync();
                if (gate != null) return gate;

                var actorUserId = _tokenReader.GetUserIdFromToken();

                var result = await _service.ApproveAndReleaseAsync(reportId, actorUserId);

                return result.Success
                    ? Ok(ApiResponseHandler.Success(result, result.Message ?? "Report released."))
                    : Ok(ApiResponseHandler.Fail<ReportReleaseResultDto>(result.Message ?? "Report could not be released."));
            }
            catch (Exception ex)
            {
                return Ok(ApiResponseHandler.Fail<string>($"{ResponseMessages.Errors.ExceptionMsg} :: {ex.GetBaseException().Message}"));
            }
        }

        // POST /api/report-approvals/{reportId}/reject
        // Terminal for this report: the manager never sees it, and the unit's
        // next cycle generates a fresh one.
        //
        // COMMENTED OUT — not exposed for now. The service and repository
        // implementations behind it (ReportApprovalService.RejectAsync →
        // IReportApprovalRepository.TryRejectAsync) are left intact and still
        // compile, so uncommenting this block is the only step needed to bring
        // the endpoint back. Reviewers currently approve or leave a report
        // pending; nothing else in the flow depends on rejection existing.
        /*
        [HttpPost("{reportId:long}/reject")]
        public async Task<IActionResult> Reject(long reportId, [FromBody] RejectReportDto dto)
        {
            try
            {
                var gate = await EnsureReviewerAsync();
                if (gate != null) return gate;

                if (dto == null || string.IsNullOrWhiteSpace(dto.Reason))
                    return Ok(ApiResponseHandler.Fail<string>("A rejection reason is required."));

                var actorUserId = _tokenReader.GetUserIdFromToken();

                var result = await _service.RejectAsync(reportId, actorUserId, dto.Reason);

                return result.Success
                    ? Ok(ApiResponseHandler.Success(result, result.Message ?? "Report rejected."))
                    : Ok(ApiResponseHandler.Fail<ReportReleaseResultDto>(result.Message ?? "Report could not be rejected."));
            }
            catch (Exception ex)
            {
                return Ok(ApiResponseHandler.Fail<string>($"{ResponseMessages.Errors.ExceptionMsg} :: {ex.GetBaseException().Message}"));
            }
        }
        */

        // Both gates, in one place. Returns null when the caller may proceed.
        private async Task<IActionResult?> EnsureReviewerAsync()
        {
            var user = await _tokenReader.GetCurrentUserAsync();
            if (user == null)
                return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.Invalidtoken));

            if (user.UserGroupId != (int)UserGroups.RridaAdmin)
                return StatusCode(403, ApiResponseHandler.Fail<string>(
                    "Only a RRIDA Admin can review generated reports."));

            if (!_service.IsReviewer(user.Username))
                return StatusCode(403, ApiResponseHandler.Fail<string>(
                    "Your account is not on the configured report-reviewer list."));

            return null;
        }

        private static bool IsKnownState(string? state) =>
            string.IsNullOrWhiteSpace(state)
            || state.Equals("All", StringComparison.OrdinalIgnoreCase)
            || state.Equals(ReportReviewStates.Pending, StringComparison.OrdinalIgnoreCase)
            || state.Equals(ReportReviewStates.Released, StringComparison.OrdinalIgnoreCase)
            || state.Equals(ReportReviewStates.Rejected, StringComparison.OrdinalIgnoreCase);

        private static string? Normalise(string? state) =>
            string.IsNullOrWhiteSpace(state) || state.Equals("All", StringComparison.OrdinalIgnoreCase)
                ? null
                : state.Trim();
    }
}
