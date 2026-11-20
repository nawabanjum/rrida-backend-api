using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Repository.Implementation
{
    public class EranCoachMemoryRepository : IEranCoachMemoryRepository
    {
        private readonly ApplicationDbContext _context;

        public EranCoachMemoryRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<EranCoachManagerMemory?> GetByManagerAsync(int managerUserId, long? businessUnitId, CancellationToken ct = default)
        {
            // Exact team match; a null team only matches the no-team memory.
            return await _context.EranCoachManagerMemories
                .Where(m => m.ManagerUserId == managerUserId && m.BusinessUnitId == businessUnitId && !m.IsDel)
                .OrderByDescending(m => m.UpdatedAt)
                .FirstOrDefaultAsync(ct);
        }

        public async Task UpsertAsync(int managerUserId, long? businessUnitId, string summary, CancellationToken ct = default)
        {
            var existing = await GetByManagerAsync(managerUserId, businessUnitId, ct);
            var now = DateTime.UtcNow;

            if (existing == null)
            {
                await _context.EranCoachManagerMemories.AddAsync(new EranCoachManagerMemory
                {
                    ManagerUserId = managerUserId,
                    BusinessUnitId = businessUnitId,
                    SummaryText = summary,
                    TurnCount = 1,
                    CreatedAt = now,
                    UpdatedAt = now
                }, ct);
            }
            else
            {
                existing.SummaryText = summary;
                existing.TurnCount += 1;
                existing.UpdatedAt = now;
            }

            await _context.SaveChangesAsync(ct);
        }
    }
}
