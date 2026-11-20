namespace CloverBackoffice.Services.Interfaces
{
    public interface IDocumentEmbeddingGeneratorService
    {
        Task<List<float[]>> GenerateEmbeddingsAsync(List<string> texts);
    }
}
