using System.Text;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models.DeterministicLayer;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using static CloverBackoffice.Services.Implementation.EngineWorkbookReader;

namespace CloverBackoffice.Services.Implementation
{
    /// <summary>
    /// Imports the test-data workbook, one team per sheet.
    ///
    /// For each sheet: the team is found by name in the profile, or created through the
    /// ordinary team-create path. Its rows become a scenario script and go through
    /// <see cref="IEngineScenarioSeeder" />, the same path the shipped scenarios use, so the
    /// engine sees members, answers, chips and confirmations exactly as it would for real
    /// ones and the replay derives everything else. The same check-ins are then written to
    /// Team Health's tables (DailyFeedback, TeamDailyFeedback, custom questions) by
    /// <see cref="EngineWorkbookTeamHealthWriter" />.
    ///
    /// A sheet that fails is reported and the next one carries on.
    /// </summary>
    public sealed class EngineWorkbookImporter : IEngineWorkbookImporter
    {
        private const int WarningsShown = 50;

        private readonly ApplicationDbContext _context;
        private readonly IRridaEngineContextFactory _engine;
        private readonly IEngineScenarioSeeder _seeder;
        private readonly ICompanyTeamService _teams;
        private readonly EngineWorkbookTeamHealthWriter _teamHealth;
        private readonly ILogger<EngineWorkbookImporter> _logger;

        public EngineWorkbookImporter(
            ApplicationDbContext context,
            IRridaEngineContextFactory engine,
            IEngineScenarioSeeder seeder,
            ICompanyTeamService teams,
            EngineWorkbookTeamHealthWriter teamHealth,
            ILogger<EngineWorkbookImporter> logger)
        {
            _context = context;
            _engine = engine;
            _seeder = seeder;
            _teams = teams;
            _teamHealth = teamHealth;
            _logger = logger;
        }

