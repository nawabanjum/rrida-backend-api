using System.Text.Json;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Infrastructure.Models.DeterministicLayer;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CloverBackoffice.Services.Implementation
{
    /// <summary>
    /// What the manager sees and does.
    ///
    /// Reads come from the stored results, not from a replay: the daily job has already
    /// produced them, and a manager opening a page must not depend on a run finishing.
    ///
    /// Writes are the other way round. A confirmation or a decline is a FACT, so it is
    /// recorded and the team is then replayed, because the confirmation changes what the
    /// cycle does next and the manager should see that immediately rather than tomorrow.
    ///
    /// Nothing here returns a member, a vote count or free text. The manager is told
    /// proportions, and the identity behind them never leaves the write path.
    /// </summary>
    public sealed class EngineManagerService : IEngineManagerService
    {
        private readonly ApplicationDbContext _context;
        private readonly IRridaEngineContextFactory _engine;
        private readonly IUnitAccessService _access;
        private readonly IEngineReplayRunner _runner;
        private readonly ILogger<EngineManagerService> _logger;
        private readonly IPlanFeatureGate? _planGate;

        // planGate: the plan catalogue's content filters (Clover Resources on the action card,
        // daily nudges / monthly report in the feed). Null = nothing filtered.
        public EngineManagerService(
            ApplicationDbContext context,
            IRridaEngineContextFactory engine,
            IUnitAccessService access,
            IEngineReplayRunner runner,
            ILogger<EngineManagerService> logger,
            IPlanFeatureGate? planGate = null)
        {
            _planGate = planGate;
            _context = context;
            _engine = engine;
            _access = access;
            _runner = runner;
            _logger = logger;
        }

        public async Task<EngineTeamStateDto> GetStateAsync(User actor, uint businessUnitId, CancellationToken ct = default)
        {
            var clock = await RequireAccessAsync(actor, businessUnitId, ct);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var day = clock.DayOf(today);

            var row = await _context.EngineStateDaily.AsNoTracking()
                .Where(s => s.BusinessUnitId == businessUnitId && s.Day <= day)
                .OrderByDescending(s => s.Day)
                .FirstOrDefaultAsync(ct);

            // As the MVP (Team.isCalibrating): until more than six asked days are resolved.
            // The engine writes one state per asked day, from day one.
            var resolvedDays = await _context.EngineStateDaily.AsNoTracking()
                .CountAsync(s => s.BusinessUnitId == businessUnitId && s.Day <= day, ct);

            var result = new EngineTeamStateDto
            {
                BusinessUnitId = businessUnitId,
                Day = day,
                AskedDayIndex = clock.AskedDayIndexOf(today),
                IsCalibrating = resolvedDays <= TeamStateService.CalibrationDays
            };

            if (row is null || result.IsCalibrating) return result;

            result.Vertical = row.Vertical;
            result.Horizontal = row.Horizontal;
            result.HeadwindsFlag = row.HeadwindsFlag;
            result.Limb = row.Limb;
            result.ConcentrationReading = row.ConcentrationReading;
            result.ConfigId = row.ConfigId;
            result.AsAt = row.WrittenAt;
            return result;
        }

        public async Task<EngineActiveActionDto?> GetActiveActionAsync(User actor, uint businessUnitId, CancellationToken ct = default)
        {
            var action = await ActiveActionCoreAsync(actor, businessUnitId, ct);
            return _planGate == null ? action : await _planGate.FilterActionAsync(actor.UserId, action, ct);
        }

        private async Task<EngineActiveActionDto?> ActiveActionCoreAsync(User actor, uint businessUnitId, CancellationToken ct)
        {
            var clock = await RequireAccessAsync(actor, businessUnitId, ct);

            // D29. Several cycles can be open at once: the one in front of the manager, and
            // confirmed ones waiting behind it for their reading. The one in front is the most
            // recently delivered — a new selection, or a rung two taking the slot, always
            // lands after whatever it displaced — which is not always the highest cycle index.
            var open = await _context.EngineCycles.AsNoTracking()
                .Include(c => c.Deliveries)
                .Where(c => c.BusinessUnitId == businessUnitId && c.Outcome == null)
                .ToListAsync(ct);

            // A decline closes the cycle the moment it is given (MVP Team.declineContent): the
            // card goes. The engine records it on the next run; until then the tap stands in.
            var declined = await DeclinedKeysAsync(businessUnitId, ct);
            open.RemoveAll(c => declined.Contains(c.CycleKey));

            var cycle = open
                .OrderByDescending(c => c.Deliveries.Count == 0 ? c.DeliveredDay : c.Deliveries.Max(d => d.DeliveredDay))
                .ThenByDescending(c => c.CycleIndex)
                .FirstOrDefault();

            if (cycle is null) return null;

            return await ToActionAsync(cycle, businessUnitId, clock, ct);
        }

        public async Task<EngineActiveActionDto?> GetCycleActionAsync(User actor, uint businessUnitId, long cycleId, CancellationToken ct = default)
        {
            var action = await CycleActionCoreAsync(actor, businessUnitId, cycleId, ct);
            return _planGate == null ? action : await _planGate.FilterActionAsync(actor.UserId, action, ct);
        }

        private async Task<EngineActiveActionDto?> CycleActionCoreAsync(User actor, uint businessUnitId, long cycleId, CancellationToken ct)
        {
            var clock = await RequireAccessAsync(actor, businessUnitId, ct);

            // One cycle's action as delivered, open or resolved: what weekly-flags shows for the
            // week it was delivered in.
            var cycle = await _context.EngineCycles.AsNoTracking()
                .Include(c => c.Deliveries)
                .FirstOrDefaultAsync(c => c.Id == cycleId && c.BusinessUnitId == businessUnitId, ct);

            return cycle is null ? null : await ToActionAsync(cycle, businessUnitId, clock, ct);
        }

        private async Task<EngineActiveActionDto> ToActionAsync(
            EngineCycle cycle, uint businessUnitId, EngineTeamClock clock, CancellationToken ct)
        {
            // A resolved cycle is history: nothing on it can be confirmed or declined any more.
            var open = cycle.Outcome is null;

            // The current rung is the last one delivered. A cycle that has laddered has two
            // rows, and rung one's row still says what rung one did.
            var delivery = cycle.Deliveries.OrderByDescending(d => d.Rung).FirstOrDefault();
            var today = clock.DayOf(DateOnly.FromDateTime(DateTime.UtcNow));
            var rung = delivery?.Rung ?? 1;
            var card = rung == 1 ? EngineWorksheetCardReader.Read(delivery?.DeliveredWorksheetCardJson) : null;
            var acknowledgedAt = await _context.EngineAcknowledgements.AsNoTracking()
                .Where(a => a.BusinessUnitId == businessUnitId && a.CycleKey == cycle.CycleKey && a.Rung == rung)
                .Select(a => (DateTime?)a.AcknowledgedAt)
                .FirstOrDefaultAsync(ct);

            // A tap is stored at once but reaches the engine on its next run (the daily run,
            // through yesterday). In the MVP it changes the board at once, so the stored tap
            // stands in for the engine's record until the run has taken it in.
            var confirmedDay = delivery?.ConfirmedDay;
            if (open && confirmedDay is null)
            {
                confirmedDay = await _context.EngineConfirmations.AsNoTracking()
                    .Where(c => c.BusinessUnitId == businessUnitId && c.CycleKey == cycle.CycleKey && c.Rung == rung)
                    .Select(c => (int?)c.Day)
                    .FirstOrDefaultAsync(ct);
            }
            var declinedNow = open && await _context.EngineDeclines.AsNoTracking()
                .AnyAsync(d => d.BusinessUnitId == businessUnitId && d.CycleKey == cycle.CycleKey, ct);
            // Declined is failed, as the engine records it (Team.DeclineContent).
            var outcome = cycle.Outcome ?? (declinedNow ? "failed" : null);
            open = outcome is null;

            // Eran's wording of the move, from the first daily message of this rung that
            // carries it (DEVELOPER-GUIDE section 2, rule 3: latch once, never re-read).
            var worded = await _context.EngineFeedItems.AsNoTracking()
                .Where(f => f.BusinessUnitId == businessUnitId && f.Kind == "daily"
                            && f.CycleKey == cycle.CycleKey && f.Rung == rung && f.Prose != null)
                .OrderBy(f => f.Day).ThenBy(f => f.Id)
                .ToListAsync(ct);
            // Cleared once the cycle resolves (rule 3: "clear the latch when the cycle resolves,
            // and empty the card ... so last week's move cannot be read back out of the page").
            // Only wording that passes Eran's guard is latched.
            var latched = !open ? default : worded
                .Select(f => (f.Day, Prose: EranCoachTimelineService.GuardedProse(f)))
                .Where(f => f.Prose is not null)
                .Select(f => (f.Day, Parts: EranCoachTimelineService.DailyParts(f.Prose!)))
                .FirstOrDefault(f => !string.IsNullOrWhiteSpace(f.Parts.OneThing));

            // MVP 90fe06d: the team's own working days, never a run-day index.
            var workingDays = new EngineWorkingDays(_engine.Context, clock.StartDate, clock.WorkingWeek);
            var dayNow = Math.Max(1, today);

            return new EngineActiveActionDto
            {
                BusinessUnitId = businessUnitId,
                CycleKey = cycle.CycleKey,
                IssueFileKey = cycle.IssueFileKey,
                Issue = cycle.Issue,
                Route = delivery?.Route ?? cycle.Route,
                ShareText = cycle.ShareText,
                Rung = delivery?.Rung ?? 1,
                ActionText = delivery?.DeliveredText,
                WorkedIf = delivery?.DeliveredWorkedIf,
                WorksheetCode = delivery?.DeliveredWorksheetCode,
                PhaseRun = delivery?.DeliveredPhaseRun ?? [],
                DeliveredDay = delivery?.DeliveredDay ?? cycle.DeliveredDay,
                ConfirmationDueDay = cycle.ConfirmationDueDay,
                SignalReadDay = cycle.SignalReadDay,
                IsConfirmed = confirmedDay is not null,
                ConfirmedDay = confirmedDay,
                ConfirmationDue = open && confirmedDay is null && today >= cycle.ConfirmationDueDay,
                IsAcknowledged = acknowledgedAt is not null,
                AcknowledgedAt = acknowledgedAt,
                Worksheet = card,
                CanDecline = open && rung == 1 && card?.Route == "content" && confirmedDay is null
                             && card.Contraindications.Count > 0,
                Outcome = outcome,
                // Built by the weekly-flag writer itself, so the two never word it differently.
                WhatCameUp = EngineWeeklyFlagWriter.WhatCameUpFor(cycle),
                SuggestedAction = EngineWeeklyFlagWriter.SuggestedActionFor(cycle, delivery),
                OneThing = latched.Parts?.OneThing,
                SayThis = string.IsNullOrWhiteSpace(latched.Parts?.SayThis) ? null : latched.Parts.SayThis,
                WhatTellsYou = string.IsNullOrWhiteSpace(latched.Parts?.WhatTellsYou) ? null : latched.Parts.WhatTellsYou,
                WordedOnDay = latched.Parts is null ? null : latched.Day,
                WordedWhen = latched.Parts is null ? null : workingDays.Back(dayNow, latched.Day),
                ConfirmedWhen = workingDays.Back(dayNow, confirmedDay),
                ConfirmationDueIn = workingDays.Ahead(dayNow, cycle.ConfirmationDueDay)
            };
        }

        public async Task<List<EngineFeedItemDto>> GetFeedAsync(
            User actor, uint businessUnitId, int take, CancellationToken ct = default)
        {
            var items = await FeedCoreAsync(actor, businessUnitId, take, ct);
            return _planGate == null ? items : await _planGate.FilterFeedAsync(actor.UserId, items, ct);
        }

        private async Task<List<EngineFeedItemDto>> FeedCoreAsync(
            User actor, uint businessUnitId, int take, CancellationToken ct)
        {
            await RequireAccessAsync(actor, businessUnitId, ct);

            var rows = await _context.EngineFeedItems.AsNoTracking()
                .Where(f => f.BusinessUnitId == businessUnitId)
                .OrderByDescending(f => f.Day).ThenByDescending(f => f.Id)
                .Take(Math.Clamp(take, 1, 200))
                .ToListAsync(ct);

            return rows.Select(f => new EngineFeedItemDto
                {
                    FeedKey = f.FeedKey,
                    Day = f.Day,
                    Kind = f.Kind,
                    CycleKey = f.CycleKey,
                    Rung = f.Rung,
                    Situation = f.Situation,
                    NoteType = f.NoteType,
                    Payload = f.PayloadJson,
                    // Only wording that passes Eran's guard; anything else reads as not yet written.
                    Prose = EranCoachTimelineService.GuardedProse(f)
                })
                .ToList();
        }

        public async Task<EngineOutwardConditionDto?> GetOutwardConditionAsync(
            User actor, uint businessUnitId, CancellationToken ct = default)
        {
            var clock = await RequireAccessAsync(actor, businessUnitId, ct);
            var today = clock.DayOf(DateOnly.FromDateTime(DateTime.UtcNow));

            // The current reading: the outward fields ride on the daily message (all six or none),
            // so the newest daily decides whether there is one now. A daily on a day the team was
            // not asked is a row left by an older run (D35) and is passed over.
            var recent = await _context.EngineFeedItems.AsNoTracking()
                .Where(f => f.BusinessUnitId == businessUnitId && f.Kind == "daily" && f.Day <= today)
                .OrderByDescending(f => f.Day).ThenByDescending(f => f.Id)
                .Select(f => new { f.Day, f.PayloadJson })
                .Take(10)
                .ToListAsync(ct);
            var daily = recent.FirstOrDefault(f => clock.IsAskedOn(clock.DateOfDay(f.Day)));
            if (daily is null) return null;

            // The run is through yesterday, so the newest daily is the team's last asked day before
            // today. One older than that is stale (the nightly run has stopped), and a condition
            // read from it is not shown as current.
            var lastAsked = Enumerable.Range(1, 14).Select(back => today - back)
                .FirstOrDefault(d => d >= 1 && clock.IsAskedOn(clock.DateOfDay(d)));
            if (lastAsked > 0 && daily.Day < lastAsked) return null;

            return OutwardFrom(businessUnitId, daily.Day, daily.PayloadJson);
        }

        /// <summary>The outward fields of a daily payload, or null when it carries none.</summary>
        public static EngineOutwardConditionDto? OutwardFrom(uint businessUnitId, int day, string? payloadJson)
        {
            if (string.IsNullOrWhiteSpace(payloadJson)) return null;
            using var doc = JsonDocument.Parse(payloadJson);
            var p = doc.RootElement;
            string? Text(string name) =>
                p.ValueKind == JsonValueKind.Object && p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                    ? v.GetString() : null;

            var condition = Text("outward_condition");
            if (string.IsNullOrWhiteSpace(condition)) return null;

            return new EngineOutwardConditionDto
            {
                BusinessUnitId = businessUnitId,
                Day = day,
                Condition = condition,
                Move = Text("outward_move"),
                GoesTo = Text("outward_goes_to"),
                Strength = Text("outward_strength"),
                DaysAbove = p.TryGetProperty("outward_days_above", out var d) && d.ValueKind == JsonValueKind.Number
                            && d.TryGetInt32(out var n) ? n : null,
                HowYouWillKnow = Text("outward_how_you_will_know")
            };
        }

        public async Task<EngineActiveActionDto?> ConfirmAsync(
            User actor, EngineConfirmRequestDto dto, CancellationToken ct = default)
        {
            var clock = await RequireAccessAsync(actor, dto.BusinessUnitId, ct);

            var cycle = await _context.EngineCycles.AsNoTracking()
                .Include(c => c.Deliveries)
                .FirstOrDefaultAsync(c => c.BusinessUnitId == dto.BusinessUnitId
                                          && c.CycleKey == dto.CycleKey, ct)
                ?? throw new InvalidOperationException("No such cycle for this team.");

            if (cycle.Outcome is not null)
                throw new InvalidOperationException("That cycle has already resolved.");
            if ((await DeclinedKeysAsync(dto.BusinessUnitId, ct)).Contains(cycle.CycleKey))
                throw new InvalidOperationException("This action has been declined, so it can no longer be confirmed.");

            var rung = cycle.Deliveries.Count == 0 ? 1 : cycle.Deliveries.Max(d => d.Rung);

            var already = await _context.EngineConfirmations
                .AnyAsync(c => c.BusinessUnitId == dto.BusinessUnitId
                               && c.CycleKey == dto.CycleKey
                               && c.Rung == rung, ct);

            if (already)
                throw new InvalidOperationException("This rung has already been confirmed.");

            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            _context.EngineConfirmations.Add(new EngineConfirmation
            {
                BusinessUnitId = dto.BusinessUnitId,
                CycleKey = dto.CycleKey,
                Rung = rung,
                ConfirmedDate = today,
                Day = clock.DayOf(today),
                AskedDayIndex = clock.AskedDayIndexOf(today),
                WorkedIfMet = dto.WorkedIfMet,
                ConfirmedByUserId = actor.UserId,
                Source = "manager",
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync(ct);

            // "I did this" is a commitment too: a move confirmed without "I'll do it" first is
            // taken up with it, so it still becomes an Action in Flight on the timeline.
            await EnsureAcknowledgedAsync(dto.BusinessUnitId, cycle.CycleKey, rung, actor.UserId, ct);

            // Nothing goes to the team at this point. The note follows the signal read,
            // which keeps the read clean and lets the note report the result rather than
            // the intention. Not replayed here: today is still being answered, and the daily
            // run takes the tap in from today, as the MVP does the step after the tap.
            return await GetActiveActionAsync(actor, dto.BusinessUnitId, ct);
        }

        /// <summary>
        /// "I'll do it" (MVP /api/acknowledge). Only for the move in front of the manager, and
        /// only once per rung: a second tap, by this manager or another, changes nothing.
        /// Nothing is replayed, because nothing about the engine's reading has changed.
        /// The acknowledgement is what makes the move an Action in Flight on the Team Health
        /// timeline (TeamActionService), so a refused tap leaves no dot.
        /// </summary>
        public async Task<EngineActiveActionDto?> AcknowledgeAsync(
            User actor, EngineAcknowledgeRequestDto dto, CancellationToken ct = default)
        {
            var active = await GetActiveActionAsync(actor, dto.BusinessUnitId, ct)
                ?? throw new InvalidOperationException("There is no action in front of this team to acknowledge.");
            if (!string.Equals(active.CycleKey, dto.CycleKey, StringComparison.Ordinal))
                throw new InvalidOperationException("That is not the action currently in front of this team.");
            if (active.IsConfirmed)
                throw new InvalidOperationException("This action has already been confirmed.");
            if (active.IsAcknowledged) return active;

            await EnsureAcknowledgedAsync(dto.BusinessUnitId, active.CycleKey, active.Rung, actor.UserId, ct);

            return await GetActiveActionAsync(actor, dto.BusinessUnitId, ct);
        }

        // One acknowledgement per rung; the first manager to take it up is the one recorded.
        private async Task EnsureAcknowledgedAsync(uint businessUnitId, string cycleKey, int rung, int userId, CancellationToken ct)
        {
            if (await _context.EngineAcknowledgements.AnyAsync(
                    a => a.BusinessUnitId == businessUnitId && a.CycleKey == cycleKey && a.Rung == rung, ct))
                return;

            var acknowledgement = new EngineAcknowledgement
            {
                BusinessUnitId = businessUnitId,
                CycleKey = cycleKey,
                Rung = rung,
                AcknowledgedByUserId = userId,
                AcknowledgedAt = DateTime.UtcNow
            };
            _context.EngineAcknowledgements.Add(acknowledgement);

            try
            {
                await _context.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Another manager of the team acknowledged in the same moment. Theirs stands.
                _context.Entry(acknowledgement).State = EntityState.Detached;
            }
        }

        public async Task<EngineActiveActionDto?> DeclineAsync(
            User actor, EngineDeclineRequestDto dto, CancellationToken ct = default)
        {
            var clock = await RequireAccessAsync(actor, dto.BusinessUnitId, ct);

            var cycle = await _context.EngineCycles.AsNoTracking()
                .FirstOrDefaultAsync(c => c.BusinessUnitId == dto.BusinessUnitId
                                          && c.CycleKey == dto.CycleKey, ct)
                ?? throw new InvalidOperationException("No such cycle for this team.");

            if (cycle.Outcome is not null)
                throw new InvalidOperationException("That cycle has already resolved.");

            var already = await _context.EngineDeclines
                .AnyAsync(d => d.BusinessUnitId == dto.BusinessUnitId && d.CycleKey == dto.CycleKey, ct);
            if (already)
                throw new InvalidOperationException("This cycle has already been declined.");

            // The engine only declines a content action that is not yet confirmed
            // (Team.DeclineContent), and the MVP refuses anything else up front. Stored
            // anyway, it would be ignored on replay while the manager believed it declined.
            var current = await _context.EngineCycleDeliveries.AsNoTracking()
                .Where(d => d.CycleId == cycle.Id)
                .OrderByDescending(d => d.Rung)
                .FirstOrDefaultAsync(ct);
            if ((current?.Route ?? cycle.Route) != "content")
                throw new InvalidOperationException(
                    "Only a content action can be declined: it is the only kind that states conditions to decline against.");
            var confirmedTap = await _context.EngineConfirmations.AsNoTracking()
                .AnyAsync(c => c.BusinessUnitId == dto.BusinessUnitId && c.CycleKey == dto.CycleKey
                               && c.Rung == (current == null ? 1 : current.Rung), ct);
            if (current?.ConfirmedDay is not null || confirmedTap)
                throw new InvalidOperationException("This action has already been confirmed, so it can no longer be declined.");

            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            // A contraindication is stated and never evaluated: the layer does not know who
            // is in a performance process and must not try to find out. The decline is
            // recorded as a failed cycle with its reason, and succession moves on.
            _context.EngineDeclines.Add(new EngineDecline
            {
                BusinessUnitId = dto.BusinessUnitId,
                CycleKey = dto.CycleKey,
                DeclinedDate = today,
                Day = clock.DayOf(today),
                AskedDayIndex = clock.AskedDayIndexOf(today),
                Reason = dto.Reason,
                DeclinedByUserId = actor.UserId,
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync(ct);

            // Not replayed here (see ConfirmAsync). The card closes now; the engine records
            // the failed cycle on its next run, and another way at it is selected from there.
            return await GetActiveActionAsync(actor, dto.BusinessUnitId, ct);
        }

        /// <summary>Cycles the manager has declined, recorded by the engine yet or not.</summary>
        private async Task<HashSet<string>> DeclinedKeysAsync(uint businessUnitId, CancellationToken ct) =>
            (await _context.EngineDeclines.AsNoTracking()
                .Where(d => d.BusinessUnitId == businessUnitId)
                .Select(d => d.CycleKey)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        public async Task<List<EngineTeamNoteDto>> GetTeamNotesAsync(
            uint businessUnitId, int take, CancellationToken ct = default)
        {
            // No access check: this is what a MEMBER sees, and the caller is the member app,
            // authenticated by its own token. It carries no state, no action and no chart.
            var notes = await _context.EngineTeamNotes.AsNoTracking()
                .Where(n => n.BusinessUnitId == businessUnitId && n.Text != null)
                .OrderByDescending(n => n.Day)
                .Take(Math.Clamp(take, 1, 50))
                .Select(n => new { n.CycleKey, n.Day, n.NoteType, n.Text, n.IssuedAt })
                .ToListAsync(ct);

            // A note is Eran's writing to the whole team: one that breaks a rule is held back
            // until the narrator rewrites it (its feed item is then cleared and phrased again).
            // Checked against its own payload, so the library's words may be echoed.
            var days = notes.Select(n => n.Day).Distinct().ToList();
            var payloads = (await _context.EngineFeedItems.AsNoTracking()
                    .Where(f => f.BusinessUnitId == businessUnitId && f.Kind == "note" && days.Contains(f.Day))
                    .Select(f => new { f.CycleKey, f.Day, f.PayloadJson })
                    .ToListAsync(ct))
                .GroupBy(f => (f.CycleKey, f.Day))
                .ToDictionary(g => g.Key, g => g.First().PayloadJson);

            return notes
                .Where(n => EranGuard.CheckFeedItem("note", null, payloads.GetValueOrDefault((n.CycleKey, n.Day)), n.Text).Passed)
                .Select(n => new EngineTeamNoteDto
                {
                    Day = n.Day,
                    NoteType = n.NoteType,
                    Text = n.Text,
                    IssuedAt = n.IssuedAt
                })
                .ToList();
        }

        /// <summary>
        /// The team must be one this user may see, by the same rule the dashboards use, and
        /// it must have engine settings. Returns its clock, which every answer here needs.
        /// </summary>
        private async Task<EngineTeamClock> RequireAccessAsync(User actor, uint businessUnitId, CancellationToken ct)
        {
            var allowed = await _access.GetAccessibleUnitIdsAsync(actor, ct);
            if (!allowed.Contains(businessUnitId))
                throw new UnauthorizedAccessException("You do not have access to this team.");

            var settings = await _context.EngineTeamSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.BusinessUnitId == businessUnitId, ct)
                ?? throw new InvalidOperationException("This team is not set up for the deterministic layer yet.");

            return new EngineTeamClock(settings.StartDate, settings.WorkingWeek, _engine.Calendar);
        }
    }
}
