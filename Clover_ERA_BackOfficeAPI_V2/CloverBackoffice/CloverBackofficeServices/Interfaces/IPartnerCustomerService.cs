using CloverBackoffice.Infrastructure.DTO;

namespace CloverBackoffice.Services.Interfaces
{
    public interface IPartnerCustomerService
    {
        Task<List<PartnerListDto>> GetAllPartnersAsync();
        Task<List<CustomerListDto>> GetCustomersAsync(int userId);
        Task<(bool Success, string Message, CustomerListDto? Customer)> CreateCustomerAsync(CreateCustomerDto dto, int userId);
        Task<(bool Success, string Message)> ReassignCustomerAsync(uint customerId, ReassignCustomerDto dto, int actorUserId);
        Task<(bool CanDelete, string Message, List<string>? CustomerNames)> CheckPartnerDeletable(uint partnerId);
        Task<(bool Success, string Message)> SoftDeletePartnerAsync(uint partnerId, int actorUserId);
    }
}
