using CloverBackoffice.Infrastructure.DTO;
using System.Text.Json;
using CloverBackoffice.Infrastructure.Models.DeterministicLayer;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Rrida.Engine;
using Rrida.State;

namespace CloverBackoffice.Services.Implementation
{
    /// <summary>
    /// Persists what a run produced: the daily state records, the cycles with a row per
    /// rung, the feed, and the notes that are due.
    ///
    /// Every write is an UPSERT on the record's own natural key. That is what makes a
    /// replay safe to repeat: re-running a day updates its row rather than adding a second
    /// one, so a redeploy, a retry or a manual re-run all land in the same place.
    /// </summary>
    public sealed class EngineResultStore : IEngineResultStore
    {
        private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

        private readonly ApplicationDbContext _context;
        private readonly ILogger<EngineResultStore> _logger;

        public EngineResultStore(ApplicationDbContext context, ILogger<EngineResultStore> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<int> SaveRunAsync(
            uint businessUnitId,
            EngineContext ctx,
            Team team,
            BufferingStateSink sink,
            int throughDay,
            CancellationToken ct = default)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(ct);

            // A cycle is not in the history until it resolves, and a replay that stopped
            // mid-cycle must still store every open one: the one in front of the manager, and
            // (D29) the confirmed ones waiting for their reading or for rung two.
            var cycles = team.CycleHistory
                .Concat(team.ActiveCycle is null ? [] : new[] { team.ActiveCycle })
                .Concat(team.AwaitingRead)
                .Concat(team.PendingRungTwo)
                .GroupBy(c => c.Id, StringComparer.Ordinal)
                .Select(g => g.Last())
                .ToList();

            var written = await UpsertStateAsync(businessUnitId, sink, ct);
            await UpsertCyclesAsync(businessUnitId, ctx, cycles, throughDay, ct);
            await UpsertFeedAsync(businessUnitId, team, throughDay, ct);

            await _context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            _logger.LogInformation(
                "Engine run stored for unit {Unit}: {Days} state records, {Cycles} cycles, {Feed} feed items.",
                businessUnitId, sink.Records.Count, cycles.Count, team.Feed.Count);

            return written;
        }

        private async Task<int> UpsertStateAsync(uint businessUnitId, BufferingStateSink sink, CancellationToken ct)
        {
            if (sink.Records.Count == 0) return 0;

            var days = sink.Records.Select(r => r.Day).ToList();
            var existing = await _context.EngineStateDaily
                .Where(s => s.BusinessUnitId == businessUnitId && days.Contains(s.Day))
                .ToDictionaryAsync(s => s.Day, ct);

            foreach (var record in sink.Records)
            {
                if (!existing.TryGetValue(record.Day, out var row))
                {
                    row = new EngineStateDaily
                    {
                        BusinessUnitId = businessUnitId,
                        Day = record.Day,
                        Schema = record.Schema,
                        ConfigId = record.ConfigId,
                        Vertical = record.Vertical,
                        Horizontal = record.Horizontal,
                        RecordJson = "{}"
                    };
                    _context.EngineStateDaily.Add(row);
                    existing[record.Day] = row;
                }

                row.Schema = record.Schema;
                row.ConfigId = record.ConfigId;
                row.Vertical = record.Vertical;
                row.Horizontal = record.Horizontal;
                row.HeadwindsFlag = record.HeadwindsFlag;
                row.Limb = record.Opening?.Limb;
                row.OpenedDay = record.Opening?.Day;
                row.ConcentrationReading = record.ConcentrationReading;
                row.ConcentrationTopShare = record.ConcentrationTopShare;
                row.ClassifierZ = record.ClassifierZ;
                row.ClassifierHorizontal = record.ClassifierHorizontal;
                row.WatchExposed = record.WatchExposed;
                row.RecordJson = JsonSerializer.Serialize(record, Json);
                row.WrittenAt = DateTime.UtcNow;
            }

            return sink.Records.Count;
        }

