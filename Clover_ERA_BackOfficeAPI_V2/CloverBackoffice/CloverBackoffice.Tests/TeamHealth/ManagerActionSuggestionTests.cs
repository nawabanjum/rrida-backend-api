using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO.TeamHealth;
using CloverBackoffice.Infrastructure.Models.DeterministicLayer;
using CloverBackoffice.Infrastructure.Models.TeamHealth;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Implementation;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static CloverBackoffice.Tests.TeamHealth.MilestoneTestData;

namespace CloverBackoffice.Tests.TeamHealth
{
    /// <summary>
    /// The ERAN suggestion dropdown on a manual action: GET api/actions/eran-suggestions and
    /// engineActionId / suggestionChoice on POST and PATCH api/actions. Team 294 starts on
    /// 2026-09-01, so today (2026-09-29) is engine day 29:
    ///   C1 day 5            "Suggestion one"          oldest, falls out of the five
    ///   C2 day 10 / day 14  "Suggestion two" / "Two, rung two"
    ///   C3 day 18           "Suggestion three"
    ///   C4 day 22           "Suggestion four"         taken up ("I'll do it")
    ///   C5 day 26           "Suggestion five"
    ///   C6 day 35           "Not yet"                 after today: never offered
    ///   C7 day 27           (no wording)              never offered
    /// Team 295 C1 day 20 "Another team's".
    /// </summary>
    public class ManagerActionSuggestionTests
    {
        private readonly string _db = Guid.NewGuid().ToString();
        private readonly long _one, _two, _twoRung2, _three, _four, _five, _future, _otherTeam;

        public ManagerActionSuggestionTests()
        {
            using var db = CreateContext(_db);
            Seed(db);
            foreach (var unit in new[] { Team, OtherTeamA })
                db.EngineTeamSettings.Add(new EngineTeamSettings
                {
                    BusinessUnitId = unit, StartDate = new DateOnly(2026, 9, 1), IsEnabled = true, CreatedAt = DateTime.UtcNow
                });
            db.SaveChanges();

            _one = AddDelivery(db, AddCycle(db, Team, "C1", 5), 1, 5, "Suggestion one");
            var c2 = AddCycle(db, Team, "C2", 10);
            _two = AddDelivery(db, c2, 1, 10, "Suggestion two");
            _twoRung2 = AddDelivery(db, c2, 2, 14, "Two, rung two");
            _three = AddDelivery(db, AddCycle(db, Team, "C3", 18), 1, 18, "Suggestion three");
            _four = AddDelivery(db, AddCycle(db, Team, "C4", 22), 1, 22, "Suggestion four");
            _five = AddDelivery(db, AddCycle(db, Team, "C5", 26), 1, 26, "Suggestion five");
            _future = AddDelivery(db, AddCycle(db, Team, "C6", 35), 1, 35, "Not yet");
            AddDelivery(db, AddCycle(db, Team, "C7", 27), 1, 27, null);
            _otherTeam = AddDelivery(db, AddCycle(db, OtherTeamA, "C1", 20), 1, 20, "Another team's");

            db.EngineAcknowledgements.Add(new EngineAcknowledgement
            {
                BusinessUnitId = Team, CycleKey = "C4", Rung = 1, AcknowledgedByUserId = ManagerOfTeam,
                AcknowledgedAt = new DateTime(2026, 9, 22, 9, 0, 0, DateTimeKind.Utc)
            });
            db.SaveChanges();
        }

        private static EngineCycle AddCycle(ApplicationDbContext db, uint unit, string key, int day)
        {
            var cycle = new EngineCycle
            {
                BusinessUnitId = unit, CycleKey = key, IssueFileKey = "IF-1", SelectedDay = day,
                DeliveredDay = day, ConfirmationDueDay = day + 3, WrittenAt = DateTime.UtcNow
            };
            db.EngineCycles.Add(cycle);
            db.SaveChanges();
            return cycle;
        }

        private static long AddDelivery(ApplicationDbContext db, EngineCycle cycle, int rung, int day, string? text)
        {
            var delivery = new EngineCycleDelivery
            {
                CycleId = cycle.Id, Rung = rung, ActionKey = $"MA-{cycle.CycleKey}-{rung}", DeliveredText = text,
                DeliveredWorkedIf = "They name one thing.", DeliveredDay = day
            };
            db.EngineCycleDeliveries.Add(delivery);
            db.SaveChanges();
            return delivery.Id;
        }

