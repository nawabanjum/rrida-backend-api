using System.Text;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models.DeterministicLayer;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using static CloverBackoffice.Services.Implementation.EngineWorkbookReader;

namespace CloverBackoffice.Services.Implementation
{
    /// <summary>
    /// TEST DATA: keeps a fixed set of test teams answering every day without anyone
    /// opening the member app.
    ///
    /// The workbook (same layout EngineWorkbookReader reads, one sheet per team) is the
    /// source of the answers. Each configured team is matched to a sheet by team name,
    /// or by its position in BusinessUnitIds when no sheet carries its name. Every member
    /// on that sheet is a dummy member with a fixed token, and checks in exactly as the
    /// app does: enrol with the team code, read today, answer. Nothing is written around
    /// the check-in service, so every rule the app meets applies here too.
    ///
    /// Each member's answer is a random workbook row asked the same question as today
    /// (so its chip labels belong to today's chip set), or any row when none was.
    /// </summary>
    public sealed class EngineDummyCheckInService : IEngineDummyCheckInService
    {
        private const string TokenPrefix = "rrida-dummy";
        private const int MaxChips = 3;

        private readonly ApplicationDbContext _context;
        private readonly IEngineCheckInService _checkIn;
        private readonly IRridaEngineContextFactory _engine;
        private readonly IHostEnvironment _hostEnvironment;
        private readonly IOptionsMonitor<EngineDummyCheckInSettings> _settings;
        private readonly ILogger<EngineDummyCheckInService> _logger;

        public EngineDummyCheckInService(
            ApplicationDbContext context,
            IEngineCheckInService checkIn,
            IRridaEngineContextFactory engine,
            IHostEnvironment hostEnvironment,
            IOptionsMonitor<EngineDummyCheckInSettings> settings,
            ILogger<EngineDummyCheckInService> logger)
        {
            _context = context;
            _checkIn = checkIn;
            _engine = engine;
            _hostEnvironment = hostEnvironment;
            _settings = settings;
            _logger = logger;
        }

        public async Task<EngineDummyCheckInResultDto> RunAsync(CancellationToken ct = default)
        {
            var settings = _settings.CurrentValue;
            var result = new EngineDummyCheckInResultDto
            {
                Date = DateOnly.FromDateTime(DateTime.UtcNow),
                Enabled = settings.Enabled
            };

            if (!settings.Enabled)
            {
                result.Message = "EngineDummyCheckIn is disabled. Nothing was done.";
                return result;
            }

            var unitIds = ParseIds(settings.BusinessUnitIds);
            if (unitIds.Count == 0)
            {
                result.Message = "No team ids are configured in EngineDummyCheckIn:BusinessUnitIds. Nothing was done.";
                return result;
            }

            var path = settings.ExcelFilePath;
            if (!Path.IsPathRooted(path)) path = Path.Combine(_hostEnvironment.ContentRootPath, path);
            if (!File.Exists(path))
            {
                result.Message = $"The workbook was not found at {path}.";
                _logger.LogWarning("[EngineDummyCheckIn] Workbook not found at {Path}.", path);
                return result;
            }

            List<Sheet> sheets;
            await using (var stream = File.OpenRead(path))
            {
                sheets = Read(stream).Where(s => s.Error is null && s.Rows.Count > 0).ToList();
            }

            if (sheets.Count == 0)
            {
                result.Message = "The workbook has no usable team sheet.";
                return result;
            }

            var units = await _context.BusinessUnits.AsNoTracking()
                .Where(b => unitIds.Contains(b.Id) && b.IsDel != true)
                .Select(b => new { b.Id, b.Name, b.BusinessUnitShortCode })
                .ToListAsync(ct);

            for (var i = 0; i < unitIds.Count; i += 1)
            {
                var team = new EngineDummyCheckInTeamResultDto { BusinessUnitId = unitIds[i] };
                result.Teams.Add(team);

                var unit = units.FirstOrDefault(u => u.Id == unitIds[i]);
                if (unit is null)
                {
                    team.Status = "NotFound";
                    continue;
                }

                team.TeamName = unit.Name;
                if (string.IsNullOrWhiteSpace(unit.BusinessUnitShortCode))
                {
                    team.Status = "NoJoinCode";
                    continue;
                }

                var sheet = sheets.FirstOrDefault(s => string.Equals(s.TeamName?.Trim(), unit.Name?.Trim(), StringComparison.OrdinalIgnoreCase))
                            ?? sheets[i % sheets.Count];
                team.SheetName = sheet.SheetName;

                try
                {
                    await RunTeamAsync(unit.Id, unit.BusinessUnitShortCode, sheet, settings.ParticipationPercent, team, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "[EngineDummyCheckIn] Team {Unit} failed.", unit.Id);
                    team.Errors.Add(ex.Message);
                }

                _logger.LogInformation(
                    "[EngineDummyCheckIn] Team {Unit} ({Sheet}): {Status}, submitted {Submitted}, already answered {Already}, errors {Errors}.",
                    unit.Id, sheet.SheetName, team.Status, team.CheckInsSubmitted, team.AlreadyAnswered, team.Errors.Count);
            }

            result.Message = $"Submitted {result.Teams.Sum(t => t.CheckInsSubmitted)} check-ins across {result.Teams.Count} teams.";
            return result;
        }

