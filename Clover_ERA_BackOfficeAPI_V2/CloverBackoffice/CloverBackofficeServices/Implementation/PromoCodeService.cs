using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Services.Interfaces;

namespace CloverBackoffice.Services.Implementation
{
    public class PromoCodeService : IPromoCodeService
    {
        private readonly IPromoCodeRepository _promoCodeRepo;

        public PromoCodeService(IPromoCodeRepository promoCodeRepo)
        {
            _promoCodeRepo = promoCodeRepo;
        }

        public async Task<PromoCodeResponseDto> CreateAsync(CreatePromoCodeDto dto, int createdByUserId)
        {
            if (string.IsNullOrWhiteSpace(dto.Code))
                throw new InvalidOperationException("Promo code cannot be empty.");

            if (dto.DiscountPercent <= 0 || dto.DiscountPercent > 100)
                throw new InvalidOperationException("Discount percent must be between 1 and 100.");

            if (dto.ExpiryDate <= DateTime.UtcNow)
                throw new InvalidOperationException("Expiry date must be in the future.");

            var existing = await _promoCodeRepo.GetByCodeAsync(dto.Code.Trim().ToUpper());
            if (existing != null)
                throw new InvalidOperationException($"Promo code '{dto.Code}' already exists.");

            var promoCode = new PromoCode
            {
                Code = dto.Code.Trim().ToUpper(),
                DiscountPercent = dto.DiscountPercent,
                ProfileId = dto.ProfileId,
                ApplicableBillingCycle = dto.ApplicableBillingCycle,
                MaxUsageCount = dto.MaxUsageCount,
                ExpiryDate = dto.ExpiryDate,
                IsLifetime = dto.IsLifetime,
                CreatedByUserId = createdByUserId
            };

            var created = await _promoCodeRepo.CreateAsync(promoCode);

            // Re-fetch with navigation properties
            var result = await _promoCodeRepo.GetByIdAsync(created.Id);
            return MapToDto(result!);
        }

        public async Task<List<PromoCodeResponseDto>> GetAllAsync()
        {
            var promoCodes = await _promoCodeRepo.GetAllAsync();
            return promoCodes.Select(MapToDto).ToList();
        }

        public async Task<PromoCodeResponseDto?> GetByIdAsync(int id)
        {
            var promoCode = await _promoCodeRepo.GetByIdAsync(id);
            return promoCode == null ? null : MapToDto(promoCode);
        }

        public async Task<PromoCodeResponseDto> UpdateAsync(int id, UpdatePromoCodeDto dto)
        {
            var promoCode = await _promoCodeRepo.GetByIdAsync(id)
                ?? throw new InvalidOperationException("Promo code not found.");

            if (dto.DiscountPercent.HasValue)
            {
                if (dto.DiscountPercent.Value <= 0 || dto.DiscountPercent.Value > 100)
                    throw new InvalidOperationException("Discount percent must be between 1 and 100.");
                promoCode.DiscountPercent = dto.DiscountPercent.Value;
            }

            if (dto.ProfileId.HasValue)
                promoCode.ProfileId = dto.ProfileId.Value == 0 ? null : dto.ProfileId;

            if (dto.ApplicableBillingCycle.HasValue)
                promoCode.ApplicableBillingCycle = dto.ApplicableBillingCycle;

            if (dto.MaxUsageCount.HasValue)
                promoCode.MaxUsageCount = dto.MaxUsageCount;

            if (dto.ExpiryDate.HasValue)
            {
                if (dto.ExpiryDate.Value <= DateTime.UtcNow)
                    throw new InvalidOperationException("Expiry date must be in the future.");
                promoCode.ExpiryDate = dto.ExpiryDate.Value;
            }

            if (dto.IsActive.HasValue)
                promoCode.IsActive = dto.IsActive.Value;

            if (dto.IsLifetime.HasValue)
                promoCode.IsLifetime = dto.IsLifetime.Value;

            await _promoCodeRepo.UpdateAsync(promoCode);

            var result = await _promoCodeRepo.GetByIdAsync(id);
            return MapToDto(result!);
        }

