using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Repository.Implementation
{
    public class AuditLogsRepository : IAuditLogsRepository
    {
        private readonly ApplicationDbContext _context;
        public AuditLogsRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<AuditLogs> AddAuditLogsAsync(AuditLogs model)
        {
            if (model != null)
            {
                await _context.AddAsync(model);
                await _context.SaveChangesAsync();
            }
            return model;
        }
        public async Task<LLMLog> AddLLMLogAsync(LLMLog model)
        {
            if (model != null)
            {
                await _context.AddAsync(model);
                await _context.SaveChangesAsync();
            }
            return model;
        }
        public async Task<LLMLog> LogHangfireJobAsync(LLMLog model)
        {
            if (model == null) return null;

            // No dedup. The previous per-day dedup keyed on
            // (UnitId, Status, Endpoint) silently dropped every cycle after the
            // first one of the day, which made the scheduler look dead and hid
            // the actual reason each cycle skipped or failed. The scheduler
            // runs every minute and emits per-stage rows (FetchFeedback,
            // FeedbackCount, EngineStarted, EngineCompleted, Generated,
            // SaveError, Skipped) — every one is meaningful and must persist
            // so the operator can read the trail.
            await _context.LLMLogs.AddAsync(model);
            await _context.SaveChangesAsync();
            return model;
        }
    }
}
