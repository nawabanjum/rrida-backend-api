using System.Text.Json;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models.DeterministicLayer;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CloverBackoffice.Services.Implementation
{
    /// <summary>
    /// Copies the vendored kit into the Ref* tables as one new version.
    ///
    /// It reads the engine's own <see cref="Rrida.Engine.DataBank"/> rather than parsing
    /// kit/data a second time. The engine canonicalises as it loads — polarity, locus and
    /// track lowercased, the export's "Opportunities" spelled "Opportunity" — and a second
    /// parser would drift from that quietly. Whatever the engine sees is what we store.
    /// </summary>
    public sealed class RridaReferenceDataLoader : IRridaReferenceDataLoader
    {
        private readonly ApplicationDbContext _context;
        private readonly IRridaEngineContextFactory _engine;
        private readonly ILogger<RridaReferenceDataLoader> _logger;

        public RridaReferenceDataLoader(
            ApplicationDbContext context,
            IRridaEngineContextFactory engine,
            ILogger<RridaReferenceDataLoader> logger)
        {
            _context = context;
            _engine = engine;
            _logger = logger;
        }

        public async Task<RefDataLoadResultDto> LoadAsync(int? actorUserId, CancellationToken ct = default)
        {
            var ctx = _engine.Context;
            var data = ctx.Data;
            var manifest = data.Manifest;

            var version = new RefDataVersion
            {
                SpecVersion = ReadString(manifest, "versions", "specVersion") ?? "unknown",
                KitVersion = ReadString(manifest, "kitVersion"),
                DataVersion = ReadString(manifest, "versions", "dataVersion"),
                BaselineTag = ReadString(manifest, "generatedFrom", "baselineTag"),
                TallyKey = ReadString(manifest, "tallyKey") ?? "rung_1_ask",
                RunMinutesCap = (int)data.RunMinutesCap,
                Source = _engine.KitDataPath.Length > 500
                    ? _engine.KitDataPath[..500]
                    : _engine.KitDataPath,
                LoadedAt = DateTime.UtcNow,
                LoadedBy = actorUserId,
                IsActive = true,
                QuestionCount = data.QuestionList.Count,
                ChipCount = data.ChipList.Count,
                ActionCount = data.Actions.Count,
                IssueFileCount = data.IssueFiles.Count,
                WorksheetCount = data.Worksheets.Count,
                WorksheetPhaseCount = data.Phases.Count,
                CapabilityFlagCount = data.CapabilityFlags.Count
            };

            // The kit's own declared counts, asserted against what we are about to write.
            // The engine already halts on a mismatch at load; this catches the other case,
            // where our copy loses rows on the way to the database.
            AssertCounts(manifest, version);

            await using var transaction = await _context.Database.BeginTransactionAsync(ct);

            // Only one version is active, and a partial unique index says so. Stand the old
            // one down first, in the same transaction; its rows are left exactly as they are,
            // because a cycle delivered against them must still resolve.
            var superseded = await _context.RefDataVersions
                .Where(v => v.IsActive)
                .ToListAsync(ct);
            foreach (var old in superseded) old.IsActive = false;
            await _context.SaveChangesAsync(ct);

            _context.RefDataVersions.Add(version);
            await _context.SaveChangesAsync(ct);

            var id = version.Id;

            _context.RefQuestions.AddRange(data.QuestionList.Select(q => new RefQuestion
            {
                VersionId = id,
                QuestionKey = q.Id,
                Text = q.Text,
                Element = q.Element,
                Track = q.Track
            }));

            _context.RefChips.AddRange(data.ChipList.Select(c => new RefChip
            {
                VersionId = id,
                ChipKey = c.Id,
                QuestionKey = c.QuestionId,
                Ordinal = OrdinalOf(c.Id),
                Track = c.Track,
                Element = c.Element,
                Polarity = c.Polarity,
                Text = c.Label,
                // A chip with no tag is a data fault, not a neutral chip. The engine says so
                // at load, so reaching here without one cannot happen; "" would be a silent
                // lie in the unlocated count, so it is stored as authored.
                Locus = c.Locus ?? throw new InvalidOperationException($"chip {c.Id} has no locus tag"),
                IssueFileKey = c.IssueFile,
                CapabilityGap = c.CapabilityGap,
                Rung1AskKey = c.Rung1.Ask,
                Rung1TeamKey = c.Rung1.Team,
                Rung1ContentKey = c.Rung1.Content,
                Rung2Key = c.Rung2
            }));

            _context.RefIssueFiles.AddRange(data.IssueFiles.Values.Select(f => new RefIssueFile
            {
                VersionId = id,
                IssueFileKey = f.Id,
                Issue = f.Issue ?? string.Empty,
                Pillar = f.Pillar ?? string.Empty,
                ConversationalCount = f.Conversational,
                StructuralCount = f.Structural,
                GestureCount = f.Gesture
            }));

            _context.RefActions.AddRange(data.Actions.Values.Select(a => new RefAction
            {
                VersionId = id,
                ActionKey = a.Id,
                Text = a.Text,
                IssueFileKey = a.IssueFile,
                Issue = a.Issue,
                Pillar = a.Pillar,
                Kind = a.Kind ?? string.Empty,
                Mode = a.Mode,
                NoteType = a.NoteType,
                WorkedIf = a.WorkedIf,
                Route = a.Route
            }));

            // Phases are written with their worksheet so the foreign key resolves in one
            // SaveChanges; EF fills WorksheetId from the navigation.
            foreach (var w in data.Worksheets.Values)
            {
                var sheet = new RefWorksheet
                {
                    VersionId = id,
                    Code = w.Code,
                    Title = w.Title ?? w.Code,
                    Formerly = w.Formerly,
                    Pillar = w.Pillar,
                    Difficulty = w.Difficulty,
                    TimeMinutes = w.TimeMinutes,
                    GroupSize = w.GroupSize,
                    Prerequisites = [.. w.Prerequisites],
                    Contraindications = [.. w.Contraindications]
                };

                foreach (var p in w.Phases)
                {
                    sheet.Phases.Add(new RefWorksheetPhase
                    {
                        VersionId = id,
                        PhaseKey = p.Id,
                        WorksheetCode = p.Worksheet,
                        Number = p.Number,
                        Title = p.Title ?? p.Id,
                        MinutesMin = ToMinutes(p.Minutes, 0),
                        MinutesMax = ToMinutes(p.Minutes, 1),
                        Role = p.Role,
                        Produces = p.Produces,
                        RequiresPhases = [.. p.RequiresPhases],
                        Excerptable = p.Excerptable,
                        Timing = p.Timing,
                        TimingNote = p.TimingNote
                    });
                }

                _context.RefWorksheets.Add(sheet);
            }

            _context.RefCapabilityFlags.AddRange(data.CapabilityFlags.Values.Select(f => new RefCapabilityFlag
            {
                VersionId = id,
                IssueFileKey = f.IssueFile,
                CapabilityGap = f.CapabilityGap,
                Worksheets = [.. f.Worksheets],
                Rationale = f.Rationale,
                ContentPhases = [.. f.ContentPhases],
                LocusDiscoveryPhases = [.. f.LocusDiscoveryPhases],
                LocusDiscoveryActionKey = f.LocusDiscoveryAction
            }));

            await _context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            _logger.LogInformation(
                "RRIDA reference data version {VersionId} loaded from {Path}: spec {Spec}, data {Data}, "
                + "{Questions} questions, {Chips} chips, {Actions} actions, {Phases} phases.",
                id, _engine.KitDataPath, version.SpecVersion, version.DataVersion,
                version.QuestionCount, version.ChipCount, version.ActionCount, version.WorksheetPhaseCount);

            return new RefDataLoadResultDto
            {
                VersionId = id,
                SpecVersion = version.SpecVersion,
                KitVersion = version.KitVersion,
                DataVersion = version.DataVersion,
                BaselineTag = version.BaselineTag,
                KitPath = _engine.KitDataPath,
                LoadedAt = version.LoadedAt,
                SupersededVersionId = superseded.Count > 0 ? superseded[^1].Id : null,
                Counts = CountsOf(version)
            };
        }

        public async Task<RefDataVersionDto?> GetActiveAsync(CancellationToken ct = default)
        {
            var v = await _context.RefDataVersions.AsNoTracking()
                .FirstOrDefaultAsync(x => x.IsActive, ct);

            return v is null ? null : new RefDataVersionDto
            {
                VersionId = v.Id,
                SpecVersion = v.SpecVersion,
                KitVersion = v.KitVersion,
                DataVersion = v.DataVersion,
                BaselineTag = v.BaselineTag,
                TallyKey = v.TallyKey,
                RunMinutesCap = v.RunMinutesCap,
                LoadedAt = v.LoadedAt,
                Counts = CountsOf(v)
            };
        }

        public async Task<RefQuestionDto?> GetQuestionAsync(int questionKey, CancellationToken ct = default)
        {
            var versionId = await _context.RefDataVersions.AsNoTracking()
                .Where(v => v.IsActive)
                .Select(v => (long?)v.Id)
                .FirstOrDefaultAsync(ct);

            if (versionId is null) return null;

            var question = await _context.RefQuestions.AsNoTracking()
                .FirstOrDefaultAsync(q => q.VersionId == versionId && q.QuestionKey == questionKey, ct);

            if (question is null) return null;

            var chips = await _context.RefChips.AsNoTracking()
                .Where(c => c.VersionId == versionId && c.QuestionKey == questionKey)
                .OrderBy(c => c.Polarity).ThenBy(c => c.Ordinal)
                .Select(c => new { c.Polarity, Dto = new RefChipDto { ChipKey = c.ChipKey, Ordinal = c.Ordinal, Text = c.Text } })
                .ToListAsync(ct);

            return new RefQuestionDto
            {
                QuestionKey = question.QuestionKey,
                Text = question.Text,
                Element = question.Element,
                Track = question.Track,
                Chips = chips.GroupBy(x => x.Polarity)
                    .ToDictionary(g => g.Key, g => g.Select(x => x.Dto).ToList(), StringComparer.Ordinal)
            };
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private static Dictionary<string, int> CountsOf(RefDataVersion v) => new(StringComparer.Ordinal)
        {
            ["questions"] = v.QuestionCount,
            ["chips"] = v.ChipCount,
            ["actions"] = v.ActionCount,
            ["issueFiles"] = v.IssueFileCount,
            ["worksheets"] = v.WorksheetCount,
            ["worksheetPhases"] = v.WorksheetPhaseCount,
            ["capabilityFlags"] = v.CapabilityFlagCount
        };

        /// <summary>
        /// The chip id ends with a polarity letter and an index, e.g. "191-I6". The index is
        /// the authored order within the set and is not recorded anywhere else.
        /// </summary>
        private static int OrdinalOf(string chipId)
        {
            var digits = string.Concat(chipId.Reverse().TakeWhile(char.IsDigit).Reverse());
            return int.TryParse(digits, out var n) ? n : 0;
        }

        private static int? ToMinutes(double?[]? minutes, int index) =>
            minutes is not null && minutes.Length > index && minutes[index] is { } m ? (int)m : null;

        private static string? ReadString(JsonElement root, params string[] path)
        {
            var node = root;
            foreach (var key in path)
            {
                if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty(key, out node))
                    return null;
            }
            return node.ValueKind == JsonValueKind.String ? node.GetString() : null;
        }

        /// <summary>
        /// The kit declares its counts and says a mismatch halts. Both tracks are loaded, so
        /// the per-track figures are added; the derived "_all_tracks" totals are carried for
        /// readers and are deliberately not asserted by the kit, nor here.
        /// </summary>
        private static void AssertCounts(JsonElement manifest, RefDataVersion loaded)
        {
            if (!manifest.TryGetProperty("counts", out var counts) || counts.ValueKind != JsonValueKind.Object)
                return;

            var expected = new List<(string Name, int Expected, int Actual)>
            {
                ("questions", Sum(counts, "questions", "team_questions"), loaded.QuestionCount),
                ("chips", Sum(counts, "chips", "team_chips"), loaded.ChipCount),
                ("actions", Sum(counts, "actions"), loaded.ActionCount),
                ("issue_files", Sum(counts, "issue_files"), loaded.IssueFileCount),
                ("worksheets", Sum(counts, "worksheets"), loaded.WorksheetCount),
                ("capability_flags", Sum(counts, "capability_flags"), loaded.CapabilityFlagCount)
            };

            var wrong = expected.Where(e => e.Expected > 0 && e.Expected != e.Actual).ToList();
            if (wrong.Count > 0)
            {
                throw new InvalidOperationException(
                    "The RRIDA kit's declared counts disagree with what was loaded, so nothing was written: "
                    + string.Join("; ", wrong.Select(w => $"{w.Name} expected {w.Expected}, loaded {w.Actual}")));
            }
        }

        private static int Sum(JsonElement counts, params string[] keys)
        {
            var total = 0;
            foreach (var key in keys)
            {
                if (counts.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number)
                    total += value.GetInt32();
            }
            return total;
        }
    }
}