        private async Task UpsertCyclesAsync(
            uint businessUnitId, EngineContext ctx, IReadOnlyList<Cycle> cycles, int throughDay, CancellationToken ct)
        {
            var existing = await _context.EngineCycles
                .Include(c => c.Deliveries)
                .Where(c => c.BusinessUnitId == businessUnitId)
                .ToDictionaryAsync(c => c.CycleKey, ct);

            // A cycle an earlier run stored and this one no longer produces did not happen:
            // left in place it reads as an action still in flight, can be put in front of the
            // manager and can be confirmed. As with the feed, only up to the day this run
            // covers; anything later belongs to a longer run and is left for that.
            var produced = cycles.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
            var stale = existing.Values
                .Where(r => !produced.Contains(r.CycleKey) && r.SelectedDay <= throughDay)
                .ToList();
            if (stale.Count > 0)
            {
                _context.EngineCycles.RemoveRange(stale);   // deliveries go with them (cascade)
                foreach (var row in stale) existing.Remove(row.CycleKey);
                _logger.LogInformation("[Engine] Unit {Unit}: removed {Count} cycle(s) this run no longer produces: {Keys}",
                    businessUnitId, stale.Count, string.Join(", ", stale.Select(r => r.CycleKey)));
            }

            foreach (var cycle in cycles)
            {
                if (!existing.TryGetValue(cycle.Id, out var row))
                {
                    row = new EngineCycle
                    {
                        BusinessUnitId = businessUnitId,
                        CycleKey = cycle.Id,
                        IssueFileKey = cycle.IssueFile
                    };
                    _context.EngineCycles.Add(row);
                    existing[cycle.Id] = row;
                }

                row.CycleIndex = cycle.Index;
                row.IssueFileKey = cycle.IssueFile;
                row.Issue = cycle.Issue;
                row.TriggeringChipKey = cycle.TriggeringChipId;
                row.TriggeringQuestionKey = cycle.TriggeringQuestionId;
                row.TallyActionKey = cycle.TallyActionId;
                row.Route = cycle.Routing.Route;
                // Proportions, never counts. The exact number is what turns "three people
                // said this" into working out which three. The engine's own words for it,
                // the ones it hands Eran (Route.ShareInWords); Routing.ShareText is the count.
                row.ShareText = Route.ShareInWords(cycle.Routing.Share, cycle.Routing.Threshold);
                row.SelectedDay = cycle.SelectedDay;
                row.DeliveredDay = cycle.DeliveredDay;
                row.ConfirmationDueDay = cycle.ConfirmationDueDay;
                row.SignalReadDay = cycle.SignalReadDay;
                row.ResolvedDay = cycle.ResolvedDay;
                row.Outcome = cycle.Outcome;
                row.Rung2Skipped = cycle.Rung2Skipped;
                row.Rung2SkipReason = cycle.Rung2SkipReason;
                row.DeclinedDay = cycle.Declined?.Day;
                row.DeclineReason = cycle.Declined?.Reason;
                row.WrittenAt = DateTime.UtcNow;

                UpsertDeliveries(ctx, cycle, row);
            }
        }

        /// <summary>
        /// A row per rung, each holding what that rung actually delivered.
        ///
        /// The engine's cycle mutates in place when it ladders, so rung one's record only
        /// survives in <c>Cycle.RungOneRecord</c> and the delivered text has to be asked for
        /// by rung. Reading the cycle's current action for a past rung is the reporting bug
        /// this shape exists to prevent.
        /// </summary>
        private static void UpsertDeliveries(EngineContext ctx, Cycle cycle, EngineCycle row)
        {
            var rungs = new List<int> { 1 };
            if (cycle.Rung >= 2) rungs.Add(2);

            // A rung the cycle no longer reaches in this run was never delivered.
            foreach (var gone in row.Deliveries.Where(d => !rungs.Contains(d.Rung)).ToList())
                row.Deliveries.Remove(gone);

            foreach (var rung in rungs)
            {
                var delivery = row.Deliveries.FirstOrDefault(d => d.Rung == rung);
                if (delivery is null)
                {
                    delivery = new EngineCycleDelivery { Rung = rung, ActionKey = string.Empty };
                    row.Deliveries.Add(delivery);
                }

                var isCurrent = rung == cycle.Rung;
                var rungOne = cycle.RungOneRecord;

                delivery.ActionKey = rung == 1
                    ? rungOne?.ActionId ?? cycle.Rung1ActionId
                    : cycle.Rung2ActionId;
                delivery.Route = rung == 1 ? cycle.Routing.Route : "structural";
                delivery.DeliveredText = Route.DeliveredActionText(ctx, cycle, rung);
                delivery.DeliveredWorksheetCode = Route.DeliveredWorksheet(cycle, rung)?.Code;
                delivery.DeliveredPhaseRun = rung == 1
                    ? [.. cycle.Routing.Phases.Select(p => p.Id)]
                    : [];
                delivery.DeliveredWorksheetCardJson = rung == 1 ? WorksheetCardJson(cycle.Routing) : null;

                // Where a prerequisite substitution fired, the condition the manager
                // confirms is the substitution's, not the action's own (spec 5.1.4).
                delivery.DeliveredWorkedIf = isCurrent
                    ? Route.DeliveredWorkedIf(ctx, cycle)
                    : delivery.DeliveredWorkedIf;

                // Rung one went out the day the cycle was selected; the cycle's DeliveredDay
                // moves to the rung-two day when it ladders (Team.AdvanceToRungTwo), so it is
                // only rung two's day once there is a rung two.
                delivery.DeliveredDay = rung == 1 ? cycle.SelectedDay : cycle.DeliveredDay;

                if (rung == 1 && rungOne is not null)
                {
                    delivery.ConfirmedDay = rungOne.ConfirmedDay;
                    delivery.WorkedIfMet = rungOne.WorkedIfMet;
                    delivery.SignalReadDay = rungOne.SignalRead?.Day;
                    delivery.SignalMoved = rungOne.SignalRead?.Moved;
                    delivery.HowRead = rungOne.SignalRead?.HowRead;
                }
                else if (isCurrent)
                {
                    delivery.ConfirmedDay = cycle.ConfirmedDay;
                    delivery.WorkedIfMet = cycle.WorkedIfMet;
                    delivery.SignalReadDay = cycle.SignalRead?.Day ?? cycle.SignalReadDay;
                    delivery.SignalMoved = cycle.SignalRead?.Moved;
                    delivery.HowRead = cycle.SignalRead?.HowRead;
                }

                delivery.Outcome = isCurrent ? cycle.Outcome : delivery.Outcome;
            }
        }

