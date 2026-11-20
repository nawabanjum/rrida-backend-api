using CloverBackoffice.Infrastructure.DTO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Interfaces
{
    public interface IQuestionService
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
        /// Builds the team's calendar event from the engine's questions for its current asked
        /// day: today, or the next asked day when the team is not asked today. Returns null
        /// when no team was requested or matched, or the engine is off for the team.
        /// </summary>
        Task<DailyQuestionCalendarEventDto?> GetDailyQuestionCalendarEventAsync(uint? profileId, uint? unitId);

        /// <summary>
        /// Builds the calendar event exactly as the file endpoint does, then mails it to the
        /// supplied addresses: "RRIDA Daily Check-in" is the subject, the body is the text held in
        /// app settings plus a link to the event just built, and the .ics is attached. Returns
        /// null when no event could be built for the requested scope.
        /// </summary>
        Task<DailyQuestionCalendarInviteEmailResultDto?> SendDailyQuestionCalendarInviteEmailAsync(
            SendDailyQuestionCalendarInviteDto dto);

        /// <summary>
        /// Mails a Team Manager the calendar invite for their team: the same standard
        /// "RRIDA Daily Check-in" event as above (the team is named in the description and the
        /// mail subject). The mail carries the invitation (accept / decline) plus a separate
        /// editable .ics file. <paramref name="reminderMinutes" /> sets the reminder
        /// (0 = at the start); null = 0 minutes. Who may ask is the
        /// caller's check. Returns null when no event could be built for the team.
        /// </summary>
        Task<DailyQuestionCalendarInviteEmailResultDto?> SendTeamCalendarInviteEmailAsync(
            uint teamId,
            string teamName,
            string email,
            int? reminderMinutes);
    }
}
