using CloverBackoffice.Infrastructure.DTO;
using System;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Interfaces
{
    /// <summary>
    /// Custom Questions — questions a company authors for one of its business units,
    /// and the free-text answers given to them.
    /// </summary>
    public interface ICustomQuestionService
    {
        Task<CreateCustomQuestionResultDto> AddCustomQuestionAsync(CreateCustomQuestionDto dto, int? createdBy);

        Task<CreateCustomQuestionFeedbackResultDto> AddCustomQuestionFeedbackAsync(
            CreateCustomQuestionFeedbackDto dto,
            int? createdBy);

        /// <summary>
        /// The custom questions due for a business unit today. The unit is named by id or
        /// by short code; the day is decided by the backend, in UTC.
        /// </summary>
        Task<ScheduledCustomQuestionsResultDto> GetScheduledCustomQuestionsAsync(
            long? unitId,
            string? unitShortCode);

        /// <summary>
        /// One pass of the expiry sweep: live questions whose validity period has run out
        /// are moved to Expired. Driven by CustomQuestionExpiryBackgroundService.
        /// </summary>
        Task<ExpireCustomQuestionsResultDto> ExpireDueCustomQuestionsAsync(int? updatedBy = null);
    }
}
