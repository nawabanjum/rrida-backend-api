using CloverBackoffice.BusinessModel;
using CloverBackoffice.Infrastructure.DTO;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Interfaces
{
    public interface IOpportunityService
    {
        Task<Opportunity> UpsertAsync(Opportunity opportunity);
        Task<IEnumerable<Opportunity>> GetAllAsync(int partnerId, int? userId = null);
        Task<Opportunity> GetByIdAsync(int id);
        Task<object> GetAllLookupsAsync();
        Task<bool> CheckCompanyExistsAsync(int profileId);
        Task<List<OpportunityListDto>> GetOpportunities(OpportunityFilterRequest filter);
        Task<bool> UpdateOpportunityAsync(UpdateOpportunityDTO dto);
        Task<bool> CheckOpportunityExistsAsync(int profileId);
        Task<OpportunityDetailDto?> GetOpportunityDetailAsync(int id);
        Task<OpportunityDashboardDto?> GetOpportunityDashboardAsync(int profileId);
        Task<DomainCheckResponse> CheckDomainExistsAsync(string domainName);
        Task<OpportunityCombinedResponseDto> GetOpportunitiesWithStatsAsync(int userId, int profileId, OpportunityFilterRequest filter);

    }
}
