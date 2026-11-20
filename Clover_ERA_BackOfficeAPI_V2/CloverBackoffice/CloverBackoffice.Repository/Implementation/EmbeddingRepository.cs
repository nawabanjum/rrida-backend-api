using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace CloverBackoffice.Repository.Implementation
{
    public class EmbeddingRepository : IEmbeddingRepository
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpClientFactory _httpFactory;
        private HttpClient? _client;
        private ModelConfiguration? _cachedEmbeddingModel;

        public EmbeddingRepository(IHttpClientFactory httpFactory, ApplicationDbContext context)
        {
            _httpFactory = httpFactory;
            _context = context;
        }

        private async Task EnsureClientInitializedAsync()
        {
            if (_client != null) return;

            _cachedEmbeddingModel = await _context.ModelConfigurations
                .Where(m => m.ModelConfigTypeId == 3 && m.IsDefault && !m.IsDel)
                .OrderByDescending(m => m.CreatedAt)
                .FirstOrDefaultAsync()
                ?? throw new InvalidOperationException("No default Embedding-model found in DB.");

            _client = _httpFactory.CreateClient();
            var baseUri = new Uri(_cachedEmbeddingModel.Endpoint.TrimEnd('/') + "/");
            _client.BaseAddress = baseUri;
            _client.DefaultRequestHeaders.Add("api-key", _cachedEmbeddingModel.ApiKey);
        }

        public async Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default)
        {
            await EnsureClientInitializedAsync();

            var url = $"openai/deployments/{_cachedEmbeddingModel!.ModelName}/embeddings?api-version={_cachedEmbeddingModel.ApiVersion}";

            // Split text into chunks if it's too long
            int chunkSize = 2000; // characters (approx safe)
            var chunks = Enumerable.Range(0, (int)Math.Ceiling((double)text.Length / chunkSize))
                                   .Select(i => text.Substring(i * chunkSize, Math.Min(chunkSize, text.Length - i * chunkSize)))
                                   .ToList();

            var allEmbeddings = new List<float[]>();

            foreach (var chunk in chunks)
            {
                var body = JsonSerializer.Serialize(new { input = chunk });

                using var res = await _client!.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json"), ct);
                res.EnsureSuccessStatusCode();

                using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
                var embedding = doc.RootElement.GetProperty("data")[0].GetProperty("embedding");

                var list = new List<float>();
                foreach (var v in embedding.EnumerateArray())
                    list.Add(v.GetSingle());

                allEmbeddings.Add(list.ToArray());
            }

            //  Merge embeddings by averaging across chunks
            if (allEmbeddings.Count == 1) return allEmbeddings[0];

            int dim = allEmbeddings[0].Length;
            var avg = new float[dim];
            foreach (var emb in allEmbeddings)
            {
                for (int i = 0; i < dim; i++)
                    avg[i] += emb[i];
            }
            for (int i = 0; i < dim; i++)
                avg[i] /= allEmbeddings.Count;

            return avg;
        }

    }
}
