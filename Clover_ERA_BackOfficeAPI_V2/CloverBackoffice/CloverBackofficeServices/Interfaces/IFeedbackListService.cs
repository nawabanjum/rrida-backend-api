using CloverBackoffice.Infrastructure.DTO;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Interfaces
{
    public interface IFeedbackListService
    {
        Task<(List<FeedbackListItemDto> Items, int Total)> GetFeedbackPagedAsync(int page, int pageSize);
    }
}
