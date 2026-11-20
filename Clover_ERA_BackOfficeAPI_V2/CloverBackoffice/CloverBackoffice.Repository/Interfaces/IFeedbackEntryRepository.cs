using CloverBackoffice.Infrastructure.DTO;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CloverBackoffice.Repository.Interfaces
{
    public interface IFeedbackEntryRepository
    {
        Task<List<FeedbackListItemDto>> GetPagedAsync(int page, int pageSize);
        Task<int> GetTotalCountAsync();
    }
}
