using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace CloverBackoffice.Repository.Implementation
{
    public class DocumentEmbeddingRepository : IDocumentEmbeddingRepository
    {
        private readonly ApplicationDbContext _context;

        public DocumentEmbeddingRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<DocumentEmbedding> AddAsync(DocumentEmbedding entity)
        {
            await _context.DocumentEmbeddings.AddAsync(entity);
            await _context.SaveChangesAsync();
            return entity;
        }

        public async Task<List<DocumentEmbedding>> AddBulkAsync(List<DocumentEmbedding> entities)
        {
            await _context.DocumentEmbeddings.AddRangeAsync(entities);
            await _context.SaveChangesAsync();
            return entities;
        }

        /// <summary>
        /// Restricts a query to one embedding scope so corpora can't cross-match.
        ///
        /// The main knowledge base is defined by EXCLUSION: uploaded documents carry a
        /// caller-supplied EmbeddingType (a document category) and older rows are NULL, so a
        /// positive match on a single value would wrongly drop them. Instead every row is
        /// included except those owned by a dedicated corpus (EmbeddingTypes.IsolatedCorpora).
        /// A dedicated corpus is matched positively.
        ///
        /// The NULL check is explicit on purpose: in SQL `EmbeddingType &lt;&gt; 'x'` evaluates to
        /// NULL for NULL rows, which would silently discard every legacy row.
        /// </summary>
        private IQueryable<DocumentEmbedding> ScopedQuery(string embeddingType)
        {
            return embeddingType == EmbeddingTypes.CloverKnowledgeBase
                ? _context.DocumentEmbeddings.Where(
                    d => d.EmbeddingType == null || !EmbeddingTypes.IsolatedCorpora.Contains(d.EmbeddingType))
                : _context.DocumentEmbeddings.Where(d => d.EmbeddingType == embeddingType);
        }

        public async Task<List<(DocumentEmbedding Document, double Distance)>> SearchSimilarAsync(
            Vector queryEmbedding, int topK, string embeddingType = EmbeddingTypes.CloverKnowledgeBase)
        {
            var results = await ScopedQuery(embeddingType)
                .Select(d => new
                {
                    Document = d,
                    Distance = d.Embedding.CosineDistance(queryEmbedding)
                })
                .OrderBy(x => x.Distance)
                .Take(topK)
                .ToListAsync();

            return results.Select(r => (r.Document, r.Distance)).ToList();
        }

        public async Task<DocumentEmbedding?> GetByIdAsync(int id)
        {
            return await _context.DocumentEmbeddings.FindAsync(id);
        }

        public async Task<List<DocumentEmbedding>> GetByProcessIdAsync(
            int processId, string embeddingType = EmbeddingTypes.CloverKnowledgeBase)
        {
            return await ScopedQuery(embeddingType)
                .Where(e => e.ProcessId == processId)
                .OrderBy(e => e.ChunkIndex)
                .ToListAsync();
        }

        public async Task<List<DocumentEmbedding>> GetAllAsync(
            string embeddingType = EmbeddingTypes.CloverKnowledgeBase)
        {
            return await ScopedQuery(embeddingType)
                .OrderByDescending(e => e.CreatedAt)
                .ToListAsync();
        }

        public async Task<bool> DeleteAsync(int id)
        {
            var entity = await _context.DocumentEmbeddings.FindAsync(id);
            if (entity == null)
                return false;

            _context.DocumentEmbeddings.Remove(entity);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> DeleteByProcessIdAsync(
            int processId, string embeddingType = EmbeddingTypes.CloverKnowledgeBase)
        {
            // Scoped so a document-management delete can never remove another corpus's rows
            // (the coach corpus is stored with ProcessId = 0).
            var entities = await ScopedQuery(embeddingType)
                .Where(e => e.ProcessId == processId)
                .ToListAsync();

            if (!entities.Any())
                return false;

            _context.DocumentEmbeddings.RemoveRange(entities);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<int> GetNextProcessIdAsync()
        {
            var maxProcessId = await _context.DocumentEmbeddings
                .MaxAsync(e => (int?)e.ProcessId) ?? 0;
            return maxProcessId + 1;
        }

        // PromptConfiguration CRUD Implementation
        public async Task<PromptConfiguration> AddPromptAsync(PromptConfiguration entity)
        {
            await _context.PromptConfigurations.AddAsync(entity);
            await _context.SaveChangesAsync();
            return entity;
        }

        public async Task<PromptConfiguration?> GetPromptByIdAsync(int id)
        {
            return await _context.PromptConfigurations
                .Where(p => p.Id == id && !p.IsDel)
                .FirstOrDefaultAsync();
        }

        public async Task<List<PromptConfiguration>> GetAllPromptsAsync()
        {
            return await _context.PromptConfigurations
                .Where(p => !p.IsDel)
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();
        }

        public async Task<bool> UpdatePromptAsync(PromptConfiguration entity)
        {
            var existing = await _context.PromptConfigurations.FindAsync(entity.Id);
            if (existing == null || existing.IsDel)
                return false;

            existing.Text = entity.Text;
            existing.IsDefault = entity.IsDefault;
            existing.ModifiedAt = DateTime.UtcNow;
            existing.ModifiedBy = entity.ModifiedBy;

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> SoftDeletePromptAsync(int id)
        {
            var entity = await _context.PromptConfigurations.FindAsync(id);
            if (entity == null || entity.IsDel)
                return false;

            entity.IsDel = true;
            entity.ModifiedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<PromptConfiguration?> GetDefaultPromptAsync()
        {
            return await _context.PromptConfigurations
                .Where(p => p.IsDefault && !p.IsDel)
                .OrderByDescending(p => p.CreatedAt)
                .FirstOrDefaultAsync();
        }

        // ModelConfiguration CRUD Implementation
        public async Task<ModelConfiguration> AddModelAsync(ModelConfiguration entity)
        {
            await _context.ModelConfigurations.AddAsync(entity);
            await _context.SaveChangesAsync();
            return entity;
        }

        public async Task<ModelConfiguration?> GetModelByIdAsync(int id)
        {
            return await _context.ModelConfigurations
                .Where(m => m.Id == id && !m.IsDel)
                .FirstOrDefaultAsync();
        }

        public async Task<List<ModelConfiguration>> GetAllModelsAsync(int? modelConfigTypeId = null)
        {
            var query = _context.ModelConfigurations.Where(m => !m.IsDel);

            if (modelConfigTypeId.HasValue)
                query = query.Where(m => m.ModelConfigTypeId == modelConfigTypeId.Value);

            return await query.OrderByDescending(m => m.CreatedAt).ToListAsync();
        }

        public async Task<bool> UpdateModelAsync(ModelConfiguration entity)
        {
            var existing = await _context.ModelConfigurations.FindAsync(entity.Id);
            if (existing == null || existing.IsDel)
                return false;

            existing.ModelName = entity.ModelName;
            existing.FriendlyName = entity.FriendlyName;
            existing.ApiKey = entity.ApiKey;
            existing.Endpoint = entity.Endpoint;
            existing.ApiVersion = entity.ApiVersion;
            existing.MaxTokens = entity.MaxTokens;
            existing.Temperature = entity.Temperature;
            existing.IsDefault = entity.IsDefault;
            existing.ModelConfigTypeId = entity.ModelConfigTypeId;
            existing.ModifiedAt = DateTime.UtcNow;
            existing.ModifiedBy = entity.ModifiedBy;

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task ClearDefaultByTypeAsync(int modelConfigTypeId, int? excludeId = null)
        {
            var others = await _context.ModelConfigurations
                .Where(m => m.ModelConfigTypeId == modelConfigTypeId && m.IsDefault && !m.IsDel)
                .Where(m => !excludeId.HasValue || m.Id != excludeId.Value)
                .ToListAsync();

            foreach (var m in others)
            {
                m.IsDefault = false;
                m.ModifiedAt = DateTime.UtcNow;
            }

            if (others.Any())
                await _context.SaveChangesAsync();
        }

        public async Task UpdateModelPricingAsync(int modelId, decimal inputCostPer1k, decimal outputCostPer1k)
        {
            var model = await _context.ModelConfigurations.FindAsync(modelId);
            if (model == null || model.IsDel) return;

            model.InputCostPer1k = inputCostPer1k;
            model.OutputCostPer1k = outputCostPer1k;
            model.ModifiedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        public async Task<bool> SoftDeleteModelAsync(int id)
        {
            var entity = await _context.ModelConfigurations.FindAsync(id);
            if (entity == null || entity.IsDel)
                return false;

            entity.IsDel = true;
            entity.ModifiedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<ModelConfiguration?> GetDefaultModelAsync(int? modelConfigTypeId = null)
        {
            var query = _context.ModelConfigurations
                .Where(m => m.IsDefault && !m.IsDel);

            if (modelConfigTypeId.HasValue)
                query = query.Where(m => m.ModelConfigTypeId == modelConfigTypeId.Value);

            return await query
                .OrderByDescending(m => m.CreatedAt)
                .FirstOrDefaultAsync();
        }

        public async Task<ModelConfiguration?> GetModelByNameAsync(string modelName)
        {
            return await _context.ModelConfigurations
                .Where(m => m.ModelName == modelName && !m.IsDel)
                .OrderByDescending(m => m.CreatedAt)
                .FirstOrDefaultAsync();
        }

        public async Task<List<ModelConfiguration>> GetModelsByIdsAsync(List<int> ids)
        {
            return await _context.ModelConfigurations
                .Where(m => ids.Contains(m.Id) && !m.IsDel)
                .ToListAsync();
        }
    }
}
