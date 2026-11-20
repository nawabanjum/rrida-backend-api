using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Services.Interfaces;

namespace CloverBackoffice.Services.Implementation
{
    public class PaymentService : IPaymentService
    {
        private readonly IPaymentRepository _paymentRepo;
        private readonly ICompanySubscriptionRepository _subscriptionRepo;

        public PaymentService(IPaymentRepository paymentRepo, ICompanySubscriptionRepository subscriptionRepo)
        {
            _paymentRepo = paymentRepo;
            _subscriptionRepo = subscriptionRepo;
        }

        public async Task<List<PaymentHistoryDto>> GetPaymentHistoryAsync(uint profileId, int page, int pageSize)
        {
            var payments = await _paymentRepo.GetByProfileIdAsync(profileId, page, pageSize);
            var dtos = payments.Select(ToHistoryDto).ToList();

            // Stamp HasSubscription: true when the row's subscription is currently
            // Active. One batched lookup keyed on the CompanySubscriptionId each
            // ToHistoryDto already set.
            var activeSubIds = await _subscriptionRepo.GetActiveSubscriptionIdsAsync(
                dtos.Select(d => d.CompanySubscriptionId));
            foreach (var d in dtos)
                d.HasSubscription = activeSubIds.Contains(d.CompanySubscriptionId);

            return dtos;
        }

        public async Task<int> GetPaymentTotalCountAsync(uint profileId)
        {
            return await _paymentRepo.GetTotalCountByProfileIdAsync(profileId);
        }

        public async Task<PaymentHistoryDto> RequestInvoicePaymentAsync(uint profileId, int companySubscriptionId)
        {
            // Get the specific subscription (scoped to the company) to check its
            // billing period. Resolve by the exact SubscriptionId the caller passed
            // rather than "the company's newest" — a company can now hold multiple
            // independent subscriptions, and this invoice request targets exactly
            // one of them.
            var subscription = await _subscriptionRepo.GetByIdForCompanyAsync(companySubscriptionId, profileId);
            if (subscription == null)
                throw new InvalidOperationException("No active subscription found.");

            // Check if there's already a successful payment in the current billing period
            var alreadyPaid = await _paymentRepo.HasSucceededPaymentInPeriodAsync(
                companySubscriptionId, subscription.CurrentPeriodStart, subscription.CurrentPeriodEnd);
            if (alreadyPaid)
                throw new InvalidOperationException("Payment already completed for the current billing period.");

            // Check if there's already a pending invoice awaiting verification
            var hasPendingInvoice = await _paymentRepo.HasPendingInvoiceForSubscriptionAsync(companySubscriptionId);
            if (hasPendingInvoice)
                throw new InvalidOperationException("An invoice is already pending verification for this subscription.");

            var payment = new Payment
            {
                ProfileId = profileId,
                CompanySubscriptionId = companySubscriptionId,
                Amount = 0,           // Will be calculated based on subscription plan
                Currency = "usd",
                PaymentMethod = PaymentMethodType.Invoice,
                Status = PaymentStatus.AwaitingVerification,
                InvoiceDueDate = DateTime.UtcNow.AddDays(30),
                Description = $"Invoice payment - {DateTime.UtcNow:MMM yyyy}"
            };

            var created = await _paymentRepo.CreateAsync(payment);
            var createdDto = ToHistoryDto(created);
            // The subscription was just resolved above — reuse it for the flag.
            // A canceled sub (CancelAtPeriodEnd) does NOT count as live.
            createdDto.HasSubscription = !subscription.IsDel
                && subscription.Status == SubscriptionStatus.Active
                && !subscription.CancelAtPeriodEnd;
            return createdDto;
        }

        public async Task<PaymentHistoryDto> VerifyInvoiceAsync(int paymentId, int adminUserId)
        {
            var payment = await _paymentRepo.GetByIdAsync(paymentId)
                ?? throw new InvalidOperationException("Payment not found.");

            if (payment.PaymentMethod != PaymentMethodType.Invoice)
                throw new InvalidOperationException("This payment is not an invoice.");

            if (payment.Status != PaymentStatus.AwaitingVerification)
                throw new InvalidOperationException("Invoice is not in awaiting verification state.");

            payment.Status = PaymentStatus.Succeeded;
            payment.VerifiedByUserId = adminUserId;
            payment.VerifiedAt = DateTime.UtcNow;

            var updated = await _paymentRepo.UpdateAsync(payment);
            var updatedDto = ToHistoryDto(updated);
            var activeSubIds = await _subscriptionRepo.GetActiveSubscriptionIdsAsync(new[] { updated.CompanySubscriptionId });
            updatedDto.HasSubscription = activeSubIds.Contains(updated.CompanySubscriptionId);
            return updatedDto;
        }

        // Shared Payment → PaymentHistoryDto mapping. Public so other services
        // (e.g. SubscriptionService's transaction/details composition) reuse the
        // exact same transaction + card mapping instead of duplicating it.
        public static PaymentHistoryDto ToHistoryDto(Payment p) => new PaymentHistoryDto
        {
            Id = p.Id,
            // Always available (non-null FK column). HasSubscription is filled by
            // the list/enrichment paths, which resolve the subscription's status.
            CompanySubscriptionId = p.CompanySubscriptionId,
            Amount = p.Amount,
            AmountSubtotal = p.AmountSubtotal,
            DiscountAmount = p.DiscountAmount,
            DiscountCode = p.DiscountCode,
            Currency = p.Currency,
            PaymentMethod = p.PaymentMethod.ToString(),
            Status = p.Status.ToString(),
            Description = p.Description,
            InvoiceDueDate = p.InvoiceDueDate,
            CreatedAt = p.CreatedAt,
            CardholderName = p.CardholderName,
            CardBrand = p.CardBrand,
            CardLast4 = p.CardLast4,
            CardExpMonth = p.CardExpMonth,
            CardExpYear = p.CardExpYear
        };
    }
}
