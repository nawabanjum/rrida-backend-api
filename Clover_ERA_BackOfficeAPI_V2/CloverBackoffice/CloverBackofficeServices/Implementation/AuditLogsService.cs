using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Implementation
{
    public class AuditLogsService : IAuditLogsService
    {
        private readonly IAuditLogsRepository _repository;

        public AuditLogsService(IConfiguration config, IAuditLogsRepository repository)
        {
            _repository = repository;
        }

        public async Task<AuditLogs> AddAuditLogsAsync(AuditLogs model)
        {
            return await _repository.AddAuditLogsAsync(model);

        }
        public async Task<LLMLog> AddLLMLogAsync(LLMLog model)
        {
            return await _repository.AddLLMLogAsync(model);
        }
        public async Task<LLMLog> LogHangfireJobAsync(uint unitId, string jobName, string status, string message, DateTime? fromDate = null, DateTime? toDate = null)
        {
            // Echo to console (helps tail logs in dev) but ALSO persist — including
            // system-level entries where unitId = 0. The previous early-return on
            // unitId=0 hid CriticalError reasons from the DB and made the per-unit
            // logic appear broken when it was the job-level setup that bailed.
            Console.WriteLine($"[JOB-LOG] Unit={unitId} Job={jobName} Status={status} Msg={message}");

            var log = new LLMLog
            {
                Timestamp = DateTime.UtcNow,
                UserId = 0,
                Endpoint = $"Hangfire/{jobName}",
                HttpMethod = "JOB",
                RequestData = $"UnitId={unitId}",
                ResponseData = message,
                UnitId = (int)unitId,
                TokenCount = 0,
                PromptTokens = null,
                FromDate = fromDate ?? DateTime.UtcNow.AddDays(-14),
                ToDate = toDate ?? DateTime.UtcNow,
                Status = status
            };

            return await _repository.LogHangfireJobAsync(log);
        }

    }
}
