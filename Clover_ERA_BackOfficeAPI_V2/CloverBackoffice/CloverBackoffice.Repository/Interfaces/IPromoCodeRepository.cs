using CloverBackoffice.Infrastructure.Models;

namespace CloverBackoffice.Repository.Interfaces
{
    public interface IPromoCodeRepository
    {
        Task<PromoCode?> GetByIdAsync(int id);
        Task<PromoCode?> GetByCodeAsync(string code);
        Task<List<PromoCode>> GetAllAsync();
        Task<PromoCode> CreateAsync(PromoCode promoCode);
        Task<PromoCode> UpdateAsync(PromoCode promoCode);
        Task<bool> HasCompanyUsedCodeAsync(int promoCodeId, uint profileId);
        Task<PromoCodeUsage> CreateUsageAsync(PromoCodeUsage usage);
    }
}
