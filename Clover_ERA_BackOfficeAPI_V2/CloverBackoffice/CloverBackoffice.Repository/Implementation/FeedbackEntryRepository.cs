using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CloverBackoffice.Repository.Implementation
{
    public class FeedbackEntryRepository : IFeedbackEntryRepository
    {
        private readonly ApplicationDbContext _context;

        public FeedbackEntryRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<List<FeedbackListItemDto>> GetPagedAsync(int page, int pageSize)
        {
            return await _context.Feedbacks
                .AsNoTracking()
                .OrderByDescending(f => f.SubmittedAt) // latest feedback first
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(f => new FeedbackListItemDto
                {
                    Id = f.Id,
                    Feedback = f.FeedbackText ?? string.Empty,
                    SubmittedAt = f.SubmittedAt
                })
                .ToListAsync();
        }

        public async Task<int> GetTotalCountAsync()
        {
            return await _context.Feedbacks.CountAsync();
        }
    }
}
