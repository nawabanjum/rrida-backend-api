using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.BusinessModel;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;

namespace CloverBackoffice.API.Controllers
{
    /// <summary>
    /// Publishes a team's daily check-in as a calendar invite. The daily and team questions
    /// are the ones the engine asks that team, as api/v1/engine/today returns them.
    /// </summary>
    [Route("api/daily-question")]
    [ApiController]
    [Authorize]
    public class DailyQuestionController : ControllerBase
    {
        private const string CalendarContentType = "text/calendar";

        private readonly IQuestionService _questionService;

        public DailyQuestionController(IQuestionService questionService)
        {
            _questionService = questionService;
        }

        /// <summary>
        /// Returns the team's daily question calendar invite, scheduled at 5:00 PM on today, or
        /// on the team's next asked day when it is not asked today.
        /// <paramref name="unitId" /> or <paramref name="profileId" /> is required, because
        /// questions rotate per team; a company on its own uses its first team.
        /// The shape of the response follows <paramref name="type" />: <c>true</c> returns the
        /// .ics file download, <c>false</c> returns the event details with the calendar content
        /// as text. The file is a plain calendar event (not a meeting invitation), so after
        /// import its date, time and reminder can be edited; its UID is stable per team and
        /// differs from the emailed invitation's, so the two never merge.
        /// The event is the standard recurring "RRIDA Daily Check-in": the title and description
        /// carry no daily or team question (only the team, its code and the static feedback
        /// link), availability is Free and the reminder defaults to 0 minutes.
        /// </summary>
        /// <param name="profileId">Company filter.</param>
        /// <param name="unitId">Business unit (team) filter.</param>
        /// <param name="type">True for the file download, false (default) for the text response.</param>
        /// <param name="reminderMinutes">Optional reminder in minutes before the event (0–10080);
        /// 0 fires at the start time. Omitted = 0 minutes (at the start time).</param>
        [HttpGet("calendar-invite")]
        public async Task<IActionResult> GetDailyQuestionCalendarInvite(
            [FromQuery] uint? profileId = null,
            [FromQuery] uint? unitId = null,
            [FromQuery] bool type = false,
            [FromQuery, Range(DataConstants.CalendarReminderMinMinutes, DataConstants.CalendarReminderMaxMinutes,
                ErrorMessage = "reminderMinutes must be between 0 and 10080 (7 days).")] int? reminderMinutes = null)
        {
            try
            {
                if (!profileId.HasValue && !unitId.HasValue)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.CalendarInviteTeamRequired));
                }

                var calendarEvent = await _questionService.GetDailyQuestionCalendarEventAsync(profileId, unitId);

                if (calendarEvent == null)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.NoRecords));
                }

                calendarEvent.ReminderMinutes = DailyQuestionCalendarBuilder.ResolveReminder(reminderMinutes);

                var calendar = DailyQuestionCalendarBuilder.Build(calendarEvent, DateTime.UtcNow);
                var fileName = $"daily-question-{calendarEvent.EventStartLocal:yyyyMMdd}.ics";

                if (type)
                {
                    return File(Encoding.UTF8.GetBytes(calendar), CalendarContentType, fileName);
                }

                var invite = new DailyQuestionCalendarInviteDto
                {
                    FileName = fileName,
                    ContentType = CalendarContentType,
                    // The standard "RRIDA Daily Check-in" title, never the daily question.
                    Subject = calendarEvent.Title,
                    // Reuses the calendar builder so this matches the .ics body exactly.
                    Description = DailyQuestionCalendarBuilder.BuildDescription(calendarEvent),
                    EventTime = calendarEvent.EventStartLocal.ToString("hh:mm tt", CultureInfo.InvariantCulture),
                    EventDate = calendarEvent.EventStartLocal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    EventStart = calendarEvent.EventStartLocal,
                    EventEnd = calendarEvent.EventEndLocal,
                    TeamCode = calendarEvent.TeamCode,
                    QuestionLink = calendarEvent.QuestionLink,
                    ReminderMinutes = calendarEvent.ReminderMinutes,
                    CalendarContent = calendar
                };

                return Ok(ApiResponseHandler.Success(invite, ResponseMessages.Success.RecordFound));
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        /// <summary>
        /// Mails the team's daily question calendar invite to the supplied addresses instead of
        /// returning it as a file. The event is built with the same logic the file endpoint
        /// uses; "RRIDA Daily Check-in" is the mail subject, the body is the text held in
        /// app settings (<c>DailyQuestion:InviteEmail:Body</c>) followed by a link to the event
        /// just created. By default (<c>DailyQuestion:InviteEmail:SendAsEditableEvent</c> = true)
        /// the event is attached as a .ics file (METHOD:PUBLISH, no organizer) the recipient
        /// adds to their calendar. They own it, so its date and time stay editable in Outlook.
        /// With the setting false it is carried as a calendar invitation (METHOD:REQUEST, with
        /// accept / decline) — no file is sent. <c>unitId</c> or <c>profileId</c> is required,
        /// as on the file endpoint. Optional <c>reminderMinutes</c> (0–10080, 0 = at the start
        /// time); omitted = 0 minutes. The mail subject is "RRIDA Daily Check-in".
        /// </summary>
        [HttpPost("calendar-invite/email")]
        public async Task<IActionResult> SendDailyQuestionCalendarInvite(
            [FromBody] SendDailyQuestionCalendarInviteDto request)
        {
            try
            {
                if (request != null && !request.ProfileId.HasValue && !request.UnitId.HasValue)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.CalendarInviteTeamRequired));
                }

                // [Required] rejects a missing / empty list before this runs; the guard also
                // covers a list that holds nothing but blanks, which the attribute allows.
                if (request == null || request.Emails == null || !request.Emails.Any(e => !string.IsNullOrWhiteSpace(e)))
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.RecipientEmailRequired));
                }

                // Same format check ValidateDomainV2 uses. The whole request is rejected rather
                // than the bad addresses dropped, so a typo is never silently skipped.
                var invalidEmails = request.Emails
                    .Where(e => !string.IsNullOrWhiteSpace(e))
                    .Select(e => e.Trim())
                    .Where(e => !new EmailAddressAttribute().IsValid(e))
                    .ToList();

                if (invalidEmails.Count > 0)
                {
                    return Ok(ApiResponseHandler.Fail(invalidEmails, ResponseMessages.Errors.InvalidEmail));
                }

                var result = await _questionService.SendDailyQuestionCalendarInviteEmailAsync(request);

                if (result == null)
                {
                    return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.NoRecords));
                }

                if (!result.EmailSent)
                {
                    return Ok(ApiResponseHandler.Fail(result, ResponseMessages.Errors.Failedemail));
                }

                return Ok(ApiResponseHandler.Success(result, ResponseMessages.Success.DailyQuestionInviteSent));
            }
            catch (Exception)
            {
                return StatusCode(500, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }
    }
}
