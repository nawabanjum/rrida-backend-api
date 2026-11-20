using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO.TeamHealth;
using CloverBackoffice.Infrastructure.Models.TeamHealth;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Services.Implementation
{
    // The "ERAN suggestion" dropdown on a manual action.
    //
    //   GET  api/actions/eran-suggestions?teamId=  the team's latest suggestions + None / None of these
    //   POST / PATCH api/actions                    engineActionId and/or suggestionChoice
    //
    // A suggestion is one rung of an engine cycle (EngineCycleDeliveries; its id is the
    // api/team-health/actions id), whether or not anyone took it up. Linking one keeps the
    // action MANUAL and the manager's own: nothing is written to the engine's tables, so it is
    // never an engine action, never a timeline dot and never confirms anything. Eran is told
    // which suggestion an open action follows (ManagerActionEranContext).
    //
    // The engine rebuilds its deliveries on every run and drops a cycle a later run no longer
    // produces, so the link is stored by the engine's natural key (cycle key, rung, action key)
    // with ERAN's wording as picked; a read finds the suggestion's current id by that key.
    public partial class ManagerActionService
    {
        /// <summary>How many suggestions the dropdown offers.</summary>
        public const int SuggestionCount = 5;

        private static readonly IReadOnlyList<SuggestionOptionDto> OtherSuggestionOptions =
        [
            new() { Value = ManagerActionSuggestionChoices.None, Label = "None" },
            new() { Value = ManagerActionSuggestionChoices.NoneOfThese, Label = "None of these" }
        ];

        // ── GET api/actions/eran-suggestions ─────────────────────────────────

        public async Task<TeamMilestoneResult<EranSuggestionListDto>> ListEranSuggestionsAsync(
            uint? teamId, int actorUserId, CancellationToken ct = default)
        {
            if (teamId is not > 0)
                return SuggestionFail(TeamMilestoneError.BadRequest, ResponseMessages.Errors.ManagerActionTeamRequired);
            var unitId = teamId.Value;

            var team = await _context.BusinessUnits.AsNoTracking()
                .Where(b => b.Id == unitId && b.IsDel != true)
                .Select(b => new { b.ProfileId })
                .FirstOrDefaultAsync(ct);
            if (team == null)
                return SuggestionFail(TeamMilestoneError.NotFound, ResponseMessages.Errors.ManagerActionTeamNotFound);
            if (!await _access.CanAccessTeamAsync(unitId, team.ProfileId, actorUserId, ct))
                return SuggestionFail(TeamMilestoneError.Forbidden, ResponseMessages.Errors.ManagerActionAccessDenied);

            var result = new EranSuggestionListDto
            {
                TeamId = unitId,
                OtherOptions = OtherSuggestionOptions
                    .Select(o => new SuggestionOptionDto { Value = o.Value, Label = o.Label })
                    .ToList()
            };

            var suggestions = await DeliveredSuggestionsAsync(unitId, ct);
            if (suggestions.Count == 0)
                return TeamMilestoneResult<EranSuggestionListDto>.Ok(result);

            var latest = suggestions
                .OrderByDescending(s => s.DeliveredDay)
                .ThenByDescending(s => s.Rung)
                .ThenByDescending(s => s.Id)
                .Take(SuggestionCount)
                .ToList();

            var keys = latest.Select(s => s.CycleKey).Distinct().ToList();
            var takenUp = (await _context.EngineAcknowledgements.AsNoTracking()
                    .Where(k => k.BusinessUnitId == unitId && keys.Contains(k.CycleKey))
                    .Select(k => new { k.CycleKey, k.Rung })
                    .ToListAsync(ct))
                .Select(k => (k.CycleKey, k.Rung))
                .ToHashSet();

            result.Suggestions = latest.Select(s => new EranSuggestionDto
            {
                EngineActionId = s.Id,
                Title = s.Text,
                SuggestedOn = Iso(s.SuggestedOn),
                Rung = s.Rung,
                TakenUp = takenUp.Contains((s.CycleKey, s.Rung))
            }).ToList();
            return TeamMilestoneResult<EranSuggestionListDto>.Ok(result);
        }

        private static TeamMilestoneResult<EranSuggestionListDto> SuggestionFail(TeamMilestoneError error, string message) =>
            TeamMilestoneResult<EranSuggestionListDto>.Fail(error, message);

        private sealed record Suggestion(
            long Id, string CycleKey, int Rung, string ActionKey, string Text, int DeliveredDay, DateOnly SuggestedOn);

        /// <summary>
        /// The team's suggestions ERAN has delivered up to today, with wording. Optionally only
        /// the one delivery id. Empty for a team not on the engine.
        /// </summary>
        private async Task<List<Suggestion>> DeliveredSuggestionsAsync(uint unitId, CancellationToken ct, long? onlyId = null)
        {
            var start = await _context.EngineTeamSettings.AsNoTracking()
                .Where(s => s.BusinessUnitId == unitId)
                .Select(s => (DateOnly?)s.StartDate)
                .FirstOrDefaultAsync(ct);
            if (start is not DateOnly startDate) return new List<Suggestion>();

            // Engine day 1 is the start date; nothing a longer run put after today is offered.
            var todayDay = Today.DayNumber - startDate.DayNumber + 1;

            var query =
                from d in _context.EngineCycleDeliveries.AsNoTracking()
                join c in _context.EngineCycles.AsNoTracking() on d.CycleId equals c.Id
                where c.BusinessUnitId == unitId
                      && d.DeliveredDay >= 1 && d.DeliveredDay <= todayDay
                      && d.DeliveredText != null && d.DeliveredText != ""
                select new { d.Id, c.CycleKey, d.Rung, d.ActionKey, d.DeliveredText, d.DeliveredDay };
            if (onlyId is long id)
                query = query.Where(x => x.Id == id);

            return (await query.ToListAsync(ct))
                .Select(x => new Suggestion(x.Id, x.CycleKey, x.Rung, x.ActionKey, x.DeliveredText!.Trim(),
                    x.DeliveredDay, startDate.AddDays(x.DeliveredDay - 1)))
                .ToList();
        }

        // ── POST / PATCH: engineActionId + suggestionChoice ──────────────────

        /// <summary>
        /// What to do with the action's link. <c>Unchanged</c>: neither field sent.
        /// <c>Clear</c>: "suggestionChoice": "" (PATCH). Otherwise the choice, and for SUGGESTION
        /// the suggestion picked.
        /// </summary>
        private sealed record SuggestionPick(bool Unchanged, bool Clear, string? Choice, Suggestion? Picked)
        {
            public static readonly SuggestionPick None = new(true, false, null, null);
        }

        private async Task<(SuggestionPick Pick, string? Error)> ResolveSuggestionAsync(
            uint unitId, long? engineActionId, string? suggestionChoice, CancellationToken ct)
        {
            var choice = suggestionChoice?.Trim().ToUpperInvariant();
            if (!string.IsNullOrEmpty(choice) && !ManagerActionSuggestionChoices.All.Contains(choice))
                return (SuggestionPick.None, ResponseMessages.Errors.ManagerActionSuggestionChoiceInvalid);

            if (engineActionId is long id)
            {
                // An id goes with SUGGESTION or on its own, never with NONE / NONE_OF_THESE / "".
                if (choice != null && choice != ManagerActionSuggestionChoices.Suggestion)
                    return (SuggestionPick.None, ResponseMessages.Errors.ManagerActionSuggestionConflict);
                if (id <= 0)
                    return (SuggestionPick.None, ResponseMessages.Errors.ManagerActionSuggestionNotFound);

                // One of this team's delivered suggestions; it need not still be among the
                // latest five, so a suggestion arriving while the form is open breaks nothing.
                var picked = (await DeliveredSuggestionsAsync(unitId, ct, id)).FirstOrDefault();
                if (picked == null)
                    return (SuggestionPick.None, ResponseMessages.Errors.ManagerActionSuggestionNotFound);
                return (new SuggestionPick(false, false, ManagerActionSuggestionChoices.Suggestion, picked), null);
            }

            return choice switch
            {
                null => (SuggestionPick.None, null),
                "" => (new SuggestionPick(false, true, null, null), null),
                ManagerActionSuggestionChoices.Suggestion =>
                    (SuggestionPick.None, ResponseMessages.Errors.ManagerActionSuggestionIdRequired),
                _ => (new SuggestionPick(false, false, choice, null), null)
            };
        }

        /// <summary>Writes the pick onto the row: the choice, and the suggestion only for SUGGESTION.</summary>
        private static void ApplySuggestion(ManagerAction action, SuggestionPick pick)
        {
            if (pick.Unchanged) return;

            var s = pick.Picked;
            action.SuggestionChoice = pick.Clear ? null : pick.Choice;
            action.EngineDeliveryId = s?.Id;
            action.EngineCycleKey = s?.CycleKey;
            action.EngineRung = s?.Rung;
            action.EngineActionKey = s?.ActionKey;
            action.EngineSuggestionText = s == null ? null : Truncate(s.Text, ManagerAction.SuggestionTextMaxLength);
            action.EngineSuggestedOn = s?.SuggestedOn;
        }

        private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

        // ── Read: the linked suggestion ──────────────────────────────────────

        private sealed record LinkKey(uint UnitId, string CycleKey, int Rung, string ActionKey);

        /// <summary>The linked suggestions' current delivery ids, found by the engine's natural key.</summary>
        private async Task<Dictionary<LinkKey, long>> CurrentSuggestionIdsAsync(IReadOnlyList<ManagerAction> actions, CancellationToken ct)
        {
            var linked = actions
                .Where(a => a.SuggestionChoice == ManagerActionSuggestionChoices.Suggestion && a.EngineCycleKey != null)
                .ToList();
            if (linked.Count == 0) return new Dictionary<LinkKey, long>();

            var unitIds = linked.Select(a => a.UnitId).Distinct().ToList();
            var keys = linked.Select(a => a.EngineCycleKey!).Distinct().ToList();
            var rows = await (
                    from d in _context.EngineCycleDeliveries.AsNoTracking()
                    join c in _context.EngineCycles.AsNoTracking() on d.CycleId equals c.Id
                    where unitIds.Contains(c.BusinessUnitId) && keys.Contains(c.CycleKey)
                    select new { c.BusinessUnitId, c.CycleKey, d.Rung, d.ActionKey, d.Id })
                .ToListAsync(ct);

            var current = new Dictionary<LinkKey, long>();
            foreach (var r in rows)
                current[new LinkKey(r.BusinessUnitId, r.CycleKey, r.Rung, r.ActionKey)] = r.Id;
            return current;
        }

        private static (long? EngineActionId, LinkedSuggestionDto? Linked) LinkOf(
            ManagerAction a, IReadOnlyDictionary<LinkKey, long> current)
        {
            if (a.SuggestionChoice != ManagerActionSuggestionChoices.Suggestion) return (null, null);

            long? currentId = a.EngineCycleKey != null && a.EngineRung is int rung && a.EngineActionKey != null
                              && current.TryGetValue(new LinkKey(a.UnitId, a.EngineCycleKey, rung, a.EngineActionKey), out var found)
                ? found
                : null;
            var id = currentId ?? a.EngineDeliveryId;
            return (id, new LinkedSuggestionDto
            {
                EngineActionId = id,
                Title = a.EngineSuggestionText ?? string.Empty,
                SuggestedOn = a.EngineSuggestedOn is { } on ? Iso(on) : null,
                Rung = a.EngineRung,
                IsAvailable = currentId != null
            });
        }
    }
}
