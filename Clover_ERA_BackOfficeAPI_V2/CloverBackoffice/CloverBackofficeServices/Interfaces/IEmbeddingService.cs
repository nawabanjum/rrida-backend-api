using System.Threading.Tasks;

namespace CloverBackoffice.Services.Interfaces
{
    public interface IEmbeddingService
    {
        Task<List<IReadOnlyList<float>>> GenerateEmbeddingsAsync(List<string> texts);
        //Task<float[]> GenerateEmbeddingAsync(string text);
    }
}