        public async Task<EngineWorkbookImportResultDto> ImportAsync(
            Stream workbook,
            string fileName,
            EngineWorkbookImportOptionsDto options,
            int actorUserId,
            CancellationToken ct = default)
        {
            var profileExists = await _context.Profiles.AnyAsync(p => p.Id == options.ProfileId && p.IsDel != true, ct);
            if (!profileExists)
                throw new InvalidOperationException($"Profile {options.ProfileId} was not found.");

            List<Sheet> sheets;
            try
            {
                sheets = EngineWorkbookReader.Read(workbook);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InvalidOperationException("The file could not be read as an .xlsx workbook.", ex);
            }

            if (sheets.Count == 0)
                throw new InvalidOperationException(
                    "No team sheet was found. Each team sheet needs a header row whose column A reads \"Day No.\".");

            var duplicate = sheets
                .Where(s => !string.IsNullOrWhiteSpace(s.TeamName))
                .GroupBy(s => s.TeamName!.Trim(), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(g => g.Count() > 1);
            if (duplicate is not null)
                throw new InvalidOperationException(
                    $"Team \"{duplicate.Key}\" appears on more than one sheet ({string.Join(", ", duplicate.Select(s => s.SheetName))}).");

            var result = new EngineWorkbookImportResultDto { FileName = fileName, SheetsRead = sheets.Count };

            foreach (var sheet in sheets)
            {
                ct.ThrowIfCancellationRequested();
                var team = await ImportSheetAsync(sheet, options, actorUserId, ct);
                result.Teams.Add(team);
            }

            foreach (var team in result.Teams)
            {
                switch (team.Status)
                {
                    case "created": result.TeamsCreated += 1; break;
                    case "updated": result.TeamsUpdated += 1; break;
                    case "skipped": result.TeamsSkipped += 1; break;
                    default: result.TeamsFailed += 1; break;
                }

                var t = result.Totals;
                t.RowsRead += team.RowsRead;
                t.Members += team.Members;
                t.IndividualAnswers += team.IndividualAnswers;
                t.TeamAnswers += team.TeamAnswers;
                t.Chips += team.Chips;
                t.Confirmations += team.Confirmations;
                t.DailyFeedback += team.DailyFeedback;
                t.TeamDailyFeedback += team.TeamDailyFeedback;
                t.FeedbackReasons += team.FeedbackReasons;
                t.CustomQuestions += team.CustomQuestions;
                t.CustomAnswers += team.CustomAnswers;
                t.EngineStateDays += team.EngineStateDays;
                t.Cycles += team.CycleOutcomes.Values.Sum();
                t.Warnings += team.WarningCount;
            }

            result.Success = result.TeamsFailed == 0 && result.TeamsCreated + result.TeamsUpdated > 0;

            _logger.LogInformation(
                "[Workbook] {File} into profile {Profile}: {Created} created, {Updated} updated, {Skipped} skipped, "
                + "{Failed} failed, {Answers} engine answers.",
                fileName, options.ProfileId, result.TeamsCreated, result.TeamsUpdated, result.TeamsSkipped,
                result.TeamsFailed, result.Totals.IndividualAnswers + result.Totals.TeamAnswers);

            return result;
        }

        // ── one sheet ────────────────────────────────────────────────────────

        private async Task<EngineWorkbookTeamResultDto> ImportSheetAsync(
            Sheet sheet, EngineWorkbookImportOptionsDto options, int actorUserId, CancellationToken ct)
        {
            var dto = new EngineWorkbookTeamResultDto
            {
                Sheet = sheet.SheetName,
                TeamName = sheet.TeamName?.Trim() ?? string.Empty,
                SheetStartDate = sheet.StartDate,
                RowsRead = sheet.Rows.Count
            };
            var warnings = new List<string>(sheet.Warnings);

            try
            {
                if (sheet.Error is not null) return Fail(dto, sheet.Error, warnings);

                var week = CanonicalWeek(sheet.WorkingDays!);
                if (week is null)
                    return Fail(dto, $"Working days \"{sheet.WorkingDays}\" is not one the engine knows "
                                     + "(Mon-Fri, Sun-Thu, Sat-Wed, Tue-Sat, Wed-Sun, Every day).", warnings);
                dto.WorkingWeek = week;

                var rows = DistinctRows(sheet.Rows, warnings);
                var askedDays = rows.Max(r => r.Day);
                dto.AskedDays = askedDays;

                // Day N of the sheet is the Nth ASKED day, so the calendar day it lands on
                // depends on the working week.
                var sheetClock = new EngineTeamClock(sheet.StartDate!.Value, week, _engine.Calendar);
                var horizon = CalendarDayOfAskedDay(sheetClock, askedDays);
                var startDate = options.AlignToToday
                    ? AlignToToday(sheet.StartDate.Value, sheetClock.DateOfDay(horizon))
                    : sheet.StartDate.Value;
                var clock = new EngineTeamClock(startDate, week, _engine.Calendar);

                // ── the team ──
                var (unitId, created) = await FindOrCreateTeamAsync(dto.TeamName, options.ProfileId,
                    rows.Select(r => r.Member).Distinct(StringComparer.OrdinalIgnoreCase).Count(), actorUserId, warnings, ct);
                dto.BusinessUnitId = unitId;

                var hasEngineData = await _context.EngineAnswers.AnyAsync(a => a.BusinessUnitId == unitId, ct);
                var hasImported = await _teamHealth.HasImportedRowsAsync(unitId, ct);
                if ((hasEngineData || hasImported) && !options.ReplaceExisting)
                {
                    dto.Status = "skipped";
                    dto.Message = "This team already holds engine or imported data. Send replaceExisting=true to clear it and import again.";
                    return Finish(dto, warnings);
                }

                if (hasImported) await _teamHealth.ClearImportedRowsAsync(unitId, ct);

                // ── the engine ──
                var scenario = BuildScenario(dto.TeamName, week, horizon, rows, warnings);
                var seed = await _seeder.SeedScenarioAsync(
                    unitId, scenario, startDate, options.AutoConfirm, reset: hasEngineData, actorUserId, ct);

                dto.StartDate = seed.StartDate;
                dto.ThroughDate = seed.ThroughDate;
                dto.Members = seed.MembersSeeded;
                dto.Chips = seed.ChipsSeeded;
                dto.Confirmations = seed.ConfirmationsSeeded;
                dto.CycleOutcomes = seed.CycleOutcomes;
                dto.IndividualAnswers = await _context.EngineAnswers
                    .CountAsync(a => a.BusinessUnitId == unitId && a.Track == "individual", ct);
                dto.TeamAnswers = await _context.EngineAnswers
                    .CountAsync(a => a.BusinessUnitId == unitId && a.Track == "team", ct);
                dto.EngineStateDays = await _context.EngineStateDaily.CountAsync(s => s.BusinessUnitId == unitId, ct);

                // ── Team Health ──
                var written = await _teamHealth.WriteAsync(unitId, options.ProfileId, clock, rows, actorUserId, warnings, ct);
                dto.DailyFeedback = written.DailyFeedback;
                dto.TeamDailyFeedback = written.TeamDailyFeedback;
                dto.FeedbackReasons = written.Reasons;
                dto.CustomQuestions = written.CustomQuestions;
                dto.CustomAnswers = written.CustomAnswers;

                dto.Status = created ? "created" : "updated";
                return Finish(dto, warnings);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // One bad sheet must not stop the others; the reason goes back in the summary.
                _logger.LogError(ex, "[Workbook] Sheet {Sheet} ({Team}) failed.", sheet.SheetName, sheet.TeamName);
                _context.ChangeTracker.Clear();
                return Fail(dto, ex is InvalidOperationException ? ex.Message : $"Import failed: {ex.GetBaseException().Message}", warnings);
            }
        }

        private async Task<(uint UnitId, bool Created)> FindOrCreateTeamAsync(
            string name, uint profileId, int teamSize, int actorUserId, List<string> warnings, CancellationToken ct)
        {
            var lowered = name.ToLower();
            var matches = await _context.BusinessUnits
                .Where(b => b.ProfileId == profileId && b.IsDel != true && b.Name.ToLower() == lowered)
                .OrderBy(b => b.Id)
                .ToListAsync(ct);

            if (matches.Count > 0)
            {
                if (matches.Count > 1)
                    warnings.Add($"{matches.Count} teams in this profile are named \"{name}\"; imported into the oldest, {matches[0].Id}.");

                var unit = matches[0];
                // Team Health's response rate needs a size; one the admin set is kept.
                if (unit.TeamSize is null)
                {
                    unit.TeamSize = teamSize;
                    await _context.SaveChangesAsync(ct);
                }

                return (unit.Id, false);
            }

            // The ordinary create path: codes, contact, question set and licence coverage
            // all as for a team made in the app. Test teams skip the team-capacity check
            // (licences x 3), which would otherwise refuse a profile with no licences.
            var upsert = await _teams.UpsertBusinessUnitAsync(new UpsertBusinessUnitDto
            {
                TeamName = name,
                ProfileId = (int)profileId,
                TeamSize = teamSize,
                CreatedBy = actorUserId,
                LicenseAlreadyReserved = true
            });

            if (upsert.Id <= 0)
                throw new InvalidOperationException($"The team could not be created: {upsert.message}");

            return ((uint)upsert.Id, true);
        }

        // ── rows → the seeder's script ──────────────────────────────────────

        private EngineScenarioFile BuildScenario(
            string teamName, string week, int horizon, List<Row> rows, List<string> warnings)
        {
            // D34. Every team is asked on the ninety-question rotation, so a row's day meets the
            // question the ninety asks on that day, as it would in the app.
            var rotation = _engine.Context.Rotation;
            var adapted = 0;
            var data = _engine.Context.Data;

            var scenario = new EngineScenarioFile
            {
                Id = "workbook",
                Name = teamName,
                WorkingWeek = week,
                Horizon = horizon
            };

            var rosterIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in rows.Select(r => r.Member))
            {
                if (rosterIds.ContainsKey(name)) continue;
                var id = UniqueSlug(name, rosterIds.Values);
                rosterIds[name] = id;
                scenario.Roster.Add(new EngineScenarioFile.ScenarioMember { Id = id, Name = name });
            }

            foreach (var row in rows)
            {
                var entries = scenario.Script.TryGetValue(row.Day.ToString(), out var list)
                    ? list
                    : scenario.Script[row.Day.ToString()] = [];

                foreach (var (track, section) in new[] { ("individual", row.Individual), ("team", row.Team) })
                {
                    if (section is null) continue;

                    var questionId = rotation.QuestionIdForDay(row.Day, track);
                    var question = questionId is null ? null : data.QuestionOf(questionId.Value);
                    if (question is null)
                    {
                        warnings.Add($"Row {row.RowNumber}: no {track} question is asked on day {row.Day}. Section skipped.");
                        continue;
                    }

                    entries.Add(new EngineScenarioFile.ScenarioEntry
                    {
                        Member = rosterIds[row.Member],
                        Track = track,
                        Smiley = section.Sentiment,
                        Chips = ResolveChips(question, section, ref adapted),
                        Text = section.Text
                    });
                }
            }

            if (adapted > 0)
                warnings.Add($"{adapted} answer(s) named no chip offered by the 90-question rotation's question on their day "
                             + "(the workbook was written for the six-question demo rotation), so each took one of that "
                             + "question's own chips for its sentiment.");
            return scenario;
        }

