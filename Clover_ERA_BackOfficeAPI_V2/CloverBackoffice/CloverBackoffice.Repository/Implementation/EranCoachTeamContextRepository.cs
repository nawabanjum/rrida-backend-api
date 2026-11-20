using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Repository.Implementation
{
    public class EranCoachTeamContextRepository : IEranCoachTeamContextRepository
    {
        private readonly ApplicationDbContext _context;

        public EranCoachTeamContextRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<EngagementInsights?> GetLatestByUnitAsync(uint unitId, CancellationToken ct = default)
        {
            return await _context.EngagementInsights
                .Where(e => e.BusinessUnitId == unitId)
                .OrderByDescending(e => e.GeneratedAt)
                .FirstOrDefaultAsync(ct);
        }
    }
}
