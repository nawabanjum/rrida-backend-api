using CloverBackoffice.Infrastructure.Models.TeamQuestions;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Services.Interfaces;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Implementation
{
    // Same snapshot-diff audit as DailyFeedbackAuditService, run against
    // TeamDailyFeedback: compare every live row to the newest audit row for that
    // Id and write Insert / Update / Delete entries for whatever moved.
    public class TeamDailyFeedbackAuditService : ITeamDailyFeedbackAuditService
    {
        private readonly ITeamDailyFeedbackAuditLogRepository _repository;
        private readonly ILogger<TeamDailyFeedbackAuditService> _logger;

        public TeamDailyFeedbackAuditService(
            ITeamDailyFeedbackAuditLogRepository repository,
            ILogger<TeamDailyFeedbackAuditService> logger)
        {
            _repository = repository;
            _logger = logger;
        }

        public async Task PerformAuditAsync()
        {
            var currentFeedbacks = await _repository.GetAllTeamDailyFeedbacksAsync();
            var auditSnapshot = await _repository.GetLatestAuditSnapshotAsync();
            var recordCount = await _repository.GetTeamDailyFeedbackCountAsync();

            var currentMap = currentFeedbacks.ToDictionary(f => (long)f.Id);
            var newAuditLogs = new List<TeamDailyFeedbackAuditLog>();

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
                    newAuditLogs.Add(new TeamDailyFeedbackAuditLog
                    {
                        Id = auditId,
                        SubmissionDateTime = lastAudit.SubmissionDateTime,
                        BusinessUnitId = lastAudit.BusinessUnitId,
                        SentimentType = lastAudit.SentimentType,
                        TeamDailyQuestionId = lastAudit.TeamDailyQuestionId,
                        TeamDailyReasonId = lastAudit.TeamDailyReasonId,
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
                _logger.LogInformation("TeamDailyFeedbackAudit: Logged {Count} audit entries ({Inserts} inserts, {Updates} updates, {Deletes} deletes)",
                    newAuditLogs.Count,
                    newAuditLogs.Count(l => l.OperationType == "Insert"),
                    newAuditLogs.Count(l => l.OperationType == "Update"),
                    newAuditLogs.Count(l => l.OperationType == "Delete"));
            }
        }

        private static bool HasFieldsChanged(TeamDailyFeedback feedback, TeamDailyFeedbackAuditLog audit)
        {
            return feedback.SubmissionDateTime != audit.SubmissionDateTime
                || (long)feedback.BusinessUnitId != audit.BusinessUnitId
                || (int)feedback.SentimentType != audit.SentimentType
                || (long)feedback.TeamDailyQuestionId != audit.TeamDailyQuestionId
                || (long)feedback.TeamDailyReasonId != audit.TeamDailyReasonId
                || feedback.FreeText != audit.FreeText;
        }

        private static TeamDailyFeedbackAuditLog CreateAuditLog(TeamDailyFeedback feedback, string operationType, int versionNumber, int recordCount)
        {
            return new TeamDailyFeedbackAuditLog
            {
                Id = (long)feedback.Id,
                SubmissionDateTime = feedback.SubmissionDateTime,
                BusinessUnitId = (long)feedback.BusinessUnitId,
                SentimentType = (int)feedback.SentimentType,
                TeamDailyQuestionId = (long)feedback.TeamDailyQuestionId,
                TeamDailyReasonId = (long)feedback.TeamDailyReasonId,
                FreeText = feedback.FreeText,
                LoggedAt = DateTime.UtcNow,
                VersionNumber = versionNumber,
                OperationType = operationType,
                RecordCountSnapshot = recordCount
            };
        }
    }
}
