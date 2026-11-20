using System.Runtime.CompilerServices;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Infrastructure.Models.DeterministicLayer;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Implementation;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace CloverBackoffice.Tests.Engine
{
    /// <summary>
    /// GET api/daily-question/calendar-invite and POST calendar-invite/email: the invite carries
    /// the questions engine/today asks the team, and links to the RRIDA Feedback app.
    /// </summary>
    public class DailyQuestionCalendarInviteTests
    {
        private const string FeedbackApp = "https://feedback.test";

        private static readonly string EngineRoot = FindEngineRoot();
        private static readonly Lazy<RridaEngineContextFactory> Factory = new(() => new RridaEngineContextFactory(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Rrida:KitDataPath"] = Path.Combine(EngineRoot, "kit", "data"),
                ["Rrida:ProductionRotationPath"] = Path.Combine(EngineRoot, "rotations", "rotation-90.json")
            }).Build(),
            NullLogger<RridaEngineContextFactory>.Instance));

        [Fact]
        public async Task The_invite_carries_the_questions_engine_today_returns()
        {
            await using var db = NewDb();
            const uint unit = 9601;
            SeedTeam(db, unit, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-10));
            var checkIn = NewCheckIn(db);
            await checkIn.EnrolAsync(new EngineEnrolRequestDto { TeamCode = $"S{unit}", MemberToken = "invite-member" });

            var today = await checkIn.TodayAsync("invite-member", unit);
            var calendarEvent = await NewService(checkIn, unit).GetDailyQuestionCalendarEventAsync(null, unit);

            Assert.NotNull(calendarEvent);
            Assert.Equal(today.Questions.Single(q => q.Track == "individual").Text, calendarEvent!.QuestionText);
            Assert.Equal(today.Questions.Single(q => q.Track == "team").Text, calendarEvent.TeamQuestionText);
            Assert.Equal(today.Questions.Single(q => q.Track == "individual").QuestionKey, calendarEvent.QuestionKey);
            Assert.Equal(today.Date.ToDateTime(new TimeOnly(17, 0)), calendarEvent.EventStartLocal);
        }

        [Fact]
        public async Task The_link_opens_the_team_on_the_feedback_app_from_settings()
        {
            await using var db = NewDb();
            const uint unit = 9602;
            SeedTeam(db, unit, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-10));

            var calendarEvent = await NewService(NewCheckIn(db), unit, FeedbackApp + "/")
                .GetDailyQuestionCalendarEventAsync(null, unit);

            Assert.Equal($"{FeedbackApp}/S{unit}", calendarEvent!.QuestionLink);
        }

        [Fact]
        public async Task A_team_not_asked_today_gets_its_next_asked_day()
        {
            await using var db = NewDb();
            const uint unit = 9603;
            var start = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3);
            SeedTeam(db, unit, start);

            var day = await NewCheckIn(db).TeamDayAsync(unit);

            Assert.Equal(start, day!.Date);
            Assert.Equal(1, day.AskedDayIndex);
            Assert.Equal(121, day.Questions.Single(q => q.Track == "individual").QuestionKey);
            Assert.Equal(301, day.Questions.Single(q => q.Track == "team").QuestionKey);
        }

        [Fact]
        public async Task A_team_with_the_engine_off_gets_no_invite()
        {
            await using var db = NewDb();
            const uint unit = 9604;
            SeedTeam(db, unit, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-10), enabled: false);

            var calendarEvent = await NewService(NewCheckIn(db), unit).GetDailyQuestionCalendarEventAsync(null, unit);

            Assert.Null(calendarEvent);
        }

        [Fact]
        public void The_description_is_static_with_the_feedback_link_and_no_questions()
        {
            var calendarEvent = NewEvent();

            var description = DailyQuestionCalendarBuilder.BuildDescription(calendarEvent);
            var calendar = DailyQuestionCalendarBuilder.Build(calendarEvent, DateTime.UtcNow);

            Assert.Equal("Team Code: abc123\nAnswer here: https://feedback.test/abc123", description);
            Assert.DoesNotContain("Did you feel heard?", calendar);
            Assert.DoesNotContain("Does the team share?", calendar);
            Assert.Contains("PRODID:-//RRIDA//Daily Question//EN", calendar);
            Assert.Contains("UID:abc123-weekdays-download@dailyquestion.rrida.com", calendar.Replace("\r\n ", string.Empty));
            Assert.DoesNotContain("clover", calendar, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void The_event_repeats_monday_to_friday_with_no_end_date()
        {
            var calendarEvent = NewEvent();

            var published = DailyQuestionCalendarBuilder.Build(calendarEvent, DateTime.UtcNow);
            var invite = DailyQuestionCalendarBuilder.BuildInvite(
                calendarEvent, DateTime.UtcNow, "noreply@rrida.com", "RRIDA", new[] { "member@team.test" });
            var link = DailyQuestionCalendarBuilder.BuildEventLink(calendarEvent);

            foreach (var calendar in new[] { published, invite })
            {
                Assert.Contains("\r\nRRULE:FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR\r\n", calendar);
                Assert.DoesNotContain("UNTIL=", calendar);
                Assert.DoesNotContain("COUNT=", calendar);
                Assert.Contains("DTSTART:20261030T170000", calendar);
                Assert.Contains("DTEND:20261030T171500", calendar);
            }

            Assert.Contains("ATTENDEE;CUTYPE=INDIVIDUAL;ROLE=REQ-PARTICIPANT;PARTSTAT=NEEDS-ACTION;", invite);
            Assert.Contains("&recur=RRULE%3AFREQ%3DWEEKLY%3BBYDAY%3DMO%2CTU%2CWE%2CTH%2CFR", link);
        }

        [Fact]
        public void A_weekend_event_day_starts_the_series_on_monday()
        {
            var calendarEvent = NewEvent();
            calendarEvent.EventStartLocal = new DateTime(2026, 10, 31, 17, 0, 0);
            calendarEvent.EventEndLocal = new DateTime(2026, 10, 31, 17, 15, 0);

            var calendar = DailyQuestionCalendarBuilder.Build(calendarEvent, DateTime.UtcNow);
            var link = DailyQuestionCalendarBuilder.BuildEventLink(calendarEvent);

            Assert.Contains("DTSTART:20261102T170000", calendar);
            Assert.Contains("DTEND:20261102T171500", calendar);
            Assert.Contains("&dates=20261102T170000/20261102T171500", link);
        }

        [Fact]
        public void The_download_is_a_plain_editable_event_and_the_email_is_an_invitation()
        {
            var calendarEvent = NewEvent();

            var download = Unfold(DailyQuestionCalendarBuilder.Build(calendarEvent, DateTime.UtcNow));
            var invite = Unfold(DailyQuestionCalendarBuilder.BuildInvite(
                calendarEvent, DateTime.UtcNow, "noreply@rrida.com", "RRIDA", new[] { "member@team.test" }));

            // Download: nobody else owns it, so the importing user can edit date, time, reminder.
            Assert.Contains("METHOD:PUBLISH", download);
            Assert.DoesNotContain("ORGANIZER", download);
            Assert.DoesNotContain("ATTENDEE", download);
            Assert.Contains("UID:abc123-weekdays-download@dailyquestion.rrida.com\r\n", download);

            // Invitation: accept / decline stays.
            Assert.Contains("METHOD:REQUEST", invite);
            Assert.Contains("ORGANIZER;CN=\"RRIDA\":mailto:noreply@rrida.com", invite);
            Assert.Contains("UID:abc123-weekdays@dailyquestion.rrida.com\r\n", invite);
        }

        [Fact]
        public void The_email_sends_the_editable_event_by_default_with_no_organizer()
        {
            // Default mail path: the plain event (what Build returns) goes as the attachment.
            Assert.True(new DailyQuestionInviteEmailSettings().SendAsEditableEvent);

            var calendarEvent = NewEvent();
            calendarEvent.TeamName = "Product Dev Team";
            var calendar = Unfold(DailyQuestionCalendarBuilder.Build(calendarEvent, DateTime.UtcNow));

            // No organizer / attendee = Outlook does not file it as a locked meeting, so the
            // recipient can move date and time; and there is nobody to send an RSVP reply to.
            Assert.Contains("METHOD:PUBLISH", calendar);
            Assert.DoesNotContain("METHOD:REQUEST", calendar);
            Assert.DoesNotContain("ORGANIZER", calendar);
            Assert.DoesNotContain("ATTENDEE", calendar);
            Assert.DoesNotContain("RSVP", calendar);
            Assert.Contains("\r\nRRULE:FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR\r\n", calendar);
            Assert.Contains("\r\nTRIGGER:PT0M\r\n", calendar);
            Assert.Contains("DESCRIPTION:Team: Product Dev Team\\nTeam Code: abc123\\nAnswer here: https://feedback.test/abc123\r\n", calendar);
        }

        [Fact]
        public void The_download_uid_is_the_same_on_every_download()
        {
            var first = Unfold(DailyQuestionCalendarBuilder.Build(NewEvent(), DateTime.UtcNow));

            var later = NewEvent();
            later.EventStartLocal = later.EventStartLocal.AddDays(4);
            later.EventEndLocal = later.EventEndLocal.AddDays(4);
            later.QuestionText = "Another day's question";
            var second = Unfold(DailyQuestionCalendarBuilder.Build(later, DateTime.UtcNow.AddDays(4)));

            const string uid = "UID:abc123-weekdays-download@dailyquestion.rrida.com\r\n";
            Assert.Contains(uid, first);
            Assert.Contains(uid, second);
        }

        [Theory]
        [InlineData(0, "TRIGGER:PT0M")]
        [InlineData(15, "TRIGGER:-PT15M")]
        [InlineData(10080, "TRIGGER:-PT10080M")]
        public void A_reminder_is_written_as_an_alarm_and_zero_is_kept(int minutes, string trigger)
        {
            var calendarEvent = NewEvent();
            calendarEvent.ReminderMinutes = minutes;

            var download = Unfold(DailyQuestionCalendarBuilder.Build(calendarEvent, DateTime.UtcNow));
            var invite = Unfold(DailyQuestionCalendarBuilder.BuildInvite(
                calendarEvent, DateTime.UtcNow, "noreply@rrida.com", "RRIDA", new[] { "member@team.test" }));

            foreach (var calendar in new[] { download, invite })
            {
                var alarm = $"BEGIN:VALARM\r\nACTION:DISPLAY\r\nDESCRIPTION:RRIDA Daily Check-in\r\n{trigger}\r\nEND:VALARM\r\nEND:VEVENT\r\n";
                Assert.Contains(alarm, calendar);
            }
        }

        [Fact]
        public void No_reminder_asked_for_is_the_standard_zero_minute_alarm()
        {
            var calendarEvent = NewEvent();
            calendarEvent.ReminderMinutes = null;

            var download = DailyQuestionCalendarBuilder.Build(calendarEvent, DateTime.UtcNow);
            var invite = DailyQuestionCalendarBuilder.BuildInvite(
                calendarEvent, DateTime.UtcNow, "noreply@rrida.com", "RRIDA", new[] { "member@team.test" });

            foreach (var calendar in new[] { download, invite })
            {
                Assert.Contains("\r\nTRIGGER:PT0M\r\n", calendar);
                Assert.Single(System.Text.RegularExpressions.Regex.Matches(calendar, "BEGIN:VALARM"));
            }

            Assert.Equal(0, DailyQuestionCalendarBuilder.ResolveReminder(null));
            Assert.Equal(25, DailyQuestionCalendarBuilder.ResolveReminder(25));
        }

        [Fact]
        public void The_standard_event_is_free_recurring_and_question_free_in_every_format()
        {
            var calendarEvent = NewEvent();

            var download = Unfold(DailyQuestionCalendarBuilder.Build(calendarEvent, DateTime.UtcNow));
            var invite = Unfold(DailyQuestionCalendarBuilder.BuildInvite(
                calendarEvent, DateTime.UtcNow, "noreply@rrida.com", "RRIDA", new[] { "member@team.test" }));
            var link = DailyQuestionCalendarBuilder.BuildEventLink(calendarEvent);

            foreach (var calendar in new[] { download, invite })
            {
                Assert.Contains("\r\nSUMMARY:RRIDA Daily Check-in\r\n", calendar);
                Assert.Contains("\r\nTRANSP:TRANSPARENT\r\n", calendar);
                Assert.Contains("\r\nX-MICROSOFT-CDO-BUSYSTATUS:FREE\r\n", calendar);
                Assert.Contains("\r\nX-MICROSOFT-CDO-INTENDEDSTATUS:FREE\r\n", calendar);
                Assert.Contains("\r\nRRULE:FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR\r\n", calendar);
                Assert.Contains("Answer here: https://feedback.test/abc123", calendar);
                Assert.DoesNotContain("Did you feel heard?", calendar);
                Assert.DoesNotContain("Does the team share?", calendar);
                Assert.DoesNotContain("Daily Question:", calendar);
                Assert.DoesNotContain("Team Question:", calendar);

                // One series, no per-occurrence overrides: every occurrence gets the same setup.
                Assert.Single(System.Text.RegularExpressions.Regex.Matches(calendar, "BEGIN:VEVENT"));
                Assert.DoesNotContain("RECURRENCE-ID", calendar);
            }

            Assert.Contains("&text=" + Uri.EscapeDataString("RRIDA Daily Check-in") + "&", link);
            Assert.Contains("&crm=AVAILABLE", link);
            Assert.DoesNotContain(Uri.EscapeDataString("Did you feel heard?"), link);
            Assert.DoesNotContain(Uri.EscapeDataString("Does the team share?"), link);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(10081)]
        public void A_reminder_out_of_range_is_refused(int minutes)
        {
            var calendarEvent = NewEvent();
            calendarEvent.ReminderMinutes = minutes;

            Assert.False(DailyQuestionCalendarBuilder.IsValidReminder(minutes));
            Assert.Throws<ArgumentOutOfRangeException>(() => DailyQuestionCalendarBuilder.Build(calendarEvent, DateTime.UtcNow));
        }

        [Theory]
        [InlineData(null, true)]
        [InlineData(0, true)]
        [InlineData(10080, true)]
        [InlineData(-1, false)]
        [InlineData(10081, false)]
        public void Reminder_range_accepts_zero_and_none(int? minutes, bool valid) =>
            Assert.Equal(valid, DailyQuestionCalendarBuilder.IsValidReminder(minutes));

        [Theory]
        [InlineData(0, true)]
        [InlineData(10080, true)]
        [InlineData(null, true)]
        [InlineData(-1, false)]
        [InlineData(10081, false)]
        public void The_email_request_validates_the_reminder_range(int? minutes, bool valid)
        {
            var dto = new SendDailyQuestionCalendarInviteDto { UnitId = 1, Emails = new() { "a@b.test" }, ReminderMinutes = minutes };

            var ok = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
                dto, new System.ComponentModel.DataAnnotations.ValidationContext(dto), new List<System.ComponentModel.DataAnnotations.ValidationResult>(), validateAllProperties: true);

            Assert.Equal(valid, ok);
        }

        private static string Unfold(string calendar) => calendar.Replace("\r\n ", string.Empty);

        private static DailyQuestionCalendarEventDto NewEvent() => new()
        {
            TeamCode = "abc123",
            QuestionKey = 121,
            QuestionText = "Did you feel heard?",
            TeamQuestionText = "Does the team share?",
            QuestionLink = "https://feedback.test/abc123",
            EventStartLocal = new DateTime(2026, 10, 30, 17, 0, 0),
            EventEndLocal = new DateTime(2026, 10, 30, 17, 15, 0)
        };

        private static EngineCheckInService NewCheckIn(ApplicationDbContext db) =>
            new(db, Factory.Value, new Mock<IEngineTeamProvisioner>().Object, NullLogger<EngineCheckInService>.Instance);

        private static QuestionService NewService(IEngineCheckInService checkIn, uint unit, string feedbackApp = FeedbackApp)
        {
            var repository = new Mock<IQuestionRepository>();
            repository.Setup(r => r.GetDailyQuestionCalendarTeamAsync(null, unit))
                .ReturnsAsync(new DailyQuestionCalendarTeamDto { UnitId = unit, TeamCode = $"S{unit}" });

            return new QuestionService(
                repository.Object,
                checkIn,
                Options.Create(new SendGridSettings { FeedbackAppBaseUrl = feedbackApp }),
                Options.Create(new DailyQuestionInviteEmailSettings()));
        }

        private static void SeedTeam(ApplicationDbContext db, uint unit, DateOnly start, bool enabled = true)
        {
            db.BusinessUnits.Add(new BusinessUnit
            {
                Id = unit, BusinessUnitCode = $"BU{unit}", BusinessUnitShortCode = $"S{unit}", Name = "Team",
                NameInvariant = "team", RegistrationDate = DateTime.UtcNow, ProfileId = 94,
                ActiveDailyQuestionSetGroupId = 1, ActiveDailyQuestionSetGroup = null!
            });
            db.EngineTeamSettings.Add(new EngineTeamSettings
            {
                BusinessUnitId = unit, StartDate = start, WorkingWeek = "every-day", IsEnabled = enabled, CreatedAt = DateTime.UtcNow
            });
            db.SaveChanges();
        }

        private static ApplicationDbContext NewDb() =>
            new TestApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"calendar-invite-{Guid.NewGuid()}")
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);

        private static string FindEngineRoot([CallerFilePath] string source = "")
        {
            for (var dir = new DirectoryInfo(Path.GetDirectoryName(source)!); dir is not null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "external", "RRIDA-Engine");
                if (Directory.Exists(Path.Combine(candidate, "kit", "data"))) return candidate;
            }
            throw new DirectoryNotFoundException("external/RRIDA-Engine was not found.");
        }
    }
}
