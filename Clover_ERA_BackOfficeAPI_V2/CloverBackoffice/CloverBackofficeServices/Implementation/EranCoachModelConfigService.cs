using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Services.Interfaces;

namespace CloverBackoffice.Services.Implementation
{
    public class EranCoachModelConfigService : IEranCoachModelConfigService
    {
        private readonly IEranCoachModelConfigRepository _repo;

        public EranCoachModelConfigService(IEranCoachModelConfigRepository repo)
        {
            _repo = repo;
        }

        public async Task<EranCoachModelConfigDto?> GetActiveAsync(CancellationToken ct = default)
        {
            var model = await _repo.GetActiveAsync(ct);
            return model == null ? null : ToConfigDto(model);
        }

        public async Task<List<EranCoachModelOptionDto>> GetModelsAsync(CancellationToken ct = default)
        {
            var models = await _repo.GetSelectableAsync(ct);
            return models.Select(ToOptionDto).ToList();
        }

        public async Task<EranCoachModelConfigDto?> SetActiveAsync(UpdateEranCoachModelConfigDto request, string? modifiedBy, CancellationToken ct = default)
        {
            var model = await _repo.SetActiveAsync(
                request.ModelConfigurationId,
                request.Temperature,
                request.TopP,
                request.MaxTokens,
                modifiedBy,
                ct);

            return model == null ? null : ToConfigDto(model);
        }

        private static EranCoachModelConfigDto ToConfigDto(ModelConfiguration m) => new()
        {
            Id = m.Id,
            ModelName = m.ModelName,
            FriendlyName = m.FriendlyName,
            Provider = m.Provider,
            Temperature = m.Temperature,
            TopP = m.TopP,
            MaxTokens = m.MaxTokens,
            IsSelected = m.IsSelected,
            IsDefault = m.IsDefault
        };

        private static EranCoachModelOptionDto ToOptionDto(ModelConfiguration m) => new()
        {
            Id = m.Id,
            ModelName = m.ModelName,
            FriendlyName = m.FriendlyName,
            Provider = m.Provider,
            IsSelected = m.IsSelected,
            IsDefault = m.IsDefault
        };
    }
}
