using CloverBackoffice.Infrastructure.DTO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Repository.Interfaces
{
    public interface IQuestionRepository
    {
        Task<bool> AddDailyQuestionAsync(CreateDailyQuestionDto dto);
        Task<CustomQuestionResponseDto?> AddCustomQuestionAsync(CustomAddQuestionDto dto);
        Task<QuestionInfoResponseDto?> GetQuestionInfoByIdAsync(long questionId);
        Task<UpdateQuestionInfoResultDto> UpdateQuestionInfoByIdAsync(UpdateQuestionInfoDto dto);
        Task<ImportQuestionResponseDto> ImportQuestionsAsync(Microsoft.AspNetCore.Http.IFormFile file);
        Task<DeleteByRangeResultDto> DeleteQuestionsByRangeAsync(long fromId, long toId);
        Task<DeleteByRangeResultDto> DeleteReasonsByRangeAsync(long fromId, long toId);
        Task<UpdateQuestionsFromExcelResponseDto> UpdateQuestionsFromExcelAsync(Microsoft.AspNetCore.Http.IFormFile file);

        /// <summary>
        /// Gets today's daily question for a business unit using the same rotation the mobile app uses.
        /// Returns null when the business unit is unknown or no question is scheduled.
        /// </summary>
        Task<DailyQuestionFetchResponseDto?> GetDailyQuestionAsync(uint businessUnitId);

        /// <summary>
        /// Resolves the team a calendar invite is built for: the unit, or the company's first
        /// team when only a company is supplied. Null when neither is supplied or nothing matches.
        /// </summary>
        Task<DailyQuestionCalendarTeamDto?> GetDailyQuestionCalendarTeamAsync(uint? profileId, uint? unitId);

    }
}
