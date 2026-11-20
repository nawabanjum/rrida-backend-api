using System.Collections.Generic;
using System.Threading.Tasks;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Services.Interfaces;

namespace CloverBackoffice.Services.Implementation
{
    public class InsightsGenerationService : IInsightsGenerationService
    {
        private readonly IInsightsRepository _insightsRepository;

        public InsightsGenerationService(IInsightsRepository insightsRepository)
        {
            _insightsRepository = insightsRepository;
        }

        public async Task<EngagementInsights> GetLatestInsightsAsync(uint businessUnitId)
        {
            return await _insightsRepository.GetLatestInsightsAsync(businessUnitId);
        }

        public async Task<List<EngagementInsights>> GetInsightsHistoryAsync(uint businessUnitId, int days, int page = 1, int pageSize = 10)
        {
            return await _insightsRepository.GetInsightsHistoryAsync(businessUnitId, days, page, pageSize);
        }
    }
}


