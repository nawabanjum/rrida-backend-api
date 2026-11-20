using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;

namespace CloverBackoffice.Services.Interfaces
{
    public interface IPromoCodeService
    {
        Task<PromoCodeResponseDto> CreateAsync(CreatePromoCodeDto dto, int createdByUserId);
        Task<List<PromoCodeResponseDto>> GetAllAsync();
        Task<PromoCodeResponseDto?> GetByIdAsync(int id);
        Task<PromoCodeResponseDto> UpdateAsync(int id, UpdatePromoCodeDto dto);
        Task DeleteAsync(int id);
        Task<ValidatePromoCodeResponseDto> ValidateAsync(string code, uint profileId, BillingCycle billingCycle);
        Task RecordUsageAsync(int promoCodeId, uint profileId, int? companySubscriptionId);
    }
}
