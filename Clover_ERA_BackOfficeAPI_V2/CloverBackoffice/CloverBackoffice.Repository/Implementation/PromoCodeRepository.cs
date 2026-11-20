using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Repository.Implementation
{
    public class PromoCodeRepository : IPromoCodeRepository
    {
        private readonly ApplicationDbContext _context;
        private readonly IPaymentAuditLogRepository _auditLog;

        public PromoCodeRepository(ApplicationDbContext context, IPaymentAuditLogRepository auditLog)
        {
            _context = context;
            _auditLog = auditLog;
        }

        public async Task<PromoCode?> GetByIdAsync(int id)
        {
            return await _context.PromoCodes
                .Include(p => p.Profile)
                .FirstOrDefaultAsync(p => p.Id == id && !p.IsDel);
        }

        public async Task<PromoCode?> GetByCodeAsync(string code)
        {
            return await _context.PromoCodes
                .Include(p => p.Profile)
                .FirstOrDefaultAsync(p => p.Code == code && !p.IsDel);
        }

        public async Task<List<PromoCode>> GetAllAsync()
        {
            return await _context.PromoCodes
                .Include(p => p.Profile)
                .Where(p => !p.IsDel)
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();
        }

        public async Task<PromoCode> CreateAsync(PromoCode promoCode)
        {
            promoCode.CreatedAt = DateTime.UtcNow;
            promoCode.UpdatedAt = DateTime.UtcNow;
            await _context.PromoCodes.AddAsync(promoCode);
            await _context.SaveChangesAsync();

            await _auditLog.LogAsync(promoCode, "INSERT");
            return promoCode;
        }

        public async Task<PromoCode> UpdateAsync(PromoCode promoCode)
        {
            promoCode.UpdatedAt = DateTime.UtcNow;
            _context.PromoCodes.Update(promoCode);
            await _context.SaveChangesAsync();

            await _auditLog.LogAsync(promoCode, "UPDATE");
            return promoCode;
        }

        public async Task<bool> HasCompanyUsedCodeAsync(int promoCodeId, uint profileId)
        {
            return await _context.PromoCodeUsages
                .AnyAsync(u => u.PromoCodeId == promoCodeId && u.ProfileId == profileId);
        }

        public async Task<PromoCodeUsage> CreateUsageAsync(PromoCodeUsage usage)
        {
            usage.UsedAt = DateTime.UtcNow;
            await _context.PromoCodeUsages.AddAsync(usage);
            await _context.SaveChangesAsync();

            await _auditLog.LogAsync(usage, "INSERT");
            return usage;
        }
    }
}
