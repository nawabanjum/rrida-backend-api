using CloverBackoffice.Infrastructure.DTO;
using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Interfaces
{
    /// <summary>
    /// Team Daily Questions. Same surface as <see cref="IQuestionService" /> for the legacy
    /// CLOVER chain, over the Team question bank.
    /// </summary>
    public interface ITeamQuestionService
    {
        Task<List<TeamQuestionListItemDto>> GetTeamQuestionListAsync(long? categoryId = null);

        Task<List<TeamQuestionCategoryDto>> GetTeamQuestionCategoriesAsync(bool includeInactive = true);

        Task<TeamQuestionCategoryDto?> GetTeamQuestionCategoryByIdAsync(long categoryId);

        Task<TeamQuestionCategoryResultDto> AddTeamQuestionCategoryAsync(CreateTeamQuestionCategoryDto dto, int? createdBy);

        Task<TeamQuestionCategoryResultDto> UpdateTeamQuestionCategoryAsync(UpdateTeamQuestionCategoryDto dto, int? updatedBy);

        Task<TeamQuestionCategoryResultDto> DeleteTeamQuestionCategoryAsync(long categoryId, int? updatedBy);

        /// <summary>Returns null when a question with the same text already exists.</summary>
        Task<TeamQuestionResponseDto?> AddTeamQuestionAsync(AddTeamQuestionDto dto);

        Task<TeamQuestionInfoResponseDto?> GetTeamQuestionInfoByIdAsync(long questionId);

        Task<UpdateTeamQuestionInfoResultDto> UpdateTeamQuestionInfoByIdAsync(UpdateTeamQuestionInfoDto dto);

        Task<ImportTeamQuestionResponseDto> ImportTeamQuestionsAsync(IFormFile file);

        Task<UpdateTeamQuestionsFromExcelResponseDto> UpdateTeamQuestionsFromExcelAsync(IFormFile file);

        Task<TeamQuestionBankReportDto> GetTeamQuestionBankReportAsync();
    }
}
