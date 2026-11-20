using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Services.Interfaces;
using System;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Implementation
{
    /// <summary>Pass-through to <see cref="ICustomQuestionRepository" />.</summary>
    public class CustomQuestionService : ICustomQuestionService
    {
        private readonly ICustomQuestionRepository _repository;

        public CustomQuestionService(ICustomQuestionRepository repository)
        {
            _repository = repository;
        }

        public Task<CreateCustomQuestionResultDto> AddCustomQuestionAsync(CreateCustomQuestionDto dto, int? createdBy) =>
            _repository.AddCustomQuestionAsync(dto, createdBy);

        public Task<CreateCustomQuestionFeedbackResultDto> AddCustomQuestionFeedbackAsync(
            CreateCustomQuestionFeedbackDto dto,
            int? createdBy) =>
            _repository.AddCustomQuestionFeedbackAsync(dto, createdBy);

        public Task<ScheduledCustomQuestionsResultDto> GetScheduledCustomQuestionsAsync(
            long? unitId,
            string? unitShortCode) =>
            _repository.GetScheduledCustomQuestionsAsync(unitId, unitShortCode);

        public Task<ExpireCustomQuestionsResultDto> ExpireDueCustomQuestionsAsync(int? updatedBy = null) =>
            _repository.ExpireDueCustomQuestionsAsync(updatedBy);
    }
}
