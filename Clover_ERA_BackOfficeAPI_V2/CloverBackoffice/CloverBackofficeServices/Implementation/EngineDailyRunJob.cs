using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CloverBackoffice.Services.Implementation
{
    /// <summary>
    /// The daily tick: replays every enabled team through the engine and stores the result.
    ///
    /// Idempotent by construction rather than by care. The replay derives everything from
    /// the facts and every result is upserted on a natural key, so running twice in a day,
    /// re-running after a deploy, or two instances starting at once all produce the same
    /// rows. The advisory lock below is only there to stop two instances doing the same
    /// work at the same moment and wasting it.
    ///
    /// One team failing does not stop the others: a team with a broken setting must not
    /// cost every other team its day.
    /// </summary>
    public class EngineDailyRunJob
    {
        // First key of pg_advisory_xact_lock(int, int); the second is the unit id.
        private const int EngineRunLockNamespace = 16019;

        private readonly ApplicationDbContext _db;
        private readonly IEngineReplayRunner _runner;
        private readonly IEngineTeamProvisioner _provisioner;
        private readonly ILogger<EngineDailyRunJob> _logger;
        private readonly IEngineNarrationQueue? _narration;

        public EngineDailyRunJob(
            ApplicationDbContext db,
            IEngineReplayRunner runner,
            IEngineTeamProvisioner provisioner,
            ILogger<EngineDailyRunJob> logger,
            IEngineNarrationQueue? narration = null)
        {
            _db = db;
            _runner = runner;
            _provisioner = provisioner;
            _logger = logger;
            _narration = narration;
        }

        public async Task RunJobAsync(CancellationToken ct = default)
        {
            // Through yesterday: today is still being answered (EngineRunWindow).
            var through = EngineRunWindow.LastCompleteDay();

            // The engine is on by default: any team without settings gets them here, so a
            // new team is picked up without anyone switching it on. Existing settings —
            // including a team an admin has switched off — are never touched.
            await _provisioner.EnsureAllAsync(ct);

            var units = await TeamsToRunAsync(_db, through, ct);

            if (units.Count == 0)
            {
                _logger.LogInformation("[EngineDailyRun] No enabled team has anyone enrolled. Nothing to do.");
                return;
            }

            _logger.LogInformation("[EngineDailyRun] {Count} team(s) to run through {Through}.", units.Count, through);

            var started = DateTime.UtcNow;
            var ran = 0;
            var skipped = 0;
            var failed = 0;

            foreach (var unitId in units)
            {
                if (ct.IsCancellationRequested) break;

                var locked = false;
                try
                {
                    // A SESSION lock, not a transaction one: the run opens its own
                    // transaction to store the results, and Npgsql forbids nesting. The
                    // connection is opened explicitly so the lock and the run share it.
                    await _db.Database.OpenConnectionAsync(ct);

                    // Try, do not wait. Another instance already replaying this team is
                    // doing the same work, so there is nothing to gain by queueing.
                    // The column is named "Value" because EF composes FirstAsync over this
                    // SQL and reads that column; without the alias every team failed here.
                    locked = await _db.Database
                        .SqlQuery<bool>($"SELECT pg_try_advisory_lock({EngineRunLockNamespace}, {(int)unitId}) AS \"Value\"")
                        .FirstAsync(ct);

                    if (!locked)
                    {
                        _logger.LogInformation("[EngineDailyRun] Unit {Unit} is already running elsewhere. Skipped.", unitId);
                        skipped += 1;
                        continue;
                    }

                    var result = await _runner.RunAsync(unitId, through, ct);
                    ran += 1;

                    // Messages that went out as Eran's fixed card get their one nightly retry,
                    // queued after the run's own narration pass.
                    _narration?.EnqueueFallbackRetry(unitId);

                    _logger.LogInformation(
                        "[EngineDailyRun] Unit {Unit}: {Asked} asked days, state {State}, {Deliveries} deliveries, {Ms} ms.",
                        unitId, result.AskedDays, result.State ?? "calibrating", result.Deliveries, result.ElapsedMs);
                }
                catch (Exception ex)
                {
                    failed += 1;
                    _logger.LogError(ex, "[EngineDailyRun] Unit {Unit} failed. Other teams continue.", unitId);
                }
                finally
                {
                    // A session lock outlives the statement that took it, so it has to be
                    // given back explicitly — including when the run threw.
                    if (locked)
                    {
                        try
                        {
                            await _db.Database
                                .SqlQuery<bool>($"SELECT pg_advisory_unlock({EngineRunLockNamespace}, {(int)unitId}) AS \"Value\"")
                                .FirstAsync(ct);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "[EngineDailyRun] Could not release the lock on unit {Unit}.", unitId);
                        }
                    }

                    await _db.Database.CloseConnectionAsync();
                }
            }

            _logger.LogInformation(
                "[EngineDailyRun] Finished: {Ran} replayed, {Skipped} skipped, {Failed} failed, {Ms} ms total.",
                ran, skipped, failed, (int)(DateTime.UtcNow - started).TotalMilliseconds);
        }

        /// <summary>
        /// The enabled teams whose engine clock has started, leaving out any team nobody has
        /// ever enrolled in. Such a team has nothing to read, yet a run would still write a
        /// calibrating message for each of its days, and Eran would phrase every one of them.
        /// A team whose members have all left keeps running: it has a history to carry.
        /// </summary>
        public static Task<List<uint>> TeamsToRunAsync(ApplicationDbContext db, DateOnly through, CancellationToken ct = default) =>
            db.EngineTeamSettings.AsNoTracking()
                .Where(s => s.IsEnabled
                            && s.StartDate <= through
                            && db.EngineTeamMemberships.Any(m => m.BusinessUnitId == s.BusinessUnitId))
                .OrderBy(s => s.BusinessUnitId)
                .Select(s => s.BusinessUnitId)
                .ToListAsync(ct);
    }
}
