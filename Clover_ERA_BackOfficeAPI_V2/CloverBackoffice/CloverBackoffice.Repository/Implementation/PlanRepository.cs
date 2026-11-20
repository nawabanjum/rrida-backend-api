using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Repository.Implementation
{
    public class PlanRepository : IPlanRepository
    {
        private readonly ApplicationDbContext _context;

        public PlanRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<List<Plan>> GetAllActiveAsync()
        {
            return await _context.Plans
                .Where(p => p.IsActive)
                .OrderBy(p => p.SortOrder)
                .ToListAsync();
        }

        public async Task<Plan?> GetBySlugAsync(string slug)
        {
            return await _context.Plans
                .FirstOrDefaultAsync(p => p.Slug == slug && p.IsActive);
        }

        public async Task<Plan?> GetByIdAsync(int id)
        {
            return await _context.Plans
                .FirstOrDefaultAsync(p => p.Id == id && p.IsActive);
        }
    }
}