        public async Task DeleteAsync(int id)
        {
            var promoCode = await _promoCodeRepo.GetByIdAsync(id)
                ?? throw new InvalidOperationException("Promo code not found.");

            promoCode.IsDel = true;
            await _promoCodeRepo.UpdateAsync(promoCode);
        }

        public async Task<ValidatePromoCodeResponseDto> ValidateAsync(string code, uint profileId, BillingCycle billingCycle)
        {
            var promoCode = await _promoCodeRepo.GetByCodeAsync(code.Trim().ToUpper());

            if (promoCode == null)
                return Invalid("Invalid promo code.");

            if (!promoCode.IsActive)
                return Invalid("This promo code is no longer active.");

            if (promoCode.ExpiryDate <= DateTime.UtcNow)
                return Invalid("This promo code has expired.");

            if (promoCode.MaxUsageCount.HasValue && promoCode.CurrentUsageCount >= promoCode.MaxUsageCount.Value)
                return Invalid("This promo code has reached its usage limit.");

            if (promoCode.ProfileId.HasValue && promoCode.ProfileId.Value != profileId)
                return Invalid("This promo code is not applicable to your company.");

            if (promoCode.ApplicableBillingCycle.HasValue && promoCode.ApplicableBillingCycle.Value != billingCycle)
                return Invalid($"This promo code is only valid for {promoCode.ApplicableBillingCycle.Value} billing.");

            var alreadyUsed = await _promoCodeRepo.HasCompanyUsedCodeAsync(promoCode.Id, profileId);
            if (alreadyUsed)
                return Invalid("Your company has already used this promo code.");

            var discountMessage = promoCode.IsLifetime
                ? $"Promo code applied! You get {promoCode.DiscountPercent}% off on every payment."
                : $"Promo code applied! You get {promoCode.DiscountPercent}% off your first payment.";

            return new ValidatePromoCodeResponseDto
            {
                IsValid = true,
                DiscountPercent = promoCode.DiscountPercent,
                IsLifetime = promoCode.IsLifetime,
                Message = discountMessage
            };
        }

        public async Task RecordUsageAsync(int promoCodeId, uint profileId, int? companySubscriptionId)
        {
            var promoCode = await _promoCodeRepo.GetByIdAsync(promoCodeId)
                ?? throw new InvalidOperationException("Promo code not found.");

            // Record usage
            var usage = new PromoCodeUsage
            {
                PromoCodeId = promoCodeId,
                ProfileId = profileId,
                CompanySubscriptionId = companySubscriptionId
            };
            await _promoCodeRepo.CreateUsageAsync(usage);

            // Increment usage count
            promoCode.CurrentUsageCount++;
            await _promoCodeRepo.UpdateAsync(promoCode);
        }

        private static ValidatePromoCodeResponseDto Invalid(string message) => new()
        {
            IsValid = false,
            DiscountPercent = 0,
            Message = message
        };

        private static PromoCodeResponseDto MapToDto(PromoCode p) => new()
        {
            Id = p.Id,
            Code = p.Code,
            DiscountPercent = p.DiscountPercent,
            ProfileId = p.ProfileId,
            CompanyName = p.Profile?.ProfileName,
            ApplicableBillingCycle = p.ApplicableBillingCycle?.ToString(),
            MaxUsageCount = p.MaxUsageCount,
            CurrentUsageCount = p.CurrentUsageCount,
            ExpiryDate = p.ExpiryDate,
            IsActive = p.IsActive,
            IsExpired = p.ExpiryDate <= DateTime.UtcNow,
            IsExhausted = p.MaxUsageCount.HasValue && p.CurrentUsageCount >= p.MaxUsageCount.Value,
            IsLifetime = p.IsLifetime,
            CreatedAt = p.CreatedAt
        };
    }
}
