using System.Collections.Generic;
using System.Threading.Tasks;
using CloverBackoffice.Infrastructure.Models;

namespace CloverBackoffice.Services.Interfaces
{
    public interface IInsightsGenerationService
    {
        Task<EngagementInsights> GetLatestInsightsAsync(uint businessUnitId);
        Task<List<EngagementInsights>> GetInsightsHistoryAsync(uint businessUnitId, int days, int page = 1, int pageSize = 10);
    }
}


