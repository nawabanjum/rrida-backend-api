using CloverBackoffice.Infrastructure.DTO;

namespace CloverBackoffice.Services.Interfaces
{
    public interface IPaymentService
    {
        Task<List<PaymentHistoryDto>> GetPaymentHistoryAsync(uint profileId, int page, int pageSize);
        Task<int> GetPaymentTotalCountAsync(uint profileId);
        Task<PaymentHistoryDto> RequestInvoicePaymentAsync(uint profileId, int companySubscriptionId);
        Task<PaymentHistoryDto> VerifyInvoiceAsync(int paymentId, int adminUserId);
    }
}