        /// <summary>
        /// Chip labels to chip ids, against the question that day's rotation asks and the
        /// polarity the sentiment selects: the same set the app would have offered. A row whose
        /// labels belong to another question and that has no text of its own takes the first chip
        /// that question offers for its sentiment, because the app never lets an answer leave
        /// without a reason (spec 4.3) and the engine refuses one that does.
        /// </summary>
        private List<string> ResolveChips(Rrida.Engine.Question question, Section section, ref int adapted)
        {
            var polarity = PolarityOf(section.Sentiment);
            var offered = question.Chips.TryGetValue(polarity, out var ids) ? ids : [];
            var chips = new List<string>();

            foreach (var label in section.Chips)
            {
                var chip = offered
                    .Select(id => _engine.Context.Data.Chip(id))
                    .FirstOrDefault(c => c is not null && string.Equals(c.Label.Trim(), label, StringComparison.OrdinalIgnoreCase));
                if (chip is not null && !chips.Contains(chip.Id)) chips.Add(chip.Id);
            }

            if (chips.Count == 0 && string.IsNullOrWhiteSpace(section.Text) && offered.Count > 0)
            {
                chips.Add(offered[0]);
                adapted += 1;
            }

            return chips;
        }

        internal static string PolarityOf(string sentiment) => sentiment switch
        {
            "red" or "orange" => "negative",
            "yellow" => "indifferent",
            _ => "positive"
        };

