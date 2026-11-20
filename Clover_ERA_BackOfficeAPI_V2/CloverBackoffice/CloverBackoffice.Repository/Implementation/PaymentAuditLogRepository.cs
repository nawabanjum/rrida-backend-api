using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;

namespace CloverBackoffice.Repository.Implementation
{
    public class PaymentAuditLogRepository : IPaymentAuditLogRepository
    {
        private readonly ApplicationDbContext _context;

        public PaymentAuditLogRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task LogAsync(Payment payment, string operationType)
        {
            var audit = new PaymentAuditLog
            {
                Id = payment.Id,
                ProfileId = payment.ProfileId,
                CompanySubscriptionId = payment.CompanySubscriptionId,
                StripePaymentIntentId = payment.StripePaymentIntentId,
                StripeInvoiceId = payment.StripeInvoiceId,
                Amount = payment.Amount,
                AmountSubtotal = payment.AmountSubtotal,
                DiscountAmount = payment.DiscountAmount,
                DiscountCode = payment.DiscountCode,
                Currency = payment.Currency,
                PaymentMethod = payment.PaymentMethod,
                CardholderName = payment.CardholderName,
                CardLast4 = payment.CardLast4,
                CardBrand = payment.CardBrand,
                CardExpMonth = payment.CardExpMonth,
                CardExpYear = payment.CardExpYear,
                StripePaymentMethodId = payment.StripePaymentMethodId,
                CardFunding = payment.CardFunding,
                CardCountry = payment.CardCountry,
                Status = payment.Status,
                InvoiceDueDate = payment.InvoiceDueDate,
                VerifiedByUserId = payment.VerifiedByUserId,
                VerifiedAt = payment.VerifiedAt,
                Description = payment.Description,
                FailureReason = payment.FailureReason,
                CreatedAt = payment.CreatedAt,
                UpdatedAt = payment.UpdatedAt,
                LoggedAt = DateTime.UtcNow,
                OperationType = operationType
            };

            await _context.PaymentAuditLogs.AddAsync(audit);
            await _context.SaveChangesAsync();
        }

        public async Task LogAsync(CompanySubscription subscription, string operationType)
        {
            var audit = new CompanySubscriptionAuditLog
            {
                Id = subscription.Id,
                ProfileId = subscription.ProfileId,
                PlanId = subscription.PlanId,
                BillingCycle = subscription.BillingCycle,
                Status = subscription.Status,
                StripeCustomerId = subscription.StripeCustomerId,
                StripeSubscriptionId = subscription.StripeSubscriptionId,
                TeamCount = subscription.TeamCount,
                MaxTeamsAllowed = subscription.MaxTeamsAllowed,
                MaxPeopleAllowed = subscription.MaxPeopleAllowed,
                TrialStartDate = subscription.TrialStartDate,
                TrialEndDate = subscription.TrialEndDate,
                CurrentPeriodStart = subscription.CurrentPeriodStart,
                CurrentPeriodEnd = subscription.CurrentPeriodEnd,
                IsTrialUsed = subscription.IsTrialUsed,
                CanceledAt = subscription.CanceledAt,
                CancelAtPeriodEnd = subscription.CancelAtPeriodEnd,
                CreatedAt = subscription.CreatedAt,
                UpdatedAt = subscription.UpdatedAt,
                PromoCodeId = subscription.PromoCodeId,
                LifetimeDiscountPercent = subscription.LifetimeDiscountPercent,
                Discount = subscription.Discount,
                GracePeriod = subscription.GracePeriod,
                IsDel = subscription.IsDel,
                LoggedAt = DateTime.UtcNow,
                OperationType = operationType
            };

            await _context.CompanySubscriptionAuditLogs.AddAsync(audit);
            await _context.SaveChangesAsync();
        }

        public async Task LogAsync(PromoCode promoCode, string operationType)
        {
            var audit = new PromoCodeAuditLog
            {
                Id = promoCode.Id,
                Code = promoCode.Code,
                DiscountPercent = promoCode.DiscountPercent,
                ProfileId = promoCode.ProfileId,
                ApplicableBillingCycle = promoCode.ApplicableBillingCycle,
                MaxUsageCount = promoCode.MaxUsageCount,
                CurrentUsageCount = promoCode.CurrentUsageCount,
                ExpiryDate = promoCode.ExpiryDate,
                IsLifetime = promoCode.IsLifetime,
                IsActive = promoCode.IsActive,
                CreatedByUserId = promoCode.CreatedByUserId,
                CreatedAt = promoCode.CreatedAt,
                UpdatedAt = promoCode.UpdatedAt,
                IsDel = promoCode.IsDel,
                LoggedAt = DateTime.UtcNow,
                OperationType = operationType
            };

            await _context.PromoCodeAuditLogs.AddAsync(audit);
            await _context.SaveChangesAsync();
        }

        public async Task LogAsync(PromoCodeUsage usage, string operationType)
        {
            var audit = new PromoCodeUsageAuditLog
            {
                Id = usage.Id,
                PromoCodeId = usage.PromoCodeId,
                ProfileId = usage.ProfileId,
                CompanySubscriptionId = usage.CompanySubscriptionId,
                UsedAt = usage.UsedAt,
                LoggedAt = DateTime.UtcNow,
                OperationType = operationType
            };

            await _context.PromoCodeUsageAuditLogs.AddAsync(audit);
            await _context.SaveChangesAsync();
        }
    }
}
