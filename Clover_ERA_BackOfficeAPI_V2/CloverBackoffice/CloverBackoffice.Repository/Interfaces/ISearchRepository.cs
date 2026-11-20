using CloverBackoffice.Infrastructure.DTO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Repository.Interfaces
{
    public interface ISearchRepository
    {
        Task<List<SearchResultDoc>> VectorSearchAsync(float[] vector, int topK = 5, CancellationToken ct = default);
    }
}
