using CloverBackoffice.Infrastructure.BusinessModel;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Components.Helpers;
using Microsoft.Extensions.Options;


namespace CloverBackoffice.Services.Implementation
{
    public class QuestionService : IQuestionService
    {
        // The method must match the METHOD line inside the invite or clients ignore it.
        private const string CalendarInviteContentType = "text/calendar; method=REQUEST; charset=utf-8";

        // Editable-event mail: the .ics attachment is a plain METHOD:PUBLISH event.
        private const string EditableEventContentType = "text/calendar; method=PUBLISH; charset=utf-8";

        private const string DailyQuestionTrack = "individual";
        private const string TeamQuestionTrack = "team";

        private readonly IQuestionRepository _questionRepository;
        private readonly IEngineCheckInService _engineCheckIn;
        private readonly SendGridSettings _sendGridSettings;
        private readonly DailyQuestionInviteEmailSettings _inviteEmailSettings;

        public QuestionService(
            IQuestionRepository questionRepository,
            IEngineCheckInService engineCheckIn,
            IOptions<SendGridSettings> sendGridSettings,
            IOptions<DailyQuestionInviteEmailSettings> inviteEmailSettings)
        {
            _questionRepository = questionRepository;
            _engineCheckIn = engineCheckIn;
            _sendGridSettings = sendGridSettings.Value;
            _inviteEmailSettings = inviteEmailSettings.Value;
        }

        public async Task<bool> AddDailyQuestionAsync(CreateDailyQuestionDto dto)
        {
            return await _questionRepository.AddDailyQuestionAsync(dto);
        }
        public async Task<CustomQuestionResponseDto?> AddCustomQuestionAsync(CustomAddQuestionDto dto)
        {
            return await _questionRepository.AddCustomQuestionAsync(dto);
        }

        public async Task<QuestionInfoResponseDto?> GetQuestionInfoByIdAsync(long questionId)
        {
            return await _questionRepository.GetQuestionInfoByIdAsync(questionId);
        }

        public async Task<UpdateQuestionInfoResultDto> UpdateQuestionInfoByIdAsync(UpdateQuestionInfoDto dto)
        {
            return await _questionRepository.UpdateQuestionInfoByIdAsync(dto);
        }

        public async Task<ImportQuestionResponseDto> ImportQuestionsAsync(IFormFile file)
        {
            return await _questionRepository.ImportQuestionsAsync(file);
        }

        public async Task<DeleteByRangeResultDto> DeleteQuestionsByRangeAsync(long fromId, long toId)
        {
            return await _questionRepository.DeleteQuestionsByRangeAsync(fromId, toId);
        }

        public async Task<DeleteByRangeResultDto> DeleteReasonsByRangeAsync(long fromId, long toId)
        {
            return await _questionRepository.DeleteReasonsByRangeAsync(fromId, toId);
        }

        public async Task<UpdateQuestionsFromExcelResponseDto> UpdateQuestionsFromExcelAsync(IFormFile file)
        {
            return await _questionRepository.UpdateQuestionsFromExcelAsync(file);
        }

        public async Task<DailyQuestionFetchResponseDto?> GetDailyQuestionAsync(uint businessUnitId)
        {
            return await _questionRepository.GetDailyQuestionAsync(businessUnitId);
        }

        public async Task<DailyQuestionCalendarEventDto?> GetDailyQuestionCalendarEventAsync(uint? profileId, uint? unitId)
        {
            var team = await _questionRepository.GetDailyQuestionCalendarTeamAsync(profileId, unitId);

            if (team == null)
            {
                return null;
            }

            var day = await _engineCheckIn.TeamDayAsync(team.UnitId);
            var dailyQuestion = day?.Questions.FirstOrDefault(q => q.Track == DailyQuestionTrack);

            if (dailyQuestion == null)
            {
                return null;
            }

            var eventStart = day!.Date.ToDateTime(TimeOnly.MinValue).Add(DataConstants.DailyQuestionEventLocalStartTime);

            return new DailyQuestionCalendarEventDto
            {
                TeamCode = team.TeamCode,
                QuestionKey = dailyQuestion.QuestionKey,
                QuestionText = dailyQuestion.Text,
                TeamQuestionText = day.Questions.FirstOrDefault(q => q.Track == TeamQuestionTrack)?.Text ?? string.Empty,
                QuestionLink = $"{_sendGridSettings.FeedbackAppBaseUrl?.TrimEnd('/')}/{team.TeamCode}",
                EventStartLocal = eventStart,
                EventEndLocal = eventStart.Add(DataConstants.DailyQuestionEventDuration)
            };
        }

