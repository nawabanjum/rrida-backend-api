using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Services.Interfaces;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Implementation
{
    public class FeedbackListService : IFeedbackListService
    {
        private readonly IFeedbackEntryRepository _repository;

        public FeedbackListService(IFeedbackEntryRepository repository)
        {
            _repository = repository;
        }

        public async Task<(List<FeedbackListItemDto> Items, int Total)> GetFeedbackPagedAsync(int page, int pageSize)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 10;

            var items = await _repository.GetPagedAsync(page, pageSize);
            var total = await _repository.GetTotalCountAsync();
            return (items, total);
        }
    }
}
