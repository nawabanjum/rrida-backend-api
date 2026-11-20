using CloverBackoffice.Infrastructure.Models.DeterministicLayer;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CloverBackoffice.Services.Implementation
{
    /// <summary>
    /// Gives every team engine settings, so the engine is on by default and nobody has to
    /// switch it on team by team.
    ///
    /// <b>It only ever inserts.</b> A row that already exists is left exactly as it is, which
    /// is what makes an admin's own settings win: once someone has set a team's working week
    /// or turned the engine off for it, no later run can undo that.
    ///
    /// The defaults come from the kit's own calendar, not from a number typed in here, so a
    /// change upstream reaches this without a code edit.
    /// </summary>
    public sealed class EngineTeamProvisioner : IEngineTeamProvisioner
    {
        private readonly ApplicationDbContext _context;
        private readonly IRridaEngineContextFactory _engine;
        private readonly ILogger<EngineTeamProvisioner> _logger;

        public EngineTeamProvisioner(
            ApplicationDbContext context,
            IRridaEngineContextFactory engine,
            ILogger<EngineTeamProvisioner> logger)
        {
            _context = context;
            _engine = engine;
            _logger = logger;
        }

        public async Task<bool> EnsureAsync(uint businessUnitId, CancellationToken ct = default)
        {
            var exists = await _context.EngineTeamSettings
                .AnyAsync(s => s.BusinessUnitId == businessUnitId, ct);

            // Already set up, by an admin or by an earlier run. Nothing to do, and nothing
            // may be overwritten.
            if (exists) return false;

            var unitExists = await _context.BusinessUnits
                .AnyAsync(b => b.Id == businessUnitId && b.IsDel != true, ct);
            if (!unitExists) return false;

            var startDate = await EarliestAnswerDateAsync(businessUnitId, ct)
                            ?? DateOnly.FromDateTime(DateTime.UtcNow);

            _context.EngineTeamSettings.Add(NewSettings(businessUnitId, startDate));
            await _context.SaveChangesAsync(ct);

            _logger.LogInformation(
                "[Provision] Unit {Unit} given default engine settings: start {Start}, week {Week}, enabled.",
                businessUnitId, startDate, DefaultWorkingWeek);

            return true;
        }

        public async Task<int> EnsureAllAsync(CancellationToken ct = default)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            var missing = await _context.BusinessUnits.AsNoTracking()
                .Where(b => b.IsDel != true
                            && !_context.EngineTeamSettings.Any(s => s.BusinessUnitId == b.Id))
                .Select(b => b.Id)
                .ToListAsync(ct);

            if (missing.Count == 0) return 0;

            // A team that has been answering already has a clock: its first answer is day
            // one. Starting such a team from today would renumber the history it already has.
            var firstAnswers = await _context.EngineAnswers.AsNoTracking()
                .Where(a => missing.Contains(a.BusinessUnitId))
                .GroupBy(a => a.BusinessUnitId)
                .Select(g => new { UnitId = g.Key, First = g.Min(a => a.AnswerDate) })
                .ToDictionaryAsync(x => x.UnitId, x => x.First, ct);

            foreach (var unitId in missing)
                _context.EngineTeamSettings.Add(
                    NewSettings(unitId, firstAnswers.GetValueOrDefault(unitId, today)));

            await _context.SaveChangesAsync(ct);

            _logger.LogInformation("[Provision] {Count} team(s) given default engine settings.", missing.Count);
            return missing.Count;
        }

        /// <summary>
        /// The working week the kit names as its default. Read once per call rather than
        /// stored, so it follows the kit if that ever changes.
        /// </summary>
        private string DefaultWorkingWeek => _engine.Calendar.DefaultSchedule;

        private EngineTeamSettings NewSettings(uint businessUnitId, DateOnly startDate) => new()
        {
            BusinessUnitId = businessUnitId,
            StartDate = startDate,
            WorkingWeek = DefaultWorkingWeek,

            // On by default. A team that starts answering is picked up by the next run
            // without anyone switching it on; an admin can still turn it off.
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow
        };

        private async Task<DateOnly?> EarliestAnswerDateAsync(uint businessUnitId, CancellationToken ct)
        {
            var any = await _context.EngineAnswers.AsNoTracking()
                .Where(a => a.BusinessUnitId == businessUnitId)
                .OrderBy(a => a.AnswerDate)
                .Select(a => (DateOnly?)a.AnswerDate)
                .FirstOrDefaultAsync(ct);

            return any;
        }
    }
}