        private ManagerActionService Service(ApplicationDbContext db) =>
            new(db, new TeamHealthAccessService(db, Roles()), Roles(), new FixedClock(Today));

        private async Task<TeamMilestoneResult<EranSuggestionListDto>> Suggestions(uint? teamId, int actor = ManagerOfTeam)
        {
            using var db = CreateContext(_db);
            return await Service(db).ListEranSuggestionsAsync(teamId, actor);
        }

        private static CreateManagerActionDto NewAction(long? engineActionId = null, string? choice = null, uint team = Team) => new()
        {
            TeamId = team, Title = "Run a retro", StartDate = "2026-09-29",
            EngineActionId = engineActionId, SuggestionChoice = choice
        };

        private async Task<TeamMilestoneResult<ManagerActionDto>> Create(CreateManagerActionDto dto, int actor = ManagerOfTeam)
        {
            using var db = CreateContext(_db);
            return await Service(db).CreateAsync(dto, actor);
        }

        private async Task<TeamMilestoneResult<ManagerActionDto>> Patch(string id, UpdateManagerActionDto dto)
        {
            using var db = CreateContext(_db);
            return await Service(db).UpdateAsync(id, dto, ManagerOfTeam);
        }

        private async Task<ManagerActionDto> Get(string id)
        {
            using var db = CreateContext(_db);
            return (await Service(db).GetAsync(id, ManagerOfTeam)).Data!;
        }

        // ── GET api/actions/eran-suggestions ─────────────────────────────────

        [Fact]
        public async Task Suggestions_are_the_latest_five_delivered_newest_first_with_the_two_fixed_options()
        {
            var result = await Suggestions(Team);

            Assert.True(result.IsSuccess);
            var list = result.Data!;
            Assert.Equal(Team, list.TeamId);
            Assert.Equal(new[] { _five, _four, _three, _twoRung2, _two }, list.Suggestions.Select(s => s.EngineActionId));
            Assert.DoesNotContain(list.Suggestions, s => s.EngineActionId == _one || s.EngineActionId == _future);

            var four = list.Suggestions.Single(s => s.EngineActionId == _four);
            Assert.Equal("Suggestion four", four.Title);
            Assert.Equal("2026-09-22", four.SuggestedOn);
            Assert.Equal(1, four.Rung);
            Assert.True(four.TakenUp);
            Assert.False(list.Suggestions.Single(s => s.EngineActionId == _five).TakenUp);
            Assert.Equal(2, list.Suggestions.Single(s => s.EngineActionId == _twoRung2).Rung);

            Assert.Equal(new[] { "NONE", "NONE_OF_THESE" }, list.OtherOptions.Select(o => o.Value));
            Assert.Equal(new[] { "None", "None of these" }, list.OtherOptions.Select(o => o.Label));
        }

        [Fact]
        public async Task Suggestions_need_a_team_the_caller_can_open()
        {
            Assert.Equal(TeamMilestoneError.BadRequest, (await Suggestions(null)).Error);
            Assert.Equal(TeamMilestoneError.NotFound, (await Suggestions(9999)).Error);
            Assert.Equal(TeamMilestoneError.Forbidden, (await Suggestions(Team, ProfileAdminB)).Error);
            Assert.True((await Suggestions(Team, ProfileAdminA)).IsSuccess);
        }

        [Fact]
        public async Task A_team_not_on_the_engine_gets_no_suggestions_but_the_fixed_options()
        {
            var list = (await Suggestions(ParentUnit, ManagerOfParent)).Data!;

            Assert.Empty(list.Suggestions);
            Assert.Equal(2, list.OtherOptions.Count);
        }

        // ── POST api/actions ─────────────────────────────────────────────────

