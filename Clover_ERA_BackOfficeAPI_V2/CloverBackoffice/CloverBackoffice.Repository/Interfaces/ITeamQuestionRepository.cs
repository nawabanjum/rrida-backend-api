using CloverBackoffice.Infrastructure.DTO;
using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CloverBackoffice.Repository.Interfaces
{
    /// <summary>
    /// The Team Daily Questions bank. Same surface as <see cref="IQuestionRepository" /> for the
    /// legacy CLOVER chain, over the Team tables instead.
    /// </summary>
    public interface ITeamQuestionRepository
    {
        /// <summary>
        /// The question bank. <paramref name="categoryId" /> filters to one category; 0 is a
        /// real filter (the general questions), null means every category.
        /// </summary>
        Task<List<TeamQuestionListItemDto>> GetTeamQuestionListAsync(long? categoryId = null);

        // ── Categories ──────────────────────────────────────────────────────

        Task<List<TeamQuestionCategoryDto>> GetTeamQuestionCategoriesAsync(bool includeInactive = true);

        Task<TeamQuestionCategoryDto?> GetTeamQuestionCategoryByIdAsync(long categoryId);

        Task<TeamQuestionCategoryResultDto> AddTeamQuestionCategoryAsync(CreateTeamQuestionCategoryDto dto, int? createdBy);

        Task<TeamQuestionCategoryResultDto> UpdateTeamQuestionCategoryAsync(UpdateTeamQuestionCategoryDto dto, int? updatedBy);

        /// <summary>Refused while the category still holds questions, and for the default category.</summary>
        Task<TeamQuestionCategoryResultDto> DeleteTeamQuestionCategoryAsync(long categoryId, int? updatedBy);

        /// <summary>Returns null when the rotation slot is already taken by another question.</summary>
        Task<TeamQuestionResponseDto?> AddTeamQuestionAsync(AddTeamQuestionDto dto);

        Task<TeamQuestionInfoResponseDto?> GetTeamQuestionInfoByIdAsync(long questionId);

        Task<UpdateTeamQuestionInfoResultDto> UpdateTeamQuestionInfoByIdAsync(UpdateTeamQuestionInfoDto dto);

        /// <summary>Imports the RRIDA Team Conditions workbook (.xlsx) or its CSV equivalent.</summary>
        Task<ImportTeamQuestionResponseDto> ImportTeamQuestionsAsync(IFormFile file);

        /// <summary>
        /// Rewrites the reasons, micro-actions and Team States of questions that are already in
        /// the bank, matching rows to questions by question text.
        /// </summary>
        Task<UpdateTeamQuestionsFromExcelResponseDto> UpdateTeamQuestionsFromExcelAsync(IFormFile file);

        Task<TeamQuestionBankReportDto> GetTeamQuestionBankReportAsync();
    }
}