        public async Task<DailyQuestionCalendarInviteEmailResultDto?> SendDailyQuestionCalendarInviteEmailAsync(
            SendDailyQuestionCalendarInviteDto dto)
        {
            // The event is built by the same call the file endpoint makes, so the invite mailed
            // out and the invite downloaded describe the same event.
            var calendarEvent = await GetDailyQuestionCalendarEventAsync(dto.ProfileId, dto.UnitId);

            if (calendarEvent == null)
            {
                return null;
            }

            calendarEvent.ReminderMinutes = DailyQuestionCalendarBuilder.ResolveReminder(dto.ReminderMinutes);

            var recipients = (dto.Emails ?? new List<string>())
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Select(e => e.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return await SendCalendarInviteAsync(calendarEvent, recipients, attachFile: false);
        }

        public async Task<DailyQuestionCalendarInviteEmailResultDto?> SendTeamCalendarInviteEmailAsync(
            uint teamId,
            string teamName,
            string email,
            int? reminderMinutes)
        {
            var calendarEvent = await GetDailyQuestionCalendarEventAsync(null, teamId);

            if (calendarEvent == null)
            {
                return null;
            }

            calendarEvent.TeamName = teamName;
            // The calendar event keeps the standard title; only the mail subject names the team.
            calendarEvent.EmailSubject = $"{DataConstants.DailyCheckInEventTitle}: {teamName}";
            calendarEvent.ReminderMinutes = DailyQuestionCalendarBuilder.ResolveReminder(reminderMinutes);

            return await SendCalendarInviteAsync(calendarEvent, new List<string> { email }, attachFile: true);
        }

        private async Task<DailyQuestionCalendarInviteEmailResultDto> SendCalendarInviteAsync(
            DailyQuestionCalendarEventDto calendarEvent,
            List<string> recipients,
            bool attachFile)
        {
            var eventLink = DailyQuestionCalendarBuilder.BuildEventLink(calendarEvent);

            var result = new DailyQuestionCalendarInviteEmailResultDto
            {
                Recipients = recipients,
                // "RRIDA Daily Check-in", or the subject set for the mail; never the daily question.
                Subject = calendarEvent.Subject,
                EventLink = eventLink,
                EventStart = calendarEvent.EventStartLocal,
                EventEnd = calendarEvent.EventEndLocal,
                TeamCode = calendarEvent.TeamCode,
                QuestionLink = calendarEvent.QuestionLink,
                ReminderMinutes = calendarEvent.ReminderMinutes
            };

            if (recipients.Count == 0)
            {
                return result;
            }

            // Default: the editable event (separate code path below). Setting
            // DailyQuestion:InviteEmail:SendAsEditableEvent = false goes back to the meeting
            // request that follows, unchanged.
            if (_inviteEmailSettings.SendAsEditableEvent)
            {
                result.EmailSent = await SendEditableEventEmailAsync(calendarEvent, recipients, eventLink);
                result.CalendarInviteSent = result.EmailSent;

                return result;
            }

            var stampUtc = DateTime.UtcNow;

            // The event travels as an invitation (METHOD:REQUEST) addressed to the recipients,
            // so the mail client shows it with its accept / decline controls.
            var invite = new EmailCalendarInvite
            {
                Calendar = DailyQuestionCalendarBuilder.BuildInvite(
                    calendarEvent,
                    stampUtc,
                    _sendGridSettings.FromEmail,
                    _sendGridSettings.FromName,
                    recipients),
                ContentType = CalendarInviteContentType,
                AttachFile = attachFile
            };

            if (attachFile)
            {
                // The downloadable file is the plain editable event (METHOD:PUBLISH, its own
                // UID), not a copy of the invitation: an imported invitation is organizer-owned,
                // so its date and time would be locked.
                invite.AttachmentCalendar = DailyQuestionCalendarBuilder.Build(calendarEvent, stampUtc);
                invite.AttachmentFileName = $"rrida-check-in-{calendarEvent.TeamCode}.ics";
            }

            result.EmailSent = await EmailHelper.SendRawHtmlEmailAsync(
                _sendGridSettings,
                recipients,
                calendarEvent.Subject,
                BuildInviteEmailBody(calendarEvent, eventLink),
                invite);

            result.CalendarInviteSent = result.EmailSent;

            return result;
        }

        /// <summary>
        /// Sends the check-in as an event the recipient owns rather than a meeting request.
        /// The mail carries only the plain event the download endpoint returns (METHOD:PUBLISH,
        /// no ORGANIZER / ATTENDEE) as a .ics attachment. Outlook offers "Add to calendar" for
        /// it and Google adds it from the attachment or the event link in the body. Once added,
        /// date, time and reminder are editable in both, which a meeting request does not allow
        /// in Outlook. Same body, subject and event as the meeting request path.
        /// </summary>
        private Task<bool> SendEditableEventEmailAsync(
            DailyQuestionCalendarEventDto calendarEvent,
            List<string> recipients,
            string eventLink)
        {
            return EmailHelper.SendHtmlEmailWithCalendarAttachmentAsync(
                _sendGridSettings,
                recipients,
                calendarEvent.Subject,
                BuildInviteEmailBody(calendarEvent, eventLink),
                DailyQuestionCalendarBuilder.Build(calendarEvent, DateTime.UtcNow),
                $"rrida-check-in-{calendarEvent.TeamCode}.ics",
                EditableEventContentType);
        }

        /// <summary>
        /// The mail body: the text held in app settings with the team's code and feedback link
        /// merged in, followed by the link to the event that was just built.
        /// </summary>
        private string BuildInviteEmailBody(DailyQuestionCalendarEventDto calendarEvent, string eventLink)
        {
            var label = string.IsNullOrWhiteSpace(_inviteEmailSettings.EventLinkLabel)
                ? "Open the calendar event"
                : _inviteEmailSettings.EventLinkLabel;

            var body = new StringBuilder();

            body.Append(_inviteEmailSettings.Body
                .Replace("{{TeamCode}}", calendarEvent.TeamCode)
                .Replace("{{FeedbackLink}}", calendarEvent.QuestionLink));
            body.Append("<p><a href=\"").Append(eventLink).Append("\">").Append(label).Append("</a></p>");

            return body.ToString();
        }
    }

}
