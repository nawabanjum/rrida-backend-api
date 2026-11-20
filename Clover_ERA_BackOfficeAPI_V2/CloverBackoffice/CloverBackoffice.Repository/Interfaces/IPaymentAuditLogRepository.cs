using CloverBackoffice.Infrastructure.Models;

namespace CloverBackoffice.Repository.Interfaces
{
    public interface IPaymentAuditLogRepository
    {
        Task LogAsync(Payment payment, string operationType);
        Task LogAsync(CompanySubscription subscription, string operationType);
        Task LogAsync(PromoCode promoCode, string operationType);
        Task LogAsync(PromoCodeUsage usage, string operationType);
    }
}
