using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Repository.Implementation
{
    public class EranCoachModelConfigRepository : IEranCoachModelConfigRepository
    {
        // ModelConfiguration.ModelConfigTypeId for the coach's chat/search models
        // (matches EranCoachChatService.SearchModelTypeId).
        private const int SearchModelTypeId = 1;

        private readonly ApplicationDbContext _context;

        public EranCoachModelConfigRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<List<ModelConfiguration>> GetSelectableAsync(CancellationToken ct = default)
        {
            return await _context.ModelConfigurations
                .Where(m => m.ModelConfigTypeId == SearchModelTypeId && !m.IsDel)
                .OrderByDescending(m => m.CreatedAt)
                .ToListAsync(ct);
        }

        public async Task<ModelConfiguration?> GetActiveAsync(CancellationToken ct = default)
        {
            var models = await GetSelectableAsync(ct);
            return models.FirstOrDefault(m => m.IsSelected)
                   ?? models.FirstOrDefault(m => m.IsDefault)
                   ?? models.FirstOrDefault();
        }

        public async Task<ModelConfiguration?> SetActiveAsync(int modelId, double? temperature, double? topP, int? maxTokens, string? modifiedBy, CancellationToken ct = default)
        {
            var models = await _context.ModelConfigurations
                .Where(m => m.ModelConfigTypeId == SearchModelTypeId && !m.IsDel)
                .ToListAsync(ct);

            var target = models.FirstOrDefault(m => m.Id == modelId);
            if (target == null) return null; // not a selectable coach model

            var now = DateTime.UtcNow;

            // Exactly one Search model carries IsSelected == true.
            foreach (var m in models)
            {
                var shouldSelect = m.Id == modelId;
                if (m.IsSelected != shouldSelect)
                {
                    m.IsSelected = shouldSelect;
                    m.ModifiedAt = now;
                    if (modifiedBy != null) m.ModifiedBy = modifiedBy;
                }
            }

            if (temperature.HasValue) target.Temperature = temperature.Value;
            if (topP.HasValue) target.TopP = topP.Value;
            if (maxTokens.HasValue) target.MaxTokens = maxTokens.Value;
            target.ModifiedAt = now;
            if (modifiedBy != null) target.ModifiedBy = modifiedBy;

            await _context.SaveChangesAsync(ct);
            return target;
        }
    }
}
