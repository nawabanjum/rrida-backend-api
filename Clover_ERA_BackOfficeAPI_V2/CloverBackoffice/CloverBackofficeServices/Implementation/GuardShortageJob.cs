using CloverBackoffice.BusinessModel;
using CloverBackoffice.Components;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Implementation
{
    public class GuardShortageJob
    {
        // Configured schedules drive entirely off the API-validated per-unit
        // value. Units with no configured schedule fall back to
        // ReportGenerationFallback.IntervalDays (appsettings, default 14 days).
        private readonly IRagService _ragService;
        private readonly IFeedbackAnalysisService _feedbackservice;
        private readonly IAuditLogsService _auditLogsService;
        private readonly ILLMReportScheduleRepository _scheduleRepo;
        private readonly IWorkbenchRunExecutor _runExecutor;
        private readonly IDocumentBatchesRepository _documentBatchesRepo;
        private readonly ILicensedPeriodService _licensedPeriod;
        private readonly IReportNotificationService _reportNotification;
        private readonly IReportApprovalService _reportApproval;
        private readonly ApplicationDbContext _db;
        private readonly ReportGenerationFallbackOptions _fallback;

        // ModelConfiguration.ModelConfigTypeId for chat / "Search-model" rows —
        // the same set EranCoachChatService and CloveraPersonalSnapshotService
        // choose from. Types 2 (Batch) and 3 (Embedding) are not report models.
        private const int SearchModelTypeId = 1;

        // Models tried per unit per cycle before giving up. The first is the
        // database default; the rest exist only so one retired deployment can't
        // cost a whole reporting cycle.
        private const int MaxModelAttempts = 3;

        public GuardShortageJob(
            IRagService ragService,
            IFeedbackAnalysisService feedbackService,
            IAuditLogsService auditLogsService,
            ILLMReportScheduleRepository scheduleRepo,
            IWorkbenchRunExecutor runExecutor,
            IDocumentBatchesRepository documentBatchesRepo,
            ILicensedPeriodService licensedPeriod,
            IReportNotificationService reportNotification,
            IReportApprovalService reportApproval,
            ApplicationDbContext db,
            IOptions<ReportGenerationFallbackOptions> fallbackOptions)
        {
            _reportApproval = reportApproval;
            _ragService = ragService;
            _feedbackservice = feedbackService;
            _auditLogsService = auditLogsService;
            _scheduleRepo = scheduleRepo;
            _runExecutor = runExecutor;
            _documentBatchesRepo = documentBatchesRepo;
            _licensedPeriod = licensedPeriod;
            _reportNotification = reportNotification;
            _db = db;
            _fallback = fallbackOptions?.Value ?? new ReportGenerationFallbackOptions();
        }

        // Azure OpenAI batch polling. Driven by BatchPollingBackgroundService.
        public async Task RunJobAsync()
        {
            string jobName = "BatchPolling";

            try
            {
                await _auditLogsService.LogHangfireJobAsync(0, jobName, "Started", "Hangfire job triggered");

                var batchJobs = await _ragService.GetPendingBatchJobsAsync();

                if (!batchJobs.Any())
                {
                    await _auditLogsService.LogHangfireJobAsync(0, jobName, "NoJobs", "No pending jobs found");
                    return;
                }

                foreach (var batchJob in batchJobs)
                {
                    try
                    {
                        string result = await _ragService.PollBatchCompletionAsync(batchJob.Id);
                        await _auditLogsService.LogHangfireJobAsync(batchJob.BusinessUnitId, jobName, "Success",
                            $"Batch {batchJob.Id} completed: {result}");
                    }
                    catch (Exception ex)
                    {
                        await _auditLogsService.LogHangfireJobAsync(batchJob.BusinessUnitId, jobName, "Error",
                            $"Error polling batch {batchJob.Id}: {ex.Message}");
                    }
                }

                await _auditLogsService.LogHangfireJobAsync(0, jobName, "Finished", "Hangfire job finished");
            }
            catch (Exception ex)
            {
                await _auditLogsService.LogHangfireJobAsync(0, jobName, "FatalError", ex.Message);
            }
        }

        // Driven by the LLMReportSchedules table, plus a fallback for everything
        // it doesn't cover.
        //
        // Two sources feed the same per-unit pipeline:
        //   Schedule — (Company, Unit) rows with IsScheduleEnabled = true, using
        //              their own ReportGenerationDays. Unchanged behaviour.
        //   Fallback — live units with no enabled schedule row, using
        //              ReportGenerationFallback.IntervalDays (default 14). These
        //              units were previously ignored entirely.
        //
        // Both sources share the same license gate: a unit without an active
        // license generates nothing.
        //
        // For every unit that reaches the pipeline, this method:
        //   1. Reads the cadence (per-unit schedule, or the fallback interval).
        //   2. Looks at the unit's last LLMReport.CreatedAt; if the cadence hasn't
        //      elapsed yet, skips this cycle.
        //   3. Fetches DailyFeedback rows for the window [lastReport, now]
        //      (or [now - days, now] on the very first run).
        //   4. If no feedback in window → skip (spec: "no data → no report").
        //   5. Otherwise runs the Workbench engine inline.
        //   6. Saves LLMReport + TopThemes + TopRecommendations + TopActions in a
        //      single transaction, as PendingReview.
        //
        // What it deliberately does NOT do any more is notify the manager. Every
        // report generated here waits in the admin review queue; the configured
        // reviewers are emailed once per run at the end of this method, and the
        // report only reaches its manager when one of them approves it. Set
        // ReportApproval.Enabled = false to restore the old notify-on-generate
        // behaviour.
        public async Task RunJobListAsync()
        {
            try
            {
                // Heartbeat — proves from LLMLogs alone that the background service
                // is actually firing this tick. If you don't see a "Heartbeat" row
                // within the last minute, the running binary doesn't contain this
                // code (deploy + restart needed) or the hosted service crashed.
                await _auditLogsService.LogHangfireJobAsync(0, "FeedbackAnalysis", "Heartbeat",
                    $"Scheduler tick at {DateTime.UtcNow:O}");

                // Admin-configured schedules. These keep their own per-unit
                // ReportGenerationDays and are unaffected by the fallback below.
                var schedules = await _scheduleRepo.GetEnabledSchedulesAsync();

                // Fallback set — every live unit with NO enabled schedule row.
                // Without this they were invisible to the scheduler; they now
                // generate a report every FallbackIntervalDays (default 14).
                var fallbackDays = _fallback.Enabled ? _fallback.IntervalDays : 0;
                var fallbackUnits = new List<(uint ProfileId, uint UnitId)>();
                if (fallbackDays > 0)
                {
                    fallbackUnits = await _scheduleRepo.GetUnitsWithoutEnabledScheduleAsync();
                }
                else if (_fallback.Enabled)
                {
                    await _auditLogsService.LogHangfireJobAsync(0, "FeedbackAnalysis", "FallbackDisabled",
                        $"ReportGenerationFallback.Enabled=true but IntervalDays={_fallback.IntervalDays} (<=0); fallback skipped.");
                }

                if (schedules.Count == 0 && fallbackUnits.Count == 0)
                {
                    await _auditLogsService.LogHangfireJobAsync(0, "FeedbackAnalysis", "NoEnabledSchedules",
                        "No LLMReportSchedules rows with IsScheduleEnabled=true and no fallback-eligible units; scheduler idle.");
                    return;
                }

                var (defaultPromptId, defaultModelIds) = await ResolveDefaultsAsync();
                if (defaultPromptId <= 0 || defaultModelIds.Count == 0)
                {
                    await _auditLogsService.LogHangfireJobAsync(0, "FeedbackAnalysis", "CriticalError",
                        "Cannot run: no default PromptId / ModelIds resolved.");
                    return;
                }

                await _auditLogsService.LogHangfireJobAsync(0, "FeedbackAnalysis", "EngineDefaults",
                    $"PromptId={defaultPromptId} (default PromptTemplate), " +
                    $"ModelCandidates=[{string.Join(",", defaultModelIds)}] from ModelConfiguration " +
                    $"(first = database default; tried in order, max {MaxModelAttempts} per unit)");

                var now = DateTime.UtcNow;

                // License gate for BOTH paths, resolved in one round-trip instead
                // of one query per unit. A unit missing from this set holds no
                // active license and generates nothing, scheduled or not.
                var candidateUnitIds = schedules.Select(s => (long)s.UnitId)
                    .Concat(fallbackUnits.Select(u => (long)u.UnitId))
                    .Distinct()
                    .ToList();
                var licensedUnitIds = await _licensedPeriod.GetActivelyLicensedUnitIdsAsync(candidateUnitIds);

                await _auditLogsService.LogHangfireJobAsync(0, "FeedbackAnalysis", "Candidates",
                    $"scheduled={schedules.Count}, fallback={fallbackUnits.Count} " +
                    $"(every {fallbackDays} days), licensed={licensedUnitIds.Count}/{candidateUnitIds.Count}");

                // ReportId → the workbench run it came out of, for everything
                // generated in this cycle. A report awaiting review has no
                // publish-log row yet — which is where the workbench flow keeps
                // its RunId — so this is the only place the pairing is known, and
                // it goes into the reviewer email below.
                var runIdByReportId = new Dictionary<long, int>();

                foreach (var schedule in schedules)
                {
                    try
                    {
                        await ProcessUnitAsync(schedule.ProfileId, schedule.UnitId, schedule.ReportGenerationDays,
                            "Schedule", licensedUnitIds, defaultPromptId, defaultModelIds, now, runIdByReportId);
                    }
                    catch (Exception ex)
                    {
                        await _auditLogsService.LogHangfireJobAsync(schedule.UnitId, "FeedbackAnalysis", "Error", ex.Message);
                    }
                }

                foreach (var (profileId, unitId) in fallbackUnits)
                {
                    try
                    {
                        await ProcessUnitAsync(profileId, unitId, fallbackDays,
                            "Fallback", licensedUnitIds, defaultPromptId, defaultModelIds, now, runIdByReportId);
                    }
                    catch (Exception ex)
                    {
                        await _auditLogsService.LogHangfireJobAsync(unitId, "FeedbackAnalysis", "Error", ex.Message);
                    }
                }

                // One reviewer email per run, covering every report this run (or
                // an earlier failed run) added to the queue — not one per unit.
                // Reports already announced are excluded inside the service, so a
                // duplicate execution of this job re-notifies nobody.
                try
                {
                    var reviewerNotify = await _reportApproval.NotifyReviewersAsync(runIdByReportId);

                    await _auditLogsService.LogHangfireJobAsync(
                        0, "FeedbackAnalysis",
                        reviewerNotify.EmailSent || reviewerNotify.InAppCreated > 0 ? "ReviewersNotified" : "ReviewersNotifySkipped",
                        reviewerNotify.ToString());
                }
                catch (Exception ex)
                {
                    // Reports are already safe in the queue; a failed reviewer
                    // mail must not fail the generation run. The next tick
                    // retries because the rows stay unannounced.
                    await _auditLogsService.LogHangfireJobAsync(0, "FeedbackAnalysis", "ReviewersNotifyError", ex.Message);
                }
            }
            catch (Exception ex)
            {
                await _auditLogsService.LogHangfireJobAsync(0, "FeedbackAnalysis", "CriticalError", ex.Message);
            }
        }

        // One unit, one cadence. Shared by both entry paths:
        //   source = "Schedule" → days comes from LLMReportSchedules.ReportGenerationDays
        //   source = "Fallback" → days comes from ReportGenerationFallback.IntervalDays
        // Everything after the cadence value is identical, including the license
        // gate, the "no feedback → no report" rule and the save transaction.
        private async Task ProcessUnitAsync(
            uint profileIdRaw,
            uint unitId,
            int days,
            string source,
            HashSet<long> licensedUnitIds,
            int promptId,
            List<int> modelIds,
            DateTime now,
            Dictionary<long, int> runIdByReportId)
        {
            var profileId = (long)profileIdRaw;

            // Defensive: row marked enabled but no cadence value. The API's
            // UpdateScheduleAsync rejects this, but a hand-edited DB row could
            // still produce it. Skip cleanly instead of running with a guessed
            // default.
            if (days <= 0)
            {
                await _auditLogsService.LogHangfireJobAsync(unitId, "FeedbackAnalysis", "Skipped",
                    $"Source={source}: cadence={days} (<=0); refusing to assume a default.");
                return;
            }

            // License gate — automated report generation must only run for units
            // that CURRENTLY hold an active license (purchased assignment or
            // complimentary grant, backing license not expired). Applies to the
            // fallback exactly as it does to configured schedules: no license,
            // no report. Same rule as the manual create-report-manually endpoint.
            if (!licensedUnitIds.Contains((long)unitId))
            {
                await _auditLogsService.LogHangfireJobAsync(unitId, "FeedbackAnalysis", "Skipped",
                    $"CompanyId={profileId}, Source={source}: unit has no active license; automated report generation skipped.");
                return;
            }

            // Trace: one row per visit so it's obvious which units were considered.
            await _auditLogsService.LogHangfireJobAsync(
                unitId, "FeedbackAnalysis", "Visited",
                $"CompanyId={profileId}, Source={source}, ReportGenerationDays={days}");

            // Cadence gate — anchored on the last LLMReport row for the unit,
            // WHATEVER its review state. The anchor is deliberately blind to
            // whether that report was approved, and both halves of the rule
            // depend on it:
            //
            //   Within a period — a report generated at T sits in the queue
            //     unpublished. Every tick until T+interval sees it, measures
            //     elapsed < interval and skips, so an admin who takes a week to
            //     review is never sent a second report for the same period. The
            //     report row exists from the moment it is generated, which is
            //     what makes this work; nothing here waits for an approval.
            //
            //   Across periods — at T+interval the gate opens again even if that
            //     first report is STILL pending. A slow review does not stall the
            //     next cycle; the two reports queue up side by side and are
            //     approved independently, each covering its own window
            //     (the new one runs from the previous report's CreatedAt).
            //
            // Anything that gives a unit a report row — scheduled or manual —
            // therefore moves its next due date, which is the intended meaning of
            // "days since the last report".
            var lastReport = await _db.LLMReports
                .AsNoTracking()
                .Where(r => r.UnitId == unitId)
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => new
                {
                    r.Id,
                    r.CreatedAt,
                    Published = _db.InsightPublishLogs.Any(l => l.ReportId == r.Id)
                })
                .FirstOrDefaultAsync();

            var lastReportAt = lastReport?.CreatedAt;

            if (lastReport != null)
            {
                var elapsed = (now - lastReport.CreatedAt).TotalDays;
                if (elapsed < days)
                {
                    // State is logged so "why did this unit not generate?" is
                    // answerable from LLMLogs alone — in particular it shows that
                    // an unapproved report is holding the period, not a bug.
                    await _auditLogsService.LogHangfireJobAsync(unitId, "FeedbackAnalysis", "Skipped",
                        $"ReportId={lastReport.Id} ({(lastReport.Published ? "released" : "awaiting review")}) " +
                        $"created {elapsed:F2} days ago (interval={days}); next due in {days - elapsed:F2} days. " +
                        $"Review state does not affect this — the period is what is closed, not the approval.");
                    return;
                }

                if (!lastReport.Published)
                {
                    await _auditLogsService.LogHangfireJobAsync(unitId, "FeedbackAnalysis", "PriorReportPending",
                        $"ReportId={lastReport.Id} from the previous period is still awaiting review after " +
                        $"{elapsed:F2} days; generating this period's report anyway. Both stay in the queue.");
                }
            }

            // Feedback window — spec: "Verify how many days have passed between
            // the last report and the current time. If there is no data within
            // that period, the report should not be generated."
            //
            // Window resolution:
            //   1. lastReportAt (if present)  → "since the previous report"
            //   2. else now - days            → first run: one cadence interval back
            var toDate = new DateTimeOffset(now, TimeSpan.Zero);
            DateTimeOffset fromDate;
            string windowSource;
            if (lastReportAt.HasValue)
            {
                fromDate = new DateTimeOffset(DateTime.SpecifyKind(lastReportAt.Value, DateTimeKind.Utc), TimeSpan.Zero);
                windowSource = "LastLLMReport.CreatedAt";
            }
            else
            {
                fromDate = toDate.AddDays(-days);
                windowSource = $"now-{days}days";
            }

            await _auditLogsService.LogHangfireJobAsync(unitId, "FeedbackAnalysis", "FetchFeedback",
                $"CompanyId={profileId}, From={fromDate:O}, To={toDate:O}, WindowSource={windowSource}");

            var feedback = await _feedbackservice.FeedbackDataAsync(profileId, unitId, fromDate, toDate);

            await _auditLogsService.LogHangfireJobAsync(unitId, "FeedbackAnalysis", "FeedbackCount",
                $"feedbackRows={(feedback?.Count ?? -1)}, window={fromDate:O}→{toDate:O}");

            if (feedback == null || feedback.Count == 0)
            {
                // No data in window → skip. Don't write any row. Next cycle will
                // recheck immediately so freshly-arriving feedback isn't held up
                // for a full interval.
                await _auditLogsService.LogHangfireJobAsync(unitId, "FeedbackAnalysis", "Skipped",
                    $"No feedback between {fromDate:O} and now. No report generated.");
                return;
            }

            // Convert feedback → CSV (the same file the workbench builds when an
            // operator picks a company, unit and date range).
            var csv = BussinessManager.GenerateCsv(feedback);
            var csvBytes = Encoding.UTF8.GetBytes(csv);
            var csvName = $"FeedbackExport_{unitId}_{now:yyyyMMddHHmmss}.csv";

            // Model failover, over workbench runs.
            //
            // Each attempt is a real WorkbenchRunStatus row put through
            // IWorkbenchRunExecutor — the identical path the workbench takes
            // after the operator presses Run. The only difference is the trigger
            // and the Source stamp on the run.
            //
            // The engine swallows per-stage LLM exceptions on purpose so a single
            // bad stage doesn't kill the run — it records them in
            // DataQualityIssues and returns empty lists. A dead model therefore
            // does NOT surface as a failed run; it surfaces as a run whose
            // insights are empty. So judge an attempt by its extracted content,
            // not by its status, and move to the next candidate when it is empty.
            var attemptIds = modelIds.Take(MaxModelAttempts).ToList();

            int usedRunId = 0;
            int usedModelId = 0;
            string? usedResultJson = null;
            List<string> themeTexts = new();
            List<string> recommendationTexts = new();
            List<string> actionTexts = new();

            foreach (var modelId in attemptIds)
            {
                var request = new WorkbenchRunRequestDto
                {
                    PromptId = promptId,
                    ModelIds = new List<int> { modelId },
                    ProfileId = profileId,
                    UnitId = unitId,
                    FromDate = fromDate,
                    ToDate = toDate
                };

                // The run row first, exactly as POST /workbench-run does, so the
                // run has an identity before any work starts and a crash mid-run
                // leaves a visible record rather than nothing.
                //
                // ResultJson starts as the scheduler marker: the executor
                // overwrites it with the full wrapper (which carries the same
                // marker), and a run that fails or never completes keeps it. That
                // is what holds scheduled runs out of the workbench screens in
                // every state, without a column for it.
                var runStatus = new WorkbenchRunStatus
                {
                    Status = "Queued",
                    ResultJson = WorkbenchRunSources.SchedulerPlaceholderJson,
                    CreatedAt = DateTime.UtcNow
                };
                _db.WorkbenchRunStatuses.Add(runStatus);
                await _db.SaveChangesAsync();

                await _auditLogsService.LogHangfireJobAsync(unitId, "FeedbackAnalysis", "EngineStarted",
                    $"RunId={runStatus.RunId}, PromptId={promptId}, ModelId={modelId}, feedbackRows={feedback.Count}, " +
                    $"attempt={attemptIds.IndexOf(modelId) + 1}/{attemptIds.Count}");

                bool completed;
                try
                {
                    completed = await _runExecutor.ExecuteAsync(new CloverEraWorkItem
                    {
                        RunId = runStatus.RunId,
                        Request = request,
                        CsvFileBytes = csvBytes,
                        CsvFileName = csvName,
                        CsvContentType = "text/csv",
                        Source = WorkbenchRunSources.Scheduler
                    });
                }
                catch (Exception ex)
                {
                    await _auditLogsService.LogHangfireJobAsync(unitId, "FeedbackAnalysis", "EngineError",
                        $"RunId={runStatus.RunId}, ModelId={modelId}: {ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                // Re-read rather than trusting the in-memory row: the executor
                // owns the run's outcome, and it writes through its own context.
                var runRow = await _db.WorkbenchRunStatuses
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r => r.RunId == runStatus.RunId);

                if (!completed || runRow == null)
                {
                    // The executor already wrote Failed + ErrorMessage on the run.
                    await _auditLogsService.LogHangfireJobAsync(unitId, "FeedbackAnalysis", "EngineError",
                        $"RunId={runStatus.RunId}, ModelId={modelId}: run status={runRow?.Status ?? "(row missing)"}, " +
                        $"error={runRow?.ErrorMessage ?? "(none)"}");
                    continue;
                }

                // Read the insights back out of the run the same way the
                // workbench screen does, so what the reviewer sees is what the
                // operator would have seen and saved.
                var (themes, recommendations, actions) =
                    WorkbenchInsightExtractor.ExtractFromResultJson(runRow.ResultJson);

                await _auditLogsService.LogHangfireJobAsync(unitId, "FeedbackAnalysis", "EngineCompleted",
                    $"RunId={runStatus.RunId}, ModelId={modelId}, themes={themes.Count}, " +
                    $"recs={recommendations.Count}, actions={actions.Count}");

                if (themes.Count + recommendations.Count + actions.Count > 0)
                {
                    usedRunId = runStatus.RunId;
                    usedModelId = modelId;
                    usedResultJson = runRow.ResultJson;
                    themeTexts = themes;
                    recommendationTexts = recommendations;
                    actionTexts = actions;
                    break;
                }

                await _auditLogsService.LogHangfireJobAsync(unitId, "FeedbackAnalysis", "EngineEmpty",
                    $"RunId={runStatus.RunId}, ModelId={modelId} produced no themes, recommendations or actions; " +
                    $"trying next candidate model. The run's DetailedLogs hold the per-stage reason.");
            }

            if (usedRunId == 0)
            {
                // Every candidate came back empty. Saving a header row here would
                // satisfy the cadence gate and lock the unit out for a full
                // interval with nothing to show, which is exactly the failure this
                // guard exists to prevent. Skip the save so the next tick retries.
                await _auditLogsService.LogHangfireJobAsync(unitId, "FeedbackAnalysis", "Skipped",
                    $"CompanyId={profileId}, Source={source}: no run produced insight content for any of " +
                    $"ModelIds=[{string.Join(",", attemptIds)}]. No LLMReport written; will retry next cycle. " +
                    $"Check the EngineError/EngineEmpty rows above and the runs' DetailedLogs for the cause.");
                return;
            }

            // Save through the workbench's own save path.
            //
            // publish = false is the entire review gate: the report and its Top*
            // rows are written, the InsightPublishLog row is not. Across this
            // product a report counts as published once it has that row — it is
            // what the workbench reads as InsightsPublished and what the review
            // queue reads as Released. It is written later by
            // ReportApprovalService.ApproveAndReleaseAsync, stamped with the
            // admin who approved it and with this RunId.
            //
            // With the gate switched off (ReportApproval.Enabled = false) the
            // same call publishes inline, which is the pre-review behaviour.
            var publishNow = !_reportApproval.IsEnabled;

            SaveRunInsightsResultDto saveResult;
            try
            {
                saveResult = await _feedbackservice.SaveRunInsightsAsync(
                    new SaveRunInsightsRequestDto
                    {
                        RunId = usedRunId,
                        TopThemes = themeTexts,
                        TopRecommendations = recommendationTexts,
                        TopActions = actionTexts
                    },
                    userId: null,
                    publish: publishNow);
            }
            catch (Exception saveEx)
            {
                var inner = saveEx.InnerException?.Message ?? "(none)";
                await _auditLogsService.LogHangfireJobAsync(unitId, "FeedbackAnalysis", "SaveError",
                    $"RunId={usedRunId}: {saveEx.GetType().Name}: {saveEx.Message} | inner: {inner}");
                throw;
            }

            if (saveResult.Outcome == 0 || saveResult.ReportId <= 0)
            {
                await _auditLogsService.LogHangfireJobAsync(unitId, "FeedbackAnalysis", "SaveError",
                    $"RunId={usedRunId}: save-run-insights returned no report (outcome={saveResult.Outcome}). " +
                    $"Nothing written; will retry next cycle.");
                return;
            }

            var reportId = saveResult.ReportId;

            // The pairing the reviewer email needs.
            runIdByReportId[reportId] = usedRunId;

            // Stamp the worksheet the run recommended.
            //
            // A workbench report leaves LLMReport.WorksheetFile null and the read
            // path derives it later by walking the publish log's RunId back to
            // the run and matching its output against the worksheet corpus. A
            // scheduled report has no publish-log row until an admin approves it,
            // so that walk has no starting point — do the same match here, now,
            // and store the answer. Same matcher, same input, same result; only
            // the timing differs.
            try
            {
                var worksheetCorpus = await _documentBatchesRepo.GetWorksheetFileContentsAsync();
                var evalResult = WorksheetMatcher.ExtractEvalResultFromResultJson(usedResultJson);
                var worksheetFile = WorksheetMatcher.MatchWorksheetFromDb(
                    evalResult?.TestResults ?? new List<PromptfooTestResultDto>(), worksheetCorpus);

                if (!string.IsNullOrWhiteSpace(worksheetFile))
                {
                    var saved = await _db.LLMReports.FirstOrDefaultAsync(r => r.Id == reportId);
                    if (saved != null)
                    {
                        saved.WorksheetFile = worksheetFile;
                        await _db.SaveChangesAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                // The report is already saved and readable; a missing worksheet
                // recommendation is a gap in one field, not a reason to fail the
                // cycle and regenerate everything next tick.
                await _auditLogsService.LogHangfireJobAsync(unitId, "FeedbackAnalysis", "WorksheetMatchError",
                    $"ReportId={reportId}, RunId={usedRunId}: {ex.GetType().Name}: {ex.Message}");
            }

            // Read-back verification — confirms the row is actually queryable.
            var verify = await _db.LLMReports
                .AsNoTracking()
                .Where(r => r.Id == reportId)
                .Select(r => new { r.Id, r.UnitId, r.WorksheetFile, r.CreatedAt })
                .FirstOrDefaultAsync();

            await _auditLogsService.LogHangfireJobAsync(unitId, "FeedbackAnalysis", "Generated",
                $"Report saved (ReportId={reportId}, RunId={usedRunId}, source={source}, interval={days}, " +
                $"feedback={feedback.Count}, modelId={usedModelId}, themes={themeTexts.Count}, " +
                $"recs={recommendationTexts.Count}, actions={actionTexts.Count}, " +
                $"verifyRow={(verify == null ? "MISSING" : $"Id={verify.Id},UnitId={verify.UnitId},worksheet={verify.WorksheetFile ?? "(none)"}")})");

            // Generation does not notify the manager.
            //
            // The report is unpublished and the configured admins are told about
            // the whole batch once, at the end of RunJobListAsync. It reaches its
            // manager only when an admin approves it in
            // ReportApprovalService.ApproveAndReleaseAsync, which publishes and
            // notifies in that order.
            if (!publishNow)
            {
                await _auditLogsService.LogHangfireJobAsync(
                    unitId, "FeedbackAnalysis", "PendingReview",
                    $"ReportId={reportId} (RunId={usedRunId}) generated unpublished, awaiting admin review; " +
                    $"the manager has NOT been notified.");
                return;
            }

            // Review step switched off — pre-review behaviour: the save above
            // already published, so tell the manager.
            var notify = await _reportNotification.NotifyReportReadyAsync(
                unitId, reportId, fromDate.UtcDateTime, toDate.UtcDateTime);

            var delivered = notify.EmailSent || notify.InAppCreated || notify.PushSent;

            await _auditLogsService.LogHangfireJobAsync(
                unitId, "FeedbackAnalysis",
                delivered ? "Notified" : "NotifySkipped",
                $"ReportId={reportId}, {notify}");
        }

        // Default Prompt + Model resolution — entirely database-driven.
        //
        // Prompt: PromptTemplates has no IsDefault column, so the default is the
        // first active, non-deleted template (lowest Id).
        //
        // Model: returns an ORDERED CANDIDATE LIST whose HEAD is the database
        // default. The list exists because nothing in ModelConfiguration records
        // whether a deployment is still alive — that was the original defect: one
        // pick, a retired deployment answering HTTP 410, and every unit got a
        // report header with no Themes / Recommendations / Actions. The tail is
        // only ever reached when the default actually fails to produce content.
        //
        // Head selection is the same rule the rest of the system uses for picking
        // a chat model from the DB (EranCoachChatService.SelectModelByPriorityAsync,
        // CloveraPersonalSnapshotService), restricted to Search-model rows:
        //   1. IsSelected  — the admin's explicit pin in model configuration
        //   2. Claude Sonnet
        //   3. GPT-4o
        //   4. IsDefault
        //   5. whatever is left (newest first)
        //
        // Tail ordering prefers endpoints the engine can actually call. A row's
        // Endpoint must include a real chat-completions path; endpoints that are
        // just the resource hostname (e.g. "https://X.cognitiveservices.azure.com/")
        // answer 200 with an empty body, which the engine treats as a stage failure.
        //   /chat/completions      Azure OpenAI + Azure AI Foundry serverless
        //   /openai/deployments    Azure OpenAI deployment-scoped URL
        //   /messages              Anthropic / Azure AI Foundry Anthropic route
        private async Task<(int promptId, List<int> modelIds)> ResolveDefaultsAsync()
        {
            var promptId = await _db.PromptTemplates
                .AsNoTracking()
                .Where(p => p.IsActive && !p.IsDel)
                .OrderBy(p => p.Id)
                .Select(p => p.Id)
                .FirstOrDefaultAsync();

            // Search-model rows, newest first — same query shape as
            // EranCoachModelConfigRepository.GetSelectableAsync. Small set, so a
            // single round-trip and in-memory ranking is fine.
            var candidates = await _db.ModelConfigurations
                .AsNoTracking()
                .Where(m => m.ModelConfigTypeId == SearchModelTypeId && !m.IsDel)
                .OrderByDescending(m => m.CreatedAt)
                .Select(m => new { m.Id, m.IsDefault, m.IsSelected, m.ModelName, m.FriendlyName, m.Provider, m.Endpoint })
                .ToListAsync();

            // Safety net: a DB with no Search-model rows at all still gets a shot
            // rather than silently generating nothing.
            if (candidates.Count == 0)
            {
                candidates = await _db.ModelConfigurations
                    .AsNoTracking()
                    .Where(m => !m.IsDel)
                    .OrderByDescending(m => m.CreatedAt)
                    .Select(m => new { m.Id, m.IsDefault, m.IsSelected, m.ModelName, m.FriendlyName, m.Provider, m.Endpoint })
                    .ToListAsync();
            }

            static bool LooksUsable(string? ep)
            {
                if (string.IsNullOrWhiteSpace(ep)) return false;
                return ep.Contains("/chat/completions", StringComparison.OrdinalIgnoreCase)
                    || ep.Contains("/openai/deployments", StringComparison.OrdinalIgnoreCase)
                    || ep.Contains("/messages", StringComparison.OrdinalIgnoreCase);
            }

            var ordered = new List<int>();
            void Append(IEnumerable<int> ids)
            {
                foreach (var id in ids)
                    if (!ordered.Contains(id)) ordered.Add(id);
            }

            // ── Head: the database default ──
            bool Has(int id, params string[] tokens)
            {
                var m = candidates.First(c => c.Id == id);
                var hay = $"{m.ModelName} {m.FriendlyName} {m.Provider}".ToLowerInvariant();
                return tokens.All(t => hay.Contains(t));
            }

            var defaultId =
                candidates.FirstOrDefault(m => m.IsSelected)?.Id
                ?? candidates.FirstOrDefault(m => Has(m.Id, "claude", "sonnet"))?.Id
                ?? candidates.FirstOrDefault(m => Has(m.Id, "sonnet"))?.Id
                ?? candidates.FirstOrDefault(m => Has(m.Id, "gpt-4o"))?.Id
                ?? candidates.FirstOrDefault(m => Has(m.Id, "gpt4o"))?.Id
                ?? candidates.FirstOrDefault(m => m.IsDefault)?.Id
                ?? candidates.FirstOrDefault()?.Id;

            if (defaultId.HasValue) Append(new[] { defaultId.Value });

            // ── Tail: failover only ──
            Append(candidates.Where(m => LooksUsable(m.Endpoint)).Select(m => m.Id));
            Append(candidates.Select(m => m.Id));

            return (promptId, ordered);
        }
    }
}