        // ── helpers ──────────────────────────────────────────────────────────

        /// <summary>One check-in per member per day: a repeated row would break the engine's unique key.</summary>
        private static List<Row> DistinctRows(List<Row> rows, List<string> warnings)
        {
            var seen = new HashSet<(int, string)>();
            var kept = new List<Row>();
            foreach (var row in rows)
            {
                if (seen.Add((row.Day, row.Member.ToLowerInvariant()))) kept.Add(row);
                else warnings.Add($"Row {row.RowNumber}: {row.Member} already has a row for day {row.Day}. Row skipped.");
            }

            return kept;
        }

        private string? CanonicalWeek(string workingDays)
        {
            var week = workingDays.Trim().ToLowerInvariant().Replace(' ', '-');
            try
            {
                _engine.Calendar.Resolve(week);
                return week;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static int CalendarDayOfAskedDay(EngineTeamClock clock, int askedDay)
        {
            var asked = 0;
            for (var day = 1; ; day += 1)
            {
                if (!clock.IsAskedOn(clock.DateOfDay(day))) continue;
                asked += 1;
                if (asked == askedDay) return day;
            }
        }

        /// <summary>Whole weeks only, so every row keeps its weekday and the engine sees the same team.</summary>
        private static DateOnly AlignToToday(DateOnly start, DateOnly lastDate)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var gap = today.DayNumber - lastDate.DayNumber;
            var weeks = (int)Math.Floor(gap / 7.0);
            return start.AddDays(weeks * 7);
        }

        // EngineMemberKey is "seed-{unit}-{id}" in 30 characters, so the id stays short.
        private static string UniqueSlug(string name, IEnumerable<string> taken)
        {
            var sb = new StringBuilder();
            foreach (var ch in name.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(ch) && ch < 128) sb.Append(ch);
                else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
            }

            var slug = sb.ToString().Trim('-');
            if (slug.Length == 0) slug = "member";
            if (slug.Length > 12) slug = slug[..12].Trim('-');

            var used = taken.ToHashSet(StringComparer.Ordinal);
            var candidate = slug;
            for (var n = 2; used.Contains(candidate); n += 1) candidate = $"{slug}{n}";
            return candidate;
        }

        private static EngineWorkbookTeamResultDto Fail(EngineWorkbookTeamResultDto dto, string message, List<string> warnings)
        {
            dto.Status = "failed";
            dto.Message = message;
            return Finish(dto, warnings);
        }

        private static EngineWorkbookTeamResultDto Finish(EngineWorkbookTeamResultDto dto, List<string> warnings)
        {
            dto.WarningCount = warnings.Count;
            dto.Warnings = warnings.Take(WarningsShown).ToList();
            return dto;
        }
    }
}
