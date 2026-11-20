using CloverBackoffice.Infrastructure.Models;
using Pgvector;

namespace CloverBackoffice.Repository.Interfaces
{
    public interface IDocumentEmbeddingRepository
    {
        Task<DocumentEmbedding> AddAsync(DocumentEmbedding entity);
        Task<List<DocumentEmbedding>> AddBulkAsync(List<DocumentEmbedding> entities);
        // Retrieval is namespace-scoped. The default is the main knowledge base, so a caller
        // that omits the scope can never match another corpus (e.g. the ERAN Coach one).
        Task<List<(DocumentEmbedding Document, double Distance)>> SearchSimilarAsync(
            Vector queryEmbedding, int topK, string embeddingType = EmbeddingTypes.CloverKnowledgeBase);
        Task<DocumentEmbedding?> GetByIdAsync(int id);
        Task<List<DocumentEmbedding>> GetByProcessIdAsync(
            int processId, string embeddingType = EmbeddingTypes.CloverKnowledgeBase);
        Task<List<DocumentEmbedding>> GetAllAsync(string embeddingType = EmbeddingTypes.CloverKnowledgeBase);
        Task<bool> DeleteAsync(int id);
        Task<bool> DeleteByProcessIdAsync(
            int processId, string embeddingType = EmbeddingTypes.CloverKnowledgeBase);
        Task<int> GetNextProcessIdAsync();

        // PromptConfiguration CRUD
        Task<PromptConfiguration> AddPromptAsync(PromptConfiguration entity);
        Task<PromptConfiguration?> GetPromptByIdAsync(int id);
        Task<List<PromptConfiguration>> GetAllPromptsAsync();
        Task<bool> UpdatePromptAsync(PromptConfiguration entity);
        Task<bool> SoftDeletePromptAsync(int id);
        Task<PromptConfiguration?> GetDefaultPromptAsync();

        // ModelConfiguration CRUD
        Task<ModelConfiguration> AddModelAsync(ModelConfiguration entity);
        Task<ModelConfiguration?> GetModelByIdAsync(int id);
        Task<List<ModelConfiguration>> GetAllModelsAsync(int? modelConfigTypeId = null);
        Task<bool> UpdateModelAsync(ModelConfiguration entity);
        Task<bool> SoftDeleteModelAsync(int id);
        Task<ModelConfiguration?> GetDefaultModelAsync(int? modelConfigTypeId = null);
        Task ClearDefaultByTypeAsync(int modelConfigTypeId, int? excludeId = null);
        Task<ModelConfiguration?> GetModelByNameAsync(string modelName);
        Task<List<ModelConfiguration>> GetModelsByIdsAsync(List<int> ids);
        Task UpdateModelPricingAsync(int modelId, decimal inputCostPer1k, decimal outputCostPer1k);
    }
}
