using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;

namespace CloverBackoffice.Repository.Interfaces
{
    public interface IPartnerCustomerRepository
    {
        Task<List<PartnerListDto>> GetAllPartnersWithCustomerCountAsync();
        Task<List<CustomerListDto>> GetAllCustomersAsync();
        Task<List<CustomerListDto>> GetCustomersByPartnerIdAsync(uint partnerId);
        Task<Profile?> GetCompanyByIdAsync(uint id);
        Task<bool> IsPartnerAsync(uint profileId);
        Task<Profile> CreateCustomerCompanyAsync(CreateCustomerDto dto, uint? partnerId);
        Task<bool> ReassignCustomerAsync(uint customerId, uint newPartnerId);
        Task AddAuditLogAsync(PartnerCustomerAuditLog log);
        Task<List<Profile>> GetCustomersByPartnerForDeletionAsync(uint partnerId);
        Task<bool> SoftDeleteCompanyAsync(uint profileId);
        Task<Profile?> GetCustomerByDomainAsync(string domain);
        Task<bool> CompanyNameExistsAsync(string name);
    }
}