        [Fact]
        public async Task Create_with_a_suggestion_links_it_and_stays_manual()
        {
            int acksBefore;
            using (var db = CreateContext(_db)) acksBefore = await db.EngineAcknowledgements.CountAsync();

            var created = await Create(NewAction(_three));

            Assert.True(created.IsSuccess, created.Message);
            var a = created.Data!;
            Assert.Equal(ManagerActionSources.Manual, a.Source);
            Assert.Equal(ManagerActionStatuses.Open, a.Status);
            Assert.Equal(_three, a.EngineActionId);
            Assert.Equal("SUGGESTION", a.SuggestionChoice);
            Assert.NotNull(a.LinkedSuggestion);
            Assert.Equal(_three, a.LinkedSuggestion!.EngineActionId);
            Assert.Equal("Suggestion three", a.LinkedSuggestion.Title);
            Assert.Equal("2026-09-18", a.LinkedSuggestion.SuggestedOn);
            Assert.True(a.LinkedSuggestion.IsAvailable);

            // Saved, and returned by both GET APIs.
            var got = await Get(a.ActionId);
            Assert.Equal(_three, got.EngineActionId);
            Assert.Equal("Suggestion three", got.LinkedSuggestion!.Title);
            using (var db = CreateContext(_db))
            {
                var listed = (await Service(db).ListAsync(new ManagerActionQueryDto { TeamId = Team }, ManagerOfTeam)).Data!;
                var row = listed.Actions.Single(x => x.ActionId == a.ActionId);
                Assert.Equal(_three, row.EngineActionId);
                Assert.Equal(ManagerActionSources.Manual, row.Source);

                // Nothing written to the engine: not taken up, no new engine action listed.
                Assert.Equal(acksBefore, await db.EngineAcknowledgements.CountAsync());
                Assert.DoesNotContain(listed.Actions, x => x.ActionId == ManagerActionService.EngineIdPrefix + _three);
            }
        }

        [Fact]
        public async Task Create_with_suggestionChoice_SUGGESTION_and_the_id_is_the_same_as_the_id_alone()
        {
            var a = (await Create(NewAction(_four, "suggestion"))).Data!;

            Assert.Equal(_four, a.EngineActionId);
            Assert.Equal("SUGGESTION", a.SuggestionChoice);
        }

        [Fact]
        public async Task Create_without_a_suggestion_has_no_link()
        {
            var a = (await Create(NewAction())).Data!;

            Assert.Equal(ManagerActionSources.Manual, a.Source);
            Assert.Null(a.EngineActionId);
            Assert.Null(a.SuggestionChoice);
            Assert.Null(a.LinkedSuggestion);
        }

        [Theory]
        [InlineData("NONE")]
        [InlineData("none_of_these")]
        public async Task Create_with_None_or_None_of_these_saves_the_choice_without_a_link(string choice)
        {
            var a = (await Create(NewAction(choice: choice))).Data!;

            Assert.Equal(choice.ToUpperInvariant(), a.SuggestionChoice);
            Assert.Null(a.EngineActionId);
            Assert.Null(a.LinkedSuggestion);
        }

        [Fact]
        public async Task An_older_suggestion_outside_the_five_can_still_be_linked()
        {
            var a = (await Create(NewAction(_one))).Data!;

            Assert.Equal(_one, a.EngineActionId);
        }

        [Fact]
        public async Task Create_refuses_a_bad_suggestion()
        {
            async Task<string> Refused(CreateManagerActionDto dto)
            {
                var r = await Create(dto);
                Assert.False(r.IsSuccess);
                Assert.Equal(TeamMilestoneError.BadRequest, r.Error);
                return r.Message;
            }

            Assert.Equal(ResponseMessages.Errors.ManagerActionSuggestionNotFound, await Refused(NewAction(_otherTeam)));
            Assert.Equal(ResponseMessages.Errors.ManagerActionSuggestionNotFound, await Refused(NewAction(_future)));
            Assert.Equal(ResponseMessages.Errors.ManagerActionSuggestionNotFound, await Refused(NewAction(987654)));
            Assert.Equal(ResponseMessages.Errors.ManagerActionSuggestionNotFound, await Refused(NewAction(0)));
            Assert.Equal(ResponseMessages.Errors.ManagerActionSuggestionConflict, await Refused(NewAction(_three, "NONE")));
            Assert.Equal(ResponseMessages.Errors.ManagerActionSuggestionIdRequired, await Refused(NewAction(choice: "SUGGESTION")));
            Assert.Equal(ResponseMessages.Errors.ManagerActionSuggestionChoiceInvalid, await Refused(NewAction(choice: "MAYBE")));

            using var db = CreateContext(_db);
            Assert.Equal(0, await db.ManagerActions.CountAsync());
        }

        // ── PATCH api/actions/{id} ───────────────────────────────────────────

