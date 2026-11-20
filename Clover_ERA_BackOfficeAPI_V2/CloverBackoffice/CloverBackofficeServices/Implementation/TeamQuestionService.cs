using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Implementation
{
    /// <summary>
    /// Pass-through to <see cref="ITeamQuestionRepository" />, matching how
    /// <see cref="QuestionService" /> sits over the legacy question repository.
    /// </summary>
    public class TeamQuestionService : ITeamQuestionService
    {
        private readonly ITeamQuestionRepository _repository;

        public TeamQuestionService(ITeamQuestionRepository repository)
        {
            _repository = repository;
        }

        public Task<List<TeamQuestionListItemDto>> GetTeamQuestionListAsync(long? categoryId = null) =>
            _repository.GetTeamQuestionListAsync(categoryId);

        public Task<List<TeamQuestionCategoryDto>> GetTeamQuestionCategoriesAsync(bool includeInactive = true) =>
            _repository.GetTeamQuestionCategoriesAsync(includeInactive);

        public Task<TeamQuestionCategoryDto?> GetTeamQuestionCategoryByIdAsync(long categoryId) =>
            _repository.GetTeamQuestionCategoryByIdAsync(categoryId);

        public Task<TeamQuestionCategoryResultDto> AddTeamQuestionCategoryAsync(CreateTeamQuestionCategoryDto dto, int? createdBy) =>
            _repository.AddTeamQuestionCategoryAsync(dto, createdBy);

        public Task<TeamQuestionCategoryResultDto> UpdateTeamQuestionCategoryAsync(UpdateTeamQuestionCategoryDto dto, int? updatedBy) =>
            _repository.UpdateTeamQuestionCategoryAsync(dto, updatedBy);

        public Task<TeamQuestionCategoryResultDto> DeleteTeamQuestionCategoryAsync(long categoryId, int? updatedBy) =>
            _repository.DeleteTeamQuestionCategoryAsync(categoryId, updatedBy);

        public Task<TeamQuestionResponseDto?> AddTeamQuestionAsync(AddTeamQuestionDto dto) =>
            _repository.AddTeamQuestionAsync(dto);

        public Task<TeamQuestionInfoResponseDto?> GetTeamQuestionInfoByIdAsync(long questionId) =>
            _repository.GetTeamQuestionInfoByIdAsync(questionId);

        public Task<UpdateTeamQuestionInfoResultDto> UpdateTeamQuestionInfoByIdAsync(UpdateTeamQuestionInfoDto dto) =>
            _repository.UpdateTeamQuestionInfoByIdAsync(dto);

        public Task<ImportTeamQuestionResponseDto> ImportTeamQuestionsAsync(IFormFile file) =>
            _repository.ImportTeamQuestionsAsync(file);

        public Task<UpdateTeamQuestionsFromExcelResponseDto> UpdateTeamQuestionsFromExcelAsync(IFormFile file) =>
            _repository.UpdateTeamQuestionsFromExcelAsync(file);

        public Task<TeamQuestionBankReportDto> GetTeamQuestionBankReportAsync() =>
            _repository.GetTeamQuestionBankReportAsync();
    }
}
