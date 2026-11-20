using CloverBackoffice.Infrastructure.Models;

namespace CloverBackoffice.Repository.Interfaces
{
    public interface IPaymentRepository
    {
        Task<Payment> CreateAsync(Payment payment);
        Task<Payment> UpdateAsync(Payment payment);
        Task<Payment?> GetByIdAsync(int id);
        Task<List<Payment>> GetByProfileIdAsync(uint profileId, int page, int pageSize);
        Task<int> GetTotalCountByProfileIdAsync(uint profileId);

        // Unit-scoped payment history. Payments have no unit column, so a unit's
        // transactions are the payments of the subscription its licenses belong
        // to (companySubscriptionId resolved from the unit).
        Task<List<Payment>> GetByCompanySubscriptionIdAsync(int companySubscriptionId, int page, int pageSize);
        Task<int> GetTotalCountByCompanySubscriptionIdAsync(int companySubscriptionId);
        Task<bool> HasSucceededPaymentInPeriodAsync(int companySubscriptionId, DateTime periodStart, DateTime periodEnd);
        Task<bool> HasPendingInvoiceForSubscriptionAsync(int companySubscriptionId);
        Task<List<Payment>> GetPendingInvoicesForSubscriptionAsync(int companySubscriptionId);
    }
}
