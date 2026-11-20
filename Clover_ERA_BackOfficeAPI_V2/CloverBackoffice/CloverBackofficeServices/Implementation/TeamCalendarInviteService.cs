using System.ComponentModel.DataAnnotations;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.DTO.TeamHealth;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace CloverBackoffice.Services.Implementation
{
    // "Email this to me" on the Invite Your Team modal. The recipient comes from the token's
    // user and never from the request; a user's Username is their registered email address.
    //
    // The event and the mail are QuestionService's, so this invite and the one from
    // api/daily-question/calendar-invite cannot drift apart.
    public class TeamCalendarInviteService : ITeamCalendarInviteService
    {
        // A double click or a client retry must not mail twice. Held per app instance, which
        // is enough for that: nothing here needs to outlive a minute.
        private static readonly TimeSpan DuplicateWindow = TimeSpan.FromSeconds(60);

        private static readonly object ClaimLock = new();

        private readonly ApplicationDbContext _context;
        private readonly ITeamHealthAccessService _access;
        private readonly IQuestionService _questions;
        private readonly IMemoryCache _cache;

        public TeamCalendarInviteService(
            ApplicationDbContext context,
            ITeamHealthAccessService access,
            IQuestionService questions,
            IMemoryCache cache)
        {
            _context = context;
            _access = access;
            _questions = questions;
            _cache = cache;
        }

        public async Task<TeamMilestoneResult<TeamCalendarInviteResultDto>> SendAsync(
            uint teamId, int actorUserId, int? reminderMinutes = null, CancellationToken ct = default)
        {
            if (teamId == 0)
                return Fail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.TimelineTeamIdInvalid);

            // 0 is valid (a reminder at the start time); only out-of-range values are refused.
            if (!DailyQuestionCalendarBuilder.IsValidReminder(reminderMinutes))
                return Fail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.CalendarReminderMinutesInvalid);

            // None asked for = the standard 0 minutes, so "omitted" and "0" are one request.
            int? reminder = DailyQuestionCalendarBuilder.ResolveReminder(reminderMinutes);

            var team = await _context.BusinessUnits
                .AsNoTracking()
                .Where(u => u.Id == teamId && u.IsDel != true)
                .Select(u => new { u.Name, u.ProfileId })
                .FirstOrDefaultAsync(ct);
            if (team == null)
                return Fail(TeamMilestoneError.NotFound, ResponseMessages.Errors.MilestoneTeamNotFound);

            if (!await _access.ManagesTeamAsync(teamId, team.ProfileId, actorUserId, ct))
                return Fail(TeamMilestoneError.Forbidden, ResponseMessages.Errors.TeamCalendarInviteForbidden);

            var email = (await _context.Users
                .AsNoTracking()
                .Where(u => u.UserId == actorUserId && !u.IsDel)
                .Select(u => u.Username)
                .FirstOrDefaultAsync(ct))?.Trim();
            if (string.IsNullOrEmpty(email) || !new EmailAddressAttribute().IsValid(email))
                return Fail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.TeamCalendarInviteEmailInvalid);

            var result = new TeamCalendarInviteResultDto { TeamId = teamId, TeamName = team.Name, Email = email };

            // The reminder is part of the claim: a repeat of the same request is swallowed, but
            // asking again with a different reminder sends the updated invite.
            var claim = (actorUserId, teamId, reminder);
            if (!TryClaim(claim))
                return TeamMilestoneResult<TeamCalendarInviteResultDto>.Ok(result);

            DailyQuestionCalendarInviteEmailResultDto? sent = null;
            try
            {
                sent = await _questions.SendTeamCalendarInviteEmailAsync(teamId, team.Name, email, reminder);
            }
            finally
            {
                // Only a delivered mail blocks a repeat; a failed one has to be retryable.
                if (sent?.EmailSent != true)
                    _cache.Remove(claim);
            }

            if (sent == null)
                return Fail(TeamMilestoneError.ServerError, ResponseMessages.Errors.TeamCalendarInviteNotGenerated);

            if (!sent.EmailSent)
                return Fail(TeamMilestoneError.ServerError, ResponseMessages.Errors.TeamCalendarInviteNotDelivered);

            result.InvitationSent = true;
            return TeamMilestoneResult<TeamCalendarInviteResultDto>.Ok(result);
        }

        private bool TryClaim((int UserId, uint TeamId, int? ReminderMinutes) claim)
        {
            lock (ClaimLock)
            {
                if (_cache.TryGetValue(claim, out _))
                    return false;

                _cache.Set(claim, true, DuplicateWindow);
                return true;
            }
        }

        private static TeamMilestoneResult<TeamCalendarInviteResultDto> Fail(TeamMilestoneError error, string message) =>
            TeamMilestoneResult<TeamCalendarInviteResultDto>.Fail(error, message);
    }
}
