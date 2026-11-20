using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Services.Interfaces;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Implementation
{
    public class DailyFeedbackAuditService : IDailyFeedbackAuditService
    {
        private readonly IDailyFeedbackAuditLogRepository _repository;
        private readonly ILogger<DailyFeedbackAuditService> _logger;

        public DailyFeedbackAuditService(
            IDailyFeedbackAuditLogRepository repository,
            ILogger<DailyFeedbackAuditService> logger)
        {
            _repository = repository;
            _logger = logger;
        }

        public async Task PerformAuditAsync()
        {
            var currentFeedbacks = await _repository.GetAllDailyFeedbacksAsync();
            var auditSnapshot = await _repository.GetLatestAuditSnapshotAsync();
            var recordCount = await _repository.GetDailyFeedbackCountAsync();

            var currentMap = currentFeedbacks.ToDictionary(f => (long)f.Id);
            var newAuditLogs = new List<DailyFeedbackAuditLog>();

            // Detect Inserts and Updates
            foreach (var kvp in currentMap)
            {
                var feedbackId = kvp.Key;
                var feedback = kvp.Value;

                if (!auditSnapshot.TryGetValue(feedbackId, out var lastAudit))
                {
                    // New record - Insert
                    newAuditLogs.Add(CreateAuditLog(feedback, "Insert", 1, recordCount));
                }
                else if (lastAudit.OperationType == "Delete")
                {
                    // Re-insert after delete
                    newAuditLogs.Add(CreateAuditLog(feedback, "Insert", lastAudit.VersionNumber + 1, recordCount));
                }
                else if (HasFieldsChanged(feedback, lastAudit))
                {
                    // Update
                    newAuditLogs.Add(CreateAuditLog(feedback, "Update", lastAudit.VersionNumber + 1, recordCount));
                }
            }

            // Detect Deletes
            foreach (var kvp in auditSnapshot)
            {
                var auditId = kvp.Key;
                var lastAudit = kvp.Value;

                if (lastAudit.OperationType != "Delete" && !currentMap.ContainsKey(auditId))
                {
                    newAuditLogs.Add(new DailyFeedbackAuditLog
                    {
                        Id = auditId,
                        SubmissionDateTime = lastAudit.SubmissionDateTime,
                        BusinessUnitId = lastAudit.BusinessUnitId,
                        SentimentType = lastAudit.SentimentType,
                        DailyQuestionId = lastAudit.DailyQuestionId,
                        DailyReasonId = lastAudit.DailyReasonId,
                        FreeText = lastAudit.FreeText,
                        LoggedAt = DateTime.UtcNow,
                        VersionNumber = lastAudit.VersionNumber + 1,
                        OperationType = "Delete",
                        RecordCountSnapshot = recordCount
                    });
                }
            }

            if (newAuditLogs.Count > 0)
            {
                await _repository.AddAuditLogsAsync(newAuditLogs);
                _logger.LogInformation("DailyFeedbackAudit: Logged {Count} audit entries ({Inserts} inserts, {Updates} updates, {Deletes} deletes)",
                    newAuditLogs.Count,
                    newAuditLogs.Count(l => l.OperationType == "Insert"),
                    newAuditLogs.Count(l => l.OperationType == "Update"),
                    newAuditLogs.Count(l => l.OperationType == "Delete"));
            }
        }

        private static bool HasFieldsChanged(DailyFeedback feedback, DailyFeedbackAuditLog audit)
        {
            return feedback.SubmissionDateTime != audit.SubmissionDateTime
                || (long)feedback.BusinessUnitId != audit.BusinessUnitId
                || (int)feedback.SentimentType != audit.SentimentType
                || (long)feedback.DailyQuestionId != audit.DailyQuestionId
                || (long)(feedback.DailyReasonId ?? 0) != audit.DailyReasonId
                || feedback.FreeText != audit.FreeText;
        }

        private static DailyFeedbackAuditLog CreateAuditLog(DailyFeedback feedback, string operationType, int versionNumber, int recordCount)
        {
            return new DailyFeedbackAuditLog
            {
                Id = (long)feedback.Id,
                SubmissionDateTime = feedback.SubmissionDateTime,
                BusinessUnitId = (long)feedback.BusinessUnitId,
                SentimentType = (int)feedback.SentimentType,
                DailyQuestionId = (long)feedback.DailyQuestionId,
                DailyReasonId = (long)(feedback.DailyReasonId ?? 0),
                FreeText = feedback.FreeText,
                LoggedAt = DateTime.UtcNow,
                VersionNumber = versionNumber,
                OperationType = operationType,
                RecordCountSnapshot = recordCount
            };
        }
    }
}