        private async Task UpsertFeedAsync(uint businessUnitId, Team team, int throughDay, CancellationToken ct)
        {
            // Keyed on what the message IS, not on the engine's running number. "F12" means
            // the twelfth message of this run, so one extra message early in a re-run would
            // shift every later key and attach Eran's wording to the wrong message.
            var items = new Dictionary<string, FeedItem>(StringComparer.Ordinal);
            foreach (var item in team.Feed)
            {
                var key = FeedKeyOf(item);
                var unique = key;
                for (var n = 2; items.ContainsKey(unique); n += 1) unique = $"{key}#{n}";
                items[unique] = item;
            }

            var existing = await _context.EngineFeedItems
                .Where(f => f.BusinessUnitId == businessUnitId && f.Day <= throughDay)
                .ToDictionaryAsync(f => f.FeedKey, ct);

            // The manager's own actions, reported on the weekly and monthly messages as part of
            // the team's progress. Added here, after the run, so the engine is untouched.
            var activity = await ManagerActivityAsync(businessUnitId, ct);

            foreach (var (key, item) in items)
            {
                var payload = JsonSerializer.Serialize(WithManagerActivity(item, activity), Json);
                if (!existing.TryGetValue(key, out var row))
                {
                    row = new EngineFeedItem
                    {
                        BusinessUnitId = businessUnitId,
                        FeedKey = key,
                        Kind = item.Kind,
                        PayloadJson = payload
                    };
                    _context.EngineFeedItems.Add(row);
                }
                else if (row.PayloadJson != payload || row.Kind != item.Kind)
                {
                    // The engine now says something different, so wording written for the
                    // old payload no longer describes it. Cleared, to be phrased again.
                    row.Prose = null;
                    row.ProseGeneratedAt = null;
                    row.GuardOutcome = null;
                    row.PromptFingerprint = null;
                }

                row.Day = item.Day;
                row.Kind = item.Kind;
                row.CycleKey = item.CycleId;
                row.Rung = item.Rung;
                row.Situation = item.Situation;
                row.NoteType = item.NoteType;
                // Flat keys, no class instances: nothing downstream needs the engine to
                // read this back.
                row.PayloadJson = payload;
                row.WrittenAt = DateTime.UtcNow;
            }

            // The run rebuilds every day up to throughDay, so a message it did not produce
            // is one the engine no longer stands behind.
            var stale = existing.Values.Where(r => !items.ContainsKey(r.FeedKey)).ToList();
            if (stale.Count > 0) _context.EngineFeedItems.RemoveRange(stale);
        }

        private sealed record ManagerActivity(DateOnly StartDate, List<ManagerActionEngineActivity.Fact> Actions);

        /// <summary>The team's manual actions and its engine start date; null when there is nothing to add.</summary>
        private async Task<ManagerActivity?> ManagerActivityAsync(uint businessUnitId, CancellationToken ct)
        {
            var startDate = await _context.EngineTeamSettings.AsNoTracking()
                .Where(s => s.BusinessUnitId == businessUnitId)
                .Select(s => (DateOnly?)s.StartDate)
                .FirstOrDefaultAsync(ct);
            if (startDate is null) return null;

            var actions = await ManagerActionEngineActivity.LoadAsync(_context, businessUnitId, ct);
            return actions.Count == 0 ? null : new ManagerActivity(startDate.Value, actions);
        }

