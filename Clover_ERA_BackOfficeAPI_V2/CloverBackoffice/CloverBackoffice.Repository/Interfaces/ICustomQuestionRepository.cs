using CloverBackoffice.Infrastructure.DTO;
using System;
using System.Threading.Tasks;

namespace CloverBackoffice.Repository.Interfaces
{
    /// <summary>
    /// Custom Questions — questions a company authors for one of its business units,
    /// and the free-text answers given to them.
    /// </summary>
    public interface ICustomQuestionRepository
    {
        /// <summary>
        /// Stores one custom question and its audit snapshot. A request naming a company
        /// or unit that does not exist, or a unit belonging to another company, comes back
        /// as a failed result carrying the reason, not an exception.
        /// </summary>
        Task<CreateCustomQuestionResultDto> AddCustomQuestionAsync(CreateCustomQuestionDto dto, int? createdBy);

        /// <summary>Stores one free-text answer to a custom question, with its audit snapshot.</summary>
        Task<CreateCustomQuestionFeedbackResultDto> AddCustomQuestionFeedbackAsync(
            CreateCustomQuestionFeedbackDto dto,
            int? createdBy);

        /// <summary>
        /// The custom questions a business unit should be asked today.
        ///
        /// The unit is named by id or by short code — either resolves the same unit, and
        /// passing both requires them to agree. The day is decided here, in UTC, not by the
        /// caller. Questions come back only when they are scheduled inside today, are still
        /// live (Scheduled or Active, not deleted, not cancelled and not already expired),
        /// and their validity period has not run out.
        /// </summary>
        Task<ScheduledCustomQuestionsResultDto> GetScheduledCustomQuestionsAsync(
            long? unitId,
            string? unitShortCode);

        /// <summary>
        /// Moves every live custom question whose validity period has run out to Expired,
        /// writing an audit snapshot for each. Idempotent: a second pass over the same
        /// rows finds nothing left to do.
        /// </summary>
        Task<ExpireCustomQuestionsResultDto> ExpireDueCustomQuestionsAsync(int? updatedBy = null);
    }
}