        private async Task RunTeamAsync(
            uint unitId, string teamCode, Sheet sheet, int participationPercent,
            EngineDummyCheckInTeamResultDto team, CancellationToken ct)
        {
            // The team is asked on the ninety-question rotation (D34). The workbook was written for
            // the demo's six, so most of its chip labels do not belong to today's question;
            // PickAnswer then falls back to the track's pool and today's own offered chips, so every
            // answer it sends is one the app could have sent today.
            var rotation = _engine.Context.Rotation;

            // The sheet's sections grouped by the question their Day No. was asked, so a
            // pick for today's question carries chip labels from today's chip set.
            var byQuestion = new Dictionary<(string Track, int Key), List<Section>>();
            var byTrack = new Dictionary<string, List<Section>>(StringComparer.Ordinal);
            foreach (var row in sheet.Rows)
            {
                foreach (var (track, section) in new[] { ("individual", row.Individual), ("team", row.Team) })
                {
                    if (section is null) continue;
                    Bucket(byTrack, track).Add(section);

                    var key = rotation.QuestionIdForDay(row.Day, track);
                    if (key is not null) Bucket(byQuestion, (track, key.Value)).Add(section);
                }
            }

            var customAnswers = sheet.Rows
                .Select(r => r.CustomAnswer)
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Select(a => a!)
                .ToList();

            var members = sheet.Rows.Select(r => r.Member).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            team.Members = members.Count;

            foreach (var member in members)
            {
                ct.ThrowIfCancellationRequested();

                if (participationPercent < 100 && Random.Shared.Next(100) >= participationPercent)
                {
                    team.SkippedByParticipation += 1;
                    continue;
                }

                var token = TokenFor(unitId, member);
                try
                {
                    await _checkIn.EnrolAsync(new EngineEnrolRequestDto { TeamCode = teamCode, MemberToken = token }, ct);

                    var today = await _checkIn.TodayAsync(token, unitId, ct);
                    if (!today.IsAskedToday)
                    {
                        // The same for every member of the team.
                        team.Status = "NotAskedToday";
                        return;
                    }

                    var pending = today.Questions.Where(q => !q.AlreadyAnswered).ToList();
                    if (pending.Count == 0)
                    {
                        team.AlreadyAnswered += 1;
                        continue;
                    }

                    var individual = pending.FirstOrDefault(q => q.Track == "individual");
                    var first = individual ?? pending[0];
                    var firstAnswer = PickAnswer(first, byQuestion, byTrack);

                    var dto = new EngineAnswerRequestDto
                    {
                        MemberToken = token,
                        BusinessUnitId = unitId,
                        Track = first.Track,
                        Level = firstAnswer.Level,
                        Chips = firstAnswer.Chips,
                        FreeText = firstAnswer.FreeText
                    };

                    // Individual first, with the team answer saved alongside, as the app does.
                    var teamQuestion = individual is null ? null : pending.FirstOrDefault(q => q.Track == "team");
                    if (teamQuestion is not null) dto.Team = PickAnswer(teamQuestion, byQuestion, byTrack);

                    // The custom question goes with the first save of the day only.
                    if (individual is not null && today.CustomQuestion is not null)
                    {
                        dto.CustomQuestion = new EngineCustomAnswerDto
                        {
                            CustomDailyQuestionId = today.CustomQuestion.CustomDailyQuestionId,
                            FreeText = customAnswers.Count == 0 ? null : customAnswers[Random.Shared.Next(customAnswers.Count)],
                            Skipped = customAnswers.Count == 0
                        };
                    }

                    await _checkIn.SubmitAsync(dto, ct);
                    team.CheckInsSubmitted += 1;
                }
                catch (InvalidOperationException ex)
                {
                    // One context serves every member of the run: a refused save must not
                    // leave its rows tracked for the next member's save.
                    _context.ChangeTracker.Clear();
                    team.Errors.Add($"{member}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// A random workbook answer for this question: sentiment, chip labels turned into
        /// today's chip ids, and text. Never leaves without a reason, which the check-in
        /// requires.
        /// </summary>
        private static EngineTrackAnswerDto PickAnswer(
            EngineTodayQuestionDto question,
            Dictionary<(string Track, int Key), List<Section>> byQuestion,
            Dictionary<string, List<Section>> byTrack)
        {
            if (!byQuestion.TryGetValue((question.Track, question.QuestionKey), out var pool) || pool.Count == 0)
                byTrack.TryGetValue(question.Track, out pool);
            if (pool is { Count: 0 }) pool = null;

            var section = pool?[Random.Shared.Next(pool.Count)];
            var level = section?.Sentiment ?? "yellow";
            var offered = question.Chips.TryGetValue(EngineWorkbookImporter.PolarityOf(level), out var set)
                ? set
                : new List<RefChipDto>();

            var chips = (section?.Chips ?? [])
                .Select(label => offered.FirstOrDefault(c => string.Equals(c.Text.Trim(), label, StringComparison.OrdinalIgnoreCase)))
                .Where(c => c is not null)
                .Select(c => c!.ChipKey)
                .Distinct(StringComparer.Ordinal)
                .Take(MaxChips)
                .ToList();

            var text = section?.Text;
            if (chips.Count == 0 && string.IsNullOrWhiteSpace(text))
            {
                if (offered.Count > 0) chips.Add(offered[Random.Shared.Next(offered.Count)].ChipKey);
                else text = "No specific reason.";
            }

            return new EngineTrackAnswerDto { Level = level, Chips = chips, FreeText = text };
        }

        /// <summary>
        /// A fixed token per team and workbook member, so the same dummy member answers
        /// every day and the engine sees a stable roster.
        /// </summary>
        public static string TokenFor(uint unitId, string member)
        {
            var slug = new StringBuilder();
            foreach (var ch in member.Trim().ToLowerInvariant())
                slug.Append(char.IsLetterOrDigit(ch) ? ch : '-');
            return $"{TokenPrefix}-{unitId}-{slug}";
        }

        public static List<uint> ParseIds(string? csv) =>
            (csv ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => uint.TryParse(s, out var id) ? id : 0)
                .Where(id => id > 0)
                .Distinct()
                .ToList();

        private static List<Section> Bucket<TKey>(Dictionary<TKey, List<Section>> map, TKey key) where TKey : notnull =>
            map.TryGetValue(key, out var list) ? list : map[key] = [];
    }
}
