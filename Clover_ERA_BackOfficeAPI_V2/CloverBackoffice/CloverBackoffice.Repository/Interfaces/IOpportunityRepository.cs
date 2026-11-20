using CloverBackoffice.BusinessModel;
using CloverBackoffice.Infrastructure.BusinessModel;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CloverBackoffice.Repository.Interfaces
{
    public interface IOpportunityRepository
    {
        Task<IEnumerable<LookupItem>> GetIndustriesAsync();
        Task<IEnumerable<LookupItem>> GetOpportunityTypesAsync();
        Task<IEnumerable<LookupItem>> GetCurrentRelationshipStatusesAsync();
        Task<IEnumerable<LookupItem>> GetNumberOfEmployeesAsync();

        Task<Opportunity> CreateAsync(Opportunity opportunity);
        Task<Opportunity> UpdateAsync(Opportunity opportunity);
        Task<IEnumerable<Opportunity>> GetAllAsync(int partnerId, int? userId = null);
        Task<Opportunity> GetByIdAsync(int id);

        Task<bool> CheckCompanyExistsAsync(int profileId);
        Task<bool> CheckOpportunityExistsAsync(int OpportunityId);

        Task<List<OpportunityListDto>> GetOpportunities(OpportunityFilterRequest filter);
        Task<bool> ApplyOpportunityChangesAsync(Opportunity opportunity);
        Task<OpportunityDetailDto?> GetOpportunityByIdAsync(int id);
        Task<OpportunityDashboardDto?> GetOpportunityDashboardAsync(int profileId);
        Task<Profile> DoesDomainExistAsync(string domainName);
        Task<bool> IsDomainActiveInOpportunitiesAsync(uint ProfileId);
        Task<OpportunityCombinedResponseDto> GetOpportunitiesWithStatsAsync(int userId, int? profileId, OpportunityFilterRequest filter);
        Task<int> AutoCloseOpportunitiesAsync();
        Task<Profile?> DoesBaseDomainExistAsync(string baseDomain);

    }
}
