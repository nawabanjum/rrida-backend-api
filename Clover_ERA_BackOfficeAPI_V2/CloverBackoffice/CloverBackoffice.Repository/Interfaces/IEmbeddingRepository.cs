using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Repository.Interfaces
{
    public interface IEmbeddingRepository
    {
        Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default);
    }
}
