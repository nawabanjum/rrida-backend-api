using CloverBackoffice.Infrastructure.Models;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class CreatePromoCodeDto
    {
        public required string Code { get; set; }
        public decimal DiscountPercent { get; set; }
        public uint? ProfileId { get; set; }
        public BillingCycle? ApplicableBillingCycle { get; set; }
        public int? MaxUsageCount { get; set; }
        public DateTime ExpiryDate { get; set; }
        public bool IsLifetime { get; set; } = false;
    }

    public class UpdatePromoCodeDto
    {
        public decimal? DiscountPercent { get; set; }
        public uint? ProfileId { get; set; }
        public BillingCycle? ApplicableBillingCycle { get; set; }
        public int? MaxUsageCount { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public bool? IsActive { get; set; }
        public bool? IsLifetime { get; set; }
    }

    public class PromoCodeResponseDto
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public decimal DiscountPercent { get; set; }
        public uint? ProfileId { get; set; }
        public string? CompanyName { get; set; }
        public string? ApplicableBillingCycle { get; set; }
        public int? MaxUsageCount { get; set; }
        public int CurrentUsageCount { get; set; }
        public DateTime ExpiryDate { get; set; }
        public bool IsActive { get; set; }
        public bool IsExpired { get; set; }
        public bool IsExhausted { get; set; }
        public bool IsLifetime { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class ValidatePromoCodeRequestDto
    {
        public required string Code { get; set; }
        public BillingCycle BillingCycle { get; set; }
    }

    public class ValidatePromoCodeResponseDto
    {
        public bool IsValid { get; set; }
        public decimal DiscountPercent { get; set; }
        public bool IsLifetime { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