        /// <summary>
        /// The payload to store: the engine's own, plus, on a weekly or monthly message with
        /// something to report, what the manager did with their own actions in that period.
        /// Weekly: the seven days ending on the reading day. Monthly: the calendar month to date.
        /// </summary>
        private static Dictionary<string, object?> WithManagerActivity(FeedItem item, ManagerActivity? a)
        {
            if (a is null || item.Kind is not ("weekly" or "monthly"))
                return item.Payload;

            var to = a.StartDate.AddDays(item.Day - 1);
            var weekly = item.Kind == "weekly";
            var from = weekly ? to.AddDays(-6) : new DateOnly(to.Year, to.Month, 1);
            var rows = ManagerActionEngineActivity.ForPeriod(a.Actions, from, to);
            if (rows is null) return item.Payload;

            return new Dictionary<string, object?>(item.Payload, StringComparer.Ordinal)
            {
                [weekly ? ManagerActionEngineActivity.WeeklyKey : ManagerActionEngineActivity.MonthlyKey] = rows
            };
        }

        /// <summary>
        /// The worksheet part of the MVP's action card, taken from the engine's own routing as
        /// src/server.js does: worksheets and the conditions only on the content route, the run
        /// minutes and run kind on any rung-one move that has them, and a substitution wherever
        /// the worksheet choice made one. Null when there is nothing to show.
        /// </summary>
        public static string? WorksheetCardJson(Routing? routing)
        {
            if (routing is null) return null;
            var isContent = routing.Route == "content";
            var card = new EngineWorksheetCard
            {
                Route = routing.Route,
                Worksheets = isContent
                    ? routing.WorksheetsDelivered
                        .Select(w => new EngineWorksheetRef { Code = w.Code, Title = w.Title, Formerly = w.Formerly })
                        .ToList()
                    : [],
                RunMinutesText = routing.RunMinutes is { } minutes ? RunMinutesText(minutes) : null,
                RunKind = routing.RunKind,
                OutsideTheSession = routing.RunMinutes?.Async
                    .Select(p => new EngineWorksheetAsyncPiece { Title = p.Title, When = p.TimingNote })
                    .ToList() ?? [],
                Contraindications = isContent ? routing.Contraindications.Select(c => c.Text).ToList() : [],
                Substitution = routing.WorksheetChoice?.Substitution is { } sub
                    ? new EngineWorksheetSubstitution { WantedTitle = sub.WantedTitle, Unmet = [.. sub.Unmet], Reason = sub.Reason }
                    : null
            };

            var empty = card.Worksheets.Count == 0 && card.RunMinutesText is null && card.Substitution is null;
            return empty ? null : JsonSerializer.Serialize(card, Json);
        }

        /// <summary>The MVP's runMinutes.text (src/engine/worksheets.js runMinutes), word for word.</summary>
        public static string RunMinutesText(Worksheets.RunMinutes minutes)
        {
            static string N(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            var session = minutes.Low == minutes.High ? $"{N(minutes.Low)} minutes" : $"{N(minutes.Low)} to {N(minutes.High)} minutes";
            return session
                + (minutes.Async.Count > 0 ? $", plus {minutes.Async.Count} piece(s) of work outside the session" : "")
                + (minutes.Untimed.Count > 0 ? $", plus {minutes.Untimed.Count} session phase(s) the library does not time" : "");
        }

        /// <summary>
        /// "{day}.{kind}" plus the cycle and rung where a message belongs to one, e.g. "12.d",
        /// "19.w", "23.o.C2.1". At most one daily, weekly or monthly message a day, and one
        /// outcome and one note per reading, so the key is unique without the running number.
        /// </summary>
        private static string FeedKeyOf(FeedItem item)
        {
            var kind = item.Kind switch
            {
                "daily" => "d",
                "weekly" => "w",
                "monthly" => "m",
                "outcome" => "o",
                "note" => "n",
                "escalation" => "e",
                "cross_team" => "x",
                "cross_team_note" => "xn",
                _ => item.Kind
            };
            var key = $"{item.Day}.{kind}";
            if (item.CycleId is not null) key += $".{item.CycleId}";
            if (item.Rung is not null) key += $".{item.Rung}";
            return key;
        }
    }
}
