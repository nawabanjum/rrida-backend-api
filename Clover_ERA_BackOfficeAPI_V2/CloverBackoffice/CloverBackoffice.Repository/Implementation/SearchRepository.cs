using CloverBackoffice.Repository.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using CloverBackoffice.Infrastructure.DTO;

namespace CloverBackoffice.Repository.Implementation
{
    public class SearchRepository : ISearchRepository
    {
        private readonly SearchClient _searchClient;

        public SearchRepository(SearchClient searchClient)
        {
            _searchClient = searchClient;
        }

        public async Task<List<SearchResultDoc>> VectorSearchAsync(float[] vector, int topK = 5, CancellationToken ct = default)
        {
            var options = new SearchOptions { Size = topK };
            options.Select.Add("id");
            options.Select.Add("content");

            var vs = new VectorSearchOptions();
            var vecQuery = new VectorizedQuery(new ReadOnlyMemory<float>(vector))
            {
                Fields = { "contentVector" },
                KNearestNeighborsCount = topK
            };
            vs.Queries.Add(vecQuery);
            options.VectorSearch = vs;

            var resp = await _searchClient.SearchAsync<SearchDocument>("*", options, ct);
            var results = new List<SearchResultDoc>();

            await foreach (var r in resp.Value.GetResultsAsync())
            {
                var doc = r.Document;
                results.Add(new SearchResultDoc
                {
                    Id = doc.GetString("id") ?? Guid.NewGuid().ToString(),
                    Content = doc.GetString("content") ?? "",
                    Score = r.Score ?? 0.0,
                    Metadata = new Dictionary<string, object>()
                });
            }

            return results;
        }
    }
}
