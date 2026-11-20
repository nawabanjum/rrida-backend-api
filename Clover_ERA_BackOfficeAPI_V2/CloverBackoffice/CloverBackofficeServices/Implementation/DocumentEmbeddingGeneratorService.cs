using Azure;
using Azure.AI.OpenAI;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Services.Interfaces;

namespace CloverBackoffice.Services.Implementation
{
    public class DocumentEmbeddingGeneratorService : IDocumentEmbeddingGeneratorService
    {
        private readonly IDocumentEmbeddingRepository _repository;
        private AzureOpenAIClient? _client;
        private string? _deploymentName;

        public DocumentEmbeddingGeneratorService(IDocumentEmbeddingRepository repository)
        {
            _repository = repository;
        }

        private async Task EnsureInitializedAsync()
        {
            if (_client != null) return;

            var config = await _repository.GetDefaultModelAsync(modelConfigTypeId: 3)
                ?? throw new InvalidOperationException("No default Embedding-model found in DB.");

            _deploymentName = config.ModelName;
            _client = new AzureOpenAIClient(
                new Uri(config.Endpoint),
                new AzureKeyCredential(config.ApiKey)
            );
        }

        public async Task<List<float[]>> GenerateEmbeddingsAsync(List<string> texts)
        {
            await EnsureInitializedAsync();

            var embeddingClient = _client!.GetEmbeddingClient(_deploymentName);
            var response = await embeddingClient.GenerateEmbeddingsAsync(texts);

            var result = new List<float[]>();

            foreach (var embeddingItem in response.Value)
            {
                result.Add(embeddingItem.ToFloats().ToArray());
            }

            return result;
        }

    }
}
