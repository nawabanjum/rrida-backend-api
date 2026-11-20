using Azure;
using Azure.AI.OpenAI;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Services.Interfaces;
using OpenAI.Embeddings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Implementation
{
    public class EmbeddingService : IEmbeddingService
    {
        private readonly IDocumentEmbeddingRepository _repository;
        private AzureOpenAIClient? _client;
        private string? _deploymentName;

        public EmbeddingService(IDocumentEmbeddingRepository repository)
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

        public async Task<List<IReadOnlyList<float>>> GenerateEmbeddingsAsyncold(List<string> texts)
        {
            await EnsureInitializedAsync();

            var embeddingClient = _client!.GetEmbeddingClient(_deploymentName);
            var response = await embeddingClient.GenerateEmbeddingsAsync(texts);

            var result = new List<IReadOnlyList<float>>();

            var embeddings = response.Value;

            foreach (var embedding in embeddings)
            {
                var vector = embedding.GetType().GetProperty("Vector")?.GetValue(embedding) as IReadOnlyList<float>
                           ?? embedding.GetType().GetProperty("Embedding")?.GetValue(embedding) as IReadOnlyList<float>
                           ?? embedding.GetType().GetProperty("Values")?.GetValue(embedding) as IReadOnlyList<float>;

                if (vector != null)
                {
                    result.Add(vector);
                }
            }

            return result;
        }

        public async Task<List<IReadOnlyList<float>>> GenerateEmbeddingsAsync(List<string> texts)
        {
            await EnsureInitializedAsync();

            var embeddingClient = _client!.GetEmbeddingClient(_deploymentName);
            var response = await embeddingClient.GenerateEmbeddingsAsync(texts);

            var result = new List<IReadOnlyList<float>>();

            foreach (var embeddingItem in response.Value)
            {
                var vectorField = embeddingItem.GetType().GetField("_vector", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (vectorField != null)
                {
                    var vectorMemory = vectorField.GetValue(embeddingItem) as ReadOnlyMemory<float>?;
                    if (vectorMemory.HasValue)
                    {
                        result.Add(vectorMemory.Value.ToArray());
                    }
                }
            }

            return result;
        }



    }
}