        [Fact]
        public async Task Patch_repicks_sets_None_and_clears_the_suggestion()
        {
            var id = (await Create(NewAction(_three))).Data!.ActionId;

            var repicked = (await Patch(id, new UpdateManagerActionDto { EngineActionId = _five })).Data!;
            Assert.Equal(_five, repicked.EngineActionId);
            Assert.Equal("Suggestion five", repicked.LinkedSuggestion!.Title);

            var none = (await Patch(id, new UpdateManagerActionDto { SuggestionChoice = "NONE_OF_THESE" })).Data!;
            Assert.Equal("NONE_OF_THESE", none.SuggestionChoice);
            Assert.Null(none.EngineActionId);
            Assert.Null(none.LinkedSuggestion);

            var cleared = (await Patch(id, new UpdateManagerActionDto { SuggestionChoice = "" })).Data!;
            Assert.Null(cleared.SuggestionChoice);
            Assert.Null(cleared.EngineActionId);

            using var db = CreateContext(_db);
            var row = await db.ManagerActions.SingleAsync();
            Assert.Null(row.EngineDeliveryId);
            Assert.Null(row.EngineCycleKey);
            Assert.Null(row.EngineSuggestionText);
        }

        [Fact]
        public async Task Patch_of_other_fields_keeps_the_suggestion()
        {
            var id = (await Create(NewAction(_three))).Data!.ActionId;

            var patched = (await Patch(id, new UpdateManagerActionDto { Status = "IN_PROGRESS" })).Data!;

            Assert.Equal(_three, patched.EngineActionId);
            Assert.Equal("SUGGESTION", patched.SuggestionChoice);
        }

        [Fact]
        public async Task Patch_refuses_a_bad_suggestion_and_engine_actions()
        {
            var id = (await Create(NewAction())).Data!.ActionId;

            var other = await Patch(id, new UpdateManagerActionDto { EngineActionId = _otherTeam });
            Assert.Equal(ResponseMessages.Errors.ManagerActionSuggestionNotFound, other.Message);

            var engine = await Patch(ManagerActionService.EngineIdPrefix + _four,
                new UpdateManagerActionDto { SuggestionChoice = "NONE" });
            Assert.Equal(TeamMilestoneError.BadRequest, engine.Error);
            Assert.Equal(ResponseMessages.Errors.ManagerActionSuggestionEngineAction, engine.Message);
        }

        // ── Engine re-runs ───────────────────────────────────────────────────

        [Fact]
        public async Task The_link_follows_the_suggestion_through_an_engine_rebuild_and_keeps_its_wording_when_gone()
        {
            var id = (await Create(NewAction(_three))).Data!.ActionId;

            // A re-run writes the same rung again under a new id.
            long rebuilt;
            using (var db = CreateContext(_db))
            {
                var old = await db.EngineCycleDeliveries.SingleAsync(d => d.Id == _three);
                var cycle = await db.EngineCycles.SingleAsync(c => c.Id == old.CycleId);
                db.EngineCycleDeliveries.Remove(old);
                await db.SaveChangesAsync();
                rebuilt = AddDelivery(db, cycle, 1, 18, "Suggestion three, reworded");
            }
            var after = await Get(id);
            Assert.Equal(rebuilt, after.EngineActionId);
            Assert.True(after.LinkedSuggestion!.IsAvailable);
            Assert.Equal("Suggestion three", after.LinkedSuggestion.Title);   // as the manager picked it

            // A later run no longer produces the cycle at all.
            using (var db = CreateContext(_db))
            {
                db.EngineCycleDeliveries.RemoveRange(db.EngineCycleDeliveries.Where(d => d.Id == rebuilt));
                db.EngineCycles.RemoveRange(db.EngineCycles.Where(c => c.BusinessUnitId == Team && c.CycleKey == "C3"));
                await db.SaveChangesAsync();
            }
            var gone = await Get(id);
            Assert.Equal(_three, gone.EngineActionId);
            Assert.False(gone.LinkedSuggestion!.IsAvailable);
            Assert.Equal("Suggestion three", gone.LinkedSuggestion.Title);
        }

        // ── Eran ─────────────────────────────────────────────────────────────

        [Fact]
        public async Task Eran_is_told_which_suggestion_an_open_action_follows()
        {
            await Create(NewAction(_three));
            await Create(new CreateManagerActionDto { TeamId = Team, Title = "Plain one", StartDate = "2026-09-29" });

            using var db = CreateContext(_db);
            var today = DateOnly.FromDateTime(Today.UtcDateTime);
            var items = await ManagerActionEranContext.OpenAsync(db, Team, today);

            Assert.Equal("Suggestion three", items.Single(i => i.Title == "Run a retro").FollowsSuggestion);
            Assert.Null(items.Single(i => i.Title == "Plain one").FollowsSuggestion);
            var value = ManagerActionEranContext.ContextValue(items, today)!;
            Assert.Contains("follows ERAN's earlier suggestion \"Suggestion three\"", value);
            Assert.Contains("not moves this product selected", value);
        }
    }
}
