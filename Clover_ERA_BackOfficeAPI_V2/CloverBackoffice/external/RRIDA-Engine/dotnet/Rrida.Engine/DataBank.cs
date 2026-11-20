using System.Text.Json;

namespace Rrida.Engine;

/// <summary>
/// Startup data load, from <c>kit/data</c>. Build spec section 2, deterministic spec 5.1.1 and 5.4.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here repairs data. The counts in meta.json are asserted against what was loaded
/// and a mismatch halts. If the source changes, re-run the exporter and fix the workbook,
/// never the JSON.
/// </para>
/// <para>
/// The only transformation applied is naming: the export uses the workbooks' own
/// snake_case and their spellings ("Opportunities", "Negative"), and the engine uses
/// camelCase and the CLOVER dimension name. That mapping lives here and nowhere else, so
/// the files on disk stay byte-identical to what the exporter wrote.
/// </para>
/// </remarks>
public sealed class DataBank
{
    /// <summary>
    /// The export spells the CLOVER dimension "Opportunities". This is a spelling fix for
    /// the names it knows, not a whitelist: anything unlisted passes through unchanged,
    /// because element is open (spec 7.1a) and a team bank element must load without
    /// anyone editing this file.
    /// </summary>
    private static readonly Dictionary<string, string> ElementSpelling =
        new(StringComparer.Ordinal) { ["Opportunities"] = "Opportunity" };

    private static readonly Dictionary<string, string> PolarityCanon =
        new(StringComparer.Ordinal)
        {
            ["Negative"] = "negative", ["Indifferent"] = "indifferent", ["Positive"] = "positive",
        };

    /// <summary>Locus as authored, lowercased. Absent is not neutral: see Validate.</summary>
    private static readonly Dictionary<string, string> LocusCanon =
        new(StringComparer.Ordinal)
        {
            ["Internal"] = "internal", ["External"] = "external", ["Neutral"] = "neutral",
        };

    private static readonly Dictionary<string, string> TrackCanon =
        new(StringComparer.Ordinal) { ["Individual"] = "individual", ["Team"] = "team" };

    private static string CanonElement(string? name) =>
        name is not null && ElementSpelling.TryGetValue(name, out var fixedUp) ? fixedUp : name ?? "";

    public required string Root { get; init; }
    public required Dictionary<string, Chip> Chips { get; init; }
    public required List<Chip> ChipList { get; init; }
    public required Dictionary<int, Question> Questions { get; init; }
    public required List<Question> QuestionList { get; init; }
    public required Dictionary<string, ActionRow> Actions { get; init; }
    public required Dictionary<string, IssueFileRow> IssueFiles { get; init; }

    /// <summary>
    /// D31. The outward move for each condition that an external-locus negative chip can
    /// raise, keyed by issue file.
    /// </summary>
    public required Dictionary<string, OutwardMove> Outward { get; init; }
    public required Dictionary<string, Worksheet> Worksheets { get; init; }
    public required Dictionary<string, Phase> Phases { get; init; }
    public required Dictionary<string, CapabilityFlag> CapabilityFlags { get; init; }
    public required JsonElement Manifest { get; init; }

    /// <summary>Simple majority (spec 5.1.1). meta.json states it in prose, so the number lives here.</summary>
    public double ShareThreshold { get; init; } = 0.5;

    /// <summary>The cap on a delivered run, in minutes of session time (spec 5.1.3d). From meta.json.</summary>
    public required double RunMinutesCap { get; init; }

    /// <summary>Which actions sit in each file, for the succession rule (spec 8.2).</summary>
    public required Dictionary<string, List<ActionRow>> ActionsByFile { get; init; }

    public required Dictionary<string, List<string>> ChipsByTallyAction { get; init; }

    private Dictionary<string, List<Question>> _byTrack = new(StringComparer.Ordinal);

    public ActionRow? Action(string? id) =>
        id is not null && Actions.TryGetValue(id, out var a) ? a : null;

    public Chip? Chip(string? id) =>
        id is not null && Chips.TryGetValue(id, out var c) ? c : null;

    public Question? QuestionOf(int id) => Questions.TryGetValue(id, out var q) ? q : null;

    public IssueFileRow? IssueFile(string? id) =>
        id is not null && IssueFiles.TryGetValue(id, out var f) ? f : null;

    public Worksheet? Worksheet(string? code) =>
        code is not null && Worksheets.TryGetValue(code, out var w) ? w : null;

    public Phase? Phase(string? id) =>
        id is not null && Phases.TryGetValue(id, out var p) ? p : null;

    public CapabilityFlag? CapabilityFlag(string? issueFile) =>
        issueFile is not null && CapabilityFlags.TryGetValue(issueFile, out var f) ? f : null;

    public IReadOnlyList<Question> QuestionsForTrack(string track) =>
        _byTrack.TryGetValue(track, out var list) ? list : [];

    public IReadOnlyList<string> TracksPresent =>
        Vocabulary.Tracks.Where(t => QuestionsForTrack(t).Count > 0).ToList();

    /// <summary>Is this issue file a capability gap (spec 5.1.1)?</summary>
    public bool IsCapabilityGap(string? issueFile) =>
        CapabilityFlag(issueFile) is { CapabilityGap: true };

    /// <summary>The action that delivers a file's locus-discovery run (spec 4.6).</summary>
    /// <summary>
    /// D31. The outward move for one condition, or null where it has none.
    /// </summary>
    /// <remarks>
    /// Returns the action and the role it goes to together, because a move named without a
    /// recipient is not actionable and the recipient on its own says nothing. The role is
    /// authored and is always a role: the layer has no org chart above the manager and must
    /// not invent one.
    /// </remarks>
    public OutwardMove? OutwardFor(string? issueFile) =>
        issueFile is not null && Outward.TryGetValue(issueFile, out var m) ? m : null;

    public List<string> OutwardFiles() =>
        Outward.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

    public ActionRow? LocusDiscoveryAction(string issueFile) =>
        CapabilityFlag(issueFile) is { LocusDiscoveryAction: { } id } ? Action(id) : null;

    /// <summary>
    /// A file's content run, or its locus-discovery run, resolved to phases.
    /// </summary>
    /// <remarks>
    /// Two different deliveries (spec 4.6, 5.1.3a). <c>content</c> is the fix.
    /// <c>locus_discovery</c> is what a majority-neutral signal gets: the same exercise
    /// more often than not, but framed as finding out rather than as the answer, and
    /// present on every file.
    /// </remarks>
    public List<Phase> PhaseRun(string issueFile, string kind = "content")
    {
        var flag = CapabilityFlag(issueFile);
        if (flag is null) return [];
        var ids = kind == "locus_discovery" ? flag.LocusDiscoveryPhases : flag.ContentPhases;
        return ids.Select(Phase).OfType<Phase>().ToList();
    }

    public List<Worksheet> WorksheetsFor(string issueFile)
    {
        var flag = CapabilityFlag(issueFile);
        if (flag is null) return [];
        return flag.Worksheets.Select(Worksheet).OfType<Worksheet>().ToList();
    }

    /// <summary>The elements the loaded bank happens to use. Derived, never declared.</summary>
    public static List<string> ElementsIn(IEnumerable<Question> questions) =>
        questions.Select(q => q.Element).Distinct(StringComparer.Ordinal).ToList();

    // ---------------------------------------------------------------- loading

    /// <summary>
    /// Read one of the kit's files.
    /// </summary>
    /// <remarks>
    /// <b>One read, and absent means "not found" and nothing else.</b> Testing for existence
    /// and then reading is a time-of-check-to-time-of-use pair, and what it admits is the
    /// worst kind of failure: a file that is present reads as absent, the caller takes its
    /// "not configured" branch, and the system comes up half built with nothing said.
    /// <c>File.Exists</c> swallows every error to answer false, so a sharing violation, a
    /// descriptor limit or a permissions blip all arrive as "missing" and send the caller
    /// down a branch written for a file that was never there. Anything that is not
    /// not-found is raised as itself.
    /// </remarks>
    private static JsonDocument ReadJson(string file)
    {
        string text;
        try
        {
            text = File.ReadAllText(file);
        }
        catch (Exception err) when (err is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new DataException(
                $"{Path.GetFileName(file)} is missing from {Path.GetDirectoryName(file)}.\n"
                + "The build's input is the kit's data folder. Re-run tools/kit/build-kit.mjs "
                + "against the reference and copy its output in.");
        }
        catch (Exception err) when (err is IOException or UnauthorizedAccessException)
        {
            throw new DataException(
                $"{Path.GetFileName(file)} is present and could not be read: {err.GetType().Name}. "
                + "That is a different fault from a missing file and is not fixed by "
                + "regenerating the kit.");
        }
        try
        {
            return JsonDocument.Parse(text);
        }
        catch (JsonException err)
        {
            throw new DataException($"{Path.GetFileName(file)} is not readable JSON: {err.Message}");
        }
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.String ? v.GetString() : null;

    private static int Int(JsonElement e, string name, int fallback = 0) =>
        e.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.Number ? v.GetInt32() : fallback;

    private static int? IntOrNull(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.Number ? v.GetInt32() : null;

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True;

    private static List<string> StrList(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v) || v.ValueKind is not JsonValueKind.Array) return [];
        return v.EnumerateArray()
            .Where(x => x.ValueKind is JsonValueKind.String)
            .Select(x => x.GetString()!)
            .ToList();
    }

    /// <summary>
    /// Load both banks, the merged library, and the configuration beside them.
    /// </summary>
    /// <param name="root">The kit's <c>data</c> directory.</param>
    public static DataBank Load(string root)
    {
        var meta = ReadJson(Path.Combine(root, "meta.json")).RootElement.Clone();
        var rawIndQuestions = ReadJson(Path.Combine(root, "individual", "questions.json")).RootElement.Clone();
        var rawIndChips = ReadJson(Path.Combine(root, "individual", "chips.json")).RootElement.Clone();
        var rawTeamQuestions = ReadJson(Path.Combine(root, "team", "questions.json")).RootElement.Clone();
        var rawTeamChips = ReadJson(Path.Combine(root, "team", "chips.json")).RootElement.Clone();
        var rawActions = ReadJson(Path.Combine(root, "library", "actions.json")).RootElement.Clone();
        var rawFiles = ReadJson(Path.Combine(root, "library", "issue_files.json")).RootElement.Clone();
        var rawFlags = ReadJson(Path.Combine(root, "library", "capability_flags.json")).RootElement.Clone();
        var rawWorksheets = ReadJson(Path.Combine(root, "library", "worksheets.json")).RootElement.Clone();
        // D31. One outward move per condition. Required rather than optional: a build where
        // this is absent is a build where a team naming a cause outside itself is handed a
        // move about its own behaviour, and that failing loudly at load is the point.
        var rawOutward = ReadJson(Path.Combine(root, "library", "outward.json")).RootElement.Clone();

        // Assert the counts in meta.json against what was loaded, and halt on mismatch.
        // The derived totals are carried for whoever reads them and are deliberately not
        // asserted: they add no independent check, and a fixture that adjusts one count
        // would otherwise have to chase a second and halt on arithmetic instead of on the
        // fault under test.
        var loaded = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["chips"] = rawIndChips.GetArrayLength(),
            ["actions"] = rawActions.GetArrayLength(),
            ["issue_files"] = rawFiles.GetArrayLength(),
            ["questions"] = rawIndQuestions.GetArrayLength(),
            ["capability_flags"] = rawFlags.GetArrayLength(),
            ["worksheets"] = rawWorksheets.GetArrayLength(),
            ["team_chips"] = rawTeamChips.GetArrayLength(),
            ["team_questions"] = rawTeamQuestions.GetArrayLength(),
        };
        var derived = new HashSet<string>(StringComparer.Ordinal) { "chips_all_tracks", "questions_all_tracks" };
        var wrong = new List<string>();
        if (meta.TryGetProperty("counts", out var counts))
        {
            foreach (var entry in counts.EnumerateObject())
            {
                if (derived.Contains(entry.Name)) continue;
                if (!loaded.TryGetValue(entry.Name, out var actual)) continue;
                var declared = entry.Value.GetInt32();
                if (actual != declared)
                {
                    wrong.Add($"{entry.Name}: meta.json says {declared}, loaded {actual}");
                }
            }
        }
        if (wrong.Count > 0)
        {
            throw new DataException(
                $"the kit's data does not match its own meta.json:\n  {string.Join("\n  ", wrong)}\n\n"
                + "The files must come from one export run. Re-run the exporters.");
        }

        // The tally key is asserted against the export rather than only hard-coded, so a
        // change of mind about it in the data shows up as a halt instead of as a silent
        // divergence between what the spec says and what the engine counts.
        var declaredKey = Str(meta, "tallyKey") ?? Str(meta, "tally_key");
        if (declaredKey is not null && declaredKey != Vocabulary.RouteColumn[Vocabulary.TallyKey])
        {
            throw new DataException(
                $"meta.json says the tally key is {declaredKey}, the engine tallies on "
                + $"{Vocabulary.RouteColumn[Vocabulary.TallyKey]}. Changing it is a spec change "
                + "(4.2, 5.1.1), not a config edit.");
        }

        // The run length cap is the spec's, not the build's (spec 5.1.3d), so it is read
        // from meta.json rather than hard-coded.
        double? runMinutesCap = meta.TryGetProperty("runMinutesCap", out var capV)
            && capV.ValueKind is JsonValueKind.Number
                ? capV.GetDouble()
                : meta.TryGetProperty("run_minutes_cap", out var capV2)
                    && capV2.ValueKind is JsonValueKind.Number ? capV2.GetDouble() : null;
        if (runMinutesCap is null)
        {
            throw new DataException(
                "meta.json does not state runMinutesCap. The cap on a delivered run is a spec "
                + "value (5.1.3d), not a build parameter, so the build will not supply one of its own.");
        }

        // The two banks share one id space. A collision would be silent rather than loud:
        // the later row would overwrite the earlier one in the id map and a question would
        // quietly lose a chip.
        var coreQuestionIds = rawIndQuestions.EnumerateArray().Select(q => Int(q, "id")).ToHashSet();
        foreach (var q in rawTeamQuestions.EnumerateArray())
        {
            if (coreQuestionIds.Contains(Int(q, "id")))
            {
                throw new DataException(
                    $"team question {Int(q, "id")} reuses an id from the individual bank. The two "
                    + "banks share one id space, so the team questions need their own range.");
            }
        }
        var coreChipIds = rawIndChips.EnumerateArray().Select(c => Str(c, "chip_id")!).ToHashSet(StringComparer.Ordinal);
        foreach (var c in rawTeamChips.EnumerateArray())
        {
            if (coreChipIds.Contains(Str(c, "chip_id")!))
            {
                throw new DataException(
                    $"team chip {Str(c, "chip_id")} reuses an id from the individual bank. The two "
                    + "banks share one id space, so the team chips need their own range.");
            }
        }

        // Track is canonicalised on the way in, on both banks. The export writes it
        // capitalised and the engine matches on lowercase; a mismatch here is silent rather
        // than loud, because QuestionsForTrack simply returns nothing and every check built
        // on it quietly passes over an empty list.
        static string Track(JsonElement row, string fallback)
        {
            var raw = Str(row, "track");
            if (raw is null) return fallback;
            return TrackCanon.TryGetValue(raw, out var t) ? t : raw;
        }

        var allQuestionRows = rawIndQuestions.EnumerateArray().Select(q => (Row: q, Track: Track(q, "individual")))
            .Concat(rawTeamQuestions.EnumerateArray().Select(q => (Row: q, Track: Track(q, "team"))))
            .ToList();
        var allChipRows = rawIndChips.EnumerateArray().Select(c => (Row: c, Track: Track(c, "individual")))
            .Concat(rawTeamChips.EnumerateArray().Select(c => (Row: c, Track: Track(c, "team"))))
            .ToList();

        var actions = new Dictionary<string, ActionRow>(StringComparer.Ordinal);
        foreach (var a in rawActions.EnumerateArray())
        {
            var id = Str(a, "id")!;
            if (actions.ContainsKey(id)) throw new DataException($"duplicate action id {id} in actions.json");
            actions[id] = new ActionRow
            {
                Id = id,
                Text = Str(a, "text") ?? "",
                IssueFile = Str(a, "issue_file") ?? "",
                Issue = Str(a, "issue"),
                Pillar = CanonElement(Str(a, "pillar")),
                Kind = Str(a, "kind"),
                Mode = Str(a, "mode"),
                NoteType = Str(a, "note_type"),
                WorkedIf = Str(a, "worked_if"),
                Route = Str(a, "route")?.ToLowerInvariant(),
                ReviewStatus = Str(a, "review_status"),
            };
        }

        var issueFiles = new Dictionary<string, IssueFileRow>(StringComparer.Ordinal);
        foreach (var f in rawFiles.EnumerateArray())
        {
            var id = Str(f, "issue_file")!;
            issueFiles[id] = new IssueFileRow
            {
                Id = id,
                Issue = Str(f, "issue"),
                Pillar = CanonElement(Str(f, "pillar")),
                Conversational = Int(f, "conversational"),
                Structural = Int(f, "structural"),
                Gesture = Int(f, "gesture"),
            };
        }

        var worksheets = new Dictionary<string, Worksheet>(StringComparer.Ordinal);
        foreach (var w in rawWorksheets.EnumerateArray())
        {
            var rich = w.ValueKind is JsonValueKind.Object;
            var code = rich ? Str(w, "code") : w.GetString();
            if (string.IsNullOrEmpty(code)) throw new DataException("worksheets.json holds an entry with no code");
            if (worksheets.ContainsKey(code)) throw new DataException($"duplicate worksheet code {code}");

            var phases = new List<Phase>();
            if (rich && w.TryGetProperty("phases", out var ps) && ps.ValueKind is JsonValueKind.Array)
            {
                foreach (var p in ps.EnumerateArray())
                {
                    // Authored as [low, high]. A single number is widened to both, which is
                    // what the reference does, so a run can always be costed as a range.
                    double?[] minutes = [null, null];
                    if (p.TryGetProperty("minutes", out var mv))
                    {
                        if (mv.ValueKind is JsonValueKind.Array)
                        {
                            var vals = mv.EnumerateArray()
                                .Select(x => x.ValueKind is JsonValueKind.Number ? (double?)x.GetDouble() : null)
                                .ToArray();
                            minutes = vals.Length >= 2 ? [vals[0], vals[1]]
                                : vals.Length == 1 ? [vals[0], vals[0]] : [null, null];
                        }
                        else if (mv.ValueKind is JsonValueKind.Number)
                        {
                            minutes = [mv.GetDouble(), mv.GetDouble()];
                        }
                    }
                    phases.Add(new Phase
                    {
                        Id = Str(p, "id")!,
                        Number = Int(p, "number"),
                        Title = Str(p, "title"),
                        Minutes = minutes,
                        Produces = Str(p, "produces"),
                        RequiresPhases = StrList(p, "requires_phases"),
                        Role = Str(p, "role"),
                        Excerptable = !(p.TryGetProperty("excerptable", out var ex) && ex.ValueKind is JsonValueKind.False),
                        Timing = Str(p, "timing"),
                        TimingNote = Str(p, "timing_note"),
                        Worksheet = code,
                    });
                }
            }

            worksheets[code] = new Worksheet
            {
                Code = code,
                Title = rich ? Str(w, "title") : null,
                Pillar = rich ? CanonElement(Str(w, "pillar")) : null,
                Difficulty = rich ? IntOrNull(w, "difficulty") : null,
                Prerequisites = rich ? StrList(w, "prerequisites") : [],
                Contraindications = rich ? StrList(w, "contraindications") : [],
                Formerly = rich ? Str(w, "formerly") : null,
                FormerlyRebuilt = rich && Bool(w, "formerlyRebuilt"),
                TimeMinutes = rich ? Str(w, "time_minutes") : null,
                GroupSize = rich ? Str(w, "group_size") : null,
                Phases = phases,
                Declared = rich,
            };
        }

        var phasesById = new Dictionary<string, Phase>(StringComparer.Ordinal);
        foreach (var worksheet in worksheets.Values)
        {
            foreach (var phase in worksheet.Phases)
            {
                if (phasesById.ContainsKey(phase.Id)) throw new DataException($"duplicate phase id {phase.Id}");
                phasesById[phase.Id] = phase;
            }
        }

        // D31. The outward registry, built and validated before anything reads it. Every row
        // has to resolve to an action in its own file, on the Outward route, with a worked-if,
        // because a condition the manager is told about and handed a dangling id for is worse
        // than one they are not told about at all.
        var outward = new Dictionary<string, OutwardMove>(StringComparer.Ordinal);
        if (rawOutward.TryGetProperty("rows", out var outwardRows))
        {
            foreach (var row in outwardRows.EnumerateArray())
            {
                var file = Str(row, "issue_file")
                    ?? throw new DataException("outward.json: a row has no issue_file");
                if (outward.ContainsKey(file))
                {
                    throw new DataException($"outward.json: {file} appears twice");
                }
                if (!issueFiles.ContainsKey(file))
                {
                    throw new DataException($"outward.json: {file} is not an issue file");
                }
                var actionId = Str(row, "outward_action");
                if (actionId is null || !actions.TryGetValue(actionId, out var action))
                {
                    throw new DataException(
                        $"outward.json: {file} names outward action {actionId}, which does not resolve");
                }
                if (action.IssueFile != file)
                {
                    throw new DataException(
                        $"outward.json: {file} names {action.Id}, which belongs to {action.IssueFile}");
                }
                if (action.Route != "outward")
                {
                    throw new DataException(
                        $"outward.json: {action.Id} has route \"{action.Route}\", expected outward");
                }
                if (string.IsNullOrWhiteSpace(action.WorkedIf))
                {
                    throw new DataException($"outward.json: {action.Id} has no worked-if");
                }
                var who = Str(row, "who_it_goes_to");
                if (string.IsNullOrWhiteSpace(who))
                {
                    throw new DataException($"outward.json: {file} names no recipient");
                }
                outward[file] = new OutwardMove
                {
                    IssueFile = file, Action = action, WhoItGoesTo = who,
                };
            }
        }
        var capabilityFlags = new Dictionary<string, CapabilityFlag>(StringComparer.Ordinal);
        foreach (var c in rawFlags.EnumerateArray())
        {
            var id = Str(c, "issue_file")!;
            capabilityFlags[id] = new CapabilityFlag
            {
                IssueFile = id,
                CapabilityGap = Bool(c, "capability_gap"),
                Worksheets = StrList(c, "worksheets"),
                Rationale = Str(c, "rationale"),
                LocusDiscoveryAction = Str(c, "locus_discovery_action"),
                ContentPhases = StrList(c, "content_phases"),
                LocusDiscoveryPhases = StrList(c, "locus_discovery_phases"),
            };
        }

        var chipList = new List<Chip>();
        foreach (var (row, track) in allChipRows)
        {
            var locusRaw = Str(row, "locus");
            var polarityRaw = Str(row, "polarity");
            chipList.Add(new Chip
            {
                Id = Str(row, "chip_id")!,
                QuestionId = Int(row, "question_id"),
                QuestionText = Str(row, "question_text"),
                Element = CanonElement(Str(row, "element")),
                Polarity = polarityRaw is not null && PolarityCanon.TryGetValue(polarityRaw, out var p)
                    ? p : polarityRaw ?? "",
                Label = Str(row, "chip") ?? "",
                IssueFile = Str(row, "issue_file") ?? "",
                CapabilityGap = Bool(row, "capability_gap"),
                Locus = locusRaw is null ? null
                    : LocusCanon.TryGetValue(locusRaw, out var l) ? l : locusRaw.ToLowerInvariant(),
                Rung1 = new RungOne
                {
                    Ask = Str(row, "rung_1_ask"),
                    Team = Str(row, "rung_1_team"),
                    Content = Str(row, "rung_1_content"),
                },
                Rung2 = Str(row, "rung_2") ?? "",
                Track = track,
            });
        }

        // The identity a chip's rung one votes under. A positive chip does not route, so it
        // is whichever single action it carries.
        foreach (var chip in chipList)
        {
            chip.TallyActionId = chip.Polarity == "positive"
                ? (chip.Rung1.Ask ?? chip.Rung1.Team ?? chip.Rung1.Content ?? "")
                : (chip.Rung1[Vocabulary.TallyKey] ?? "");
        }

        var chips = new Dictionary<string, Chip>(StringComparer.Ordinal);
        foreach (var chip in chipList) chips[chip.Id] = chip;

        var questions = new Dictionary<int, Question>();
        foreach (var (row, track) in allQuestionRows)
        {
            var id = Int(row, "id");
            questions[id] = new Question
            {
                Id = id,
                Text = Str(row, "text") ?? "",
                Element = CanonElement(Str(row, "element")),
                Track = track,
            };
        }
        foreach (var chip in chipList)
        {
            if (!questions.TryGetValue(chip.QuestionId, out var question))
            {
                throw new DataException(
                    $"chip {chip.Id} names question {chip.QuestionId}, which is not in questions.json");
            }
            if (!question.Chips.TryGetValue(chip.Polarity, out var set))
            {
                throw new DataException($"chip {chip.Id} has polarity \"{chip.Polarity}\", which is not a chip set");
            }
            set.Add(chip.Id);
        }
        // Ordered by the chip id's index, so a member sees N1..N6 in the order authored.
        foreach (var question in questions.Values)
        {
            foreach (var polarity in question.Chips.Keys.ToList())
            {
                question.Chips[polarity].Sort((a, b) => ChipIndex(a).CompareTo(ChipIndex(b)));
            }
        }

        var questionList = questions.Values.OrderBy(q => q.Id).ToList();

        // The individual bank must be reachable by name. Asserted rather than assumed
        // because the failure mode is an empty list rather than an error.
        var individualQuestions = questionList.Where(q => q.Track == "individual").ToList();
        if (rawIndQuestions.GetArrayLength() > 0 && individualQuestions.Count == 0)
        {
            var seen = string.Join(", ", questionList.Select(q => q.Track).Distinct(StringComparer.Ordinal));
            throw new DataException(
                $"questions.json holds {rawIndQuestions.GetArrayLength()} questions and none of them "
                + $"resolved to the individual track. Tracks seen: {seen}.");
        }

        // Every condition an external-locus negative chip can raise has to have somewhere to
        // go, or the outward bucket fills with a condition the engine cannot say anything
        // about.
        var missingOutward = chipList
            .Where(c => c.Locus == "external" && c.Polarity == "negative")
            .Select(c => c.IssueFile)
            .Distinct(StringComparer.Ordinal)
            .Where(f => !outward.ContainsKey(f))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
        if (missingOutward.Count > 0)
        {
            throw new DataException(
                $"{missingOutward.Count} condition(s) can be raised by an external-locus "
                + $"negative chip and have no outward move: {string.Join(", ", missingOutward)}. "
                + "Add them to outward.json.");
        }

        Validate(chipList, actions, issueFiles, questionList, capabilityFlags, worksheets, phasesById);

        var actionsByFile = new Dictionary<string, List<ActionRow>>(StringComparer.Ordinal);
        foreach (var action in actions.Values)
        {
            if (!actionsByFile.TryGetValue(action.IssueFile, out var list))
            {
                list = [];
                actionsByFile[action.IssueFile] = list;
            }
            list.Add(action);
        }

        var chipsByTallyAction = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var chip in chipList)
        {
            if (!chipsByTallyAction.TryGetValue(chip.TallyActionId, out var list))
            {
                list = [];
                chipsByTallyAction[chip.TallyActionId] = list;
            }
            list.Add(chip.Id);
        }

        var bank = new DataBank
        {
            Root = root,
            Chips = chips,
            ChipList = chipList,
            Questions = questions,
            QuestionList = questionList,
            Actions = actions,
            IssueFiles = issueFiles,
            Worksheets = worksheets,
            Phases = phasesById,
            CapabilityFlags = capabilityFlags,
            Outward = outward,
            Manifest = meta,
            RunMinutesCap = runMinutesCap.Value,
            ActionsByFile = actionsByFile,
            ChipsByTallyAction = chipsByTallyAction,
        };
        bank._byTrack = Vocabulary.Tracks.ToDictionary(
            t => t,
            t => questionList.Where(q => q.Track == t).ToList(),
            StringComparer.Ordinal);
        return bank;
    }

    /// <summary>The numeric part of a chip id's suffix, so N1..N6 sort in authored order.</summary>
    private static int ChipIndex(string chipId)
    {
        var dash = chipId.IndexOf('-');
        if (dash < 0 || dash + 2 >= chipId.Length) return 0;
        return int.TryParse(chipId[(dash + 2)..], out var n) ? n : 0;
    }

    private static bool ChipIdShape(string id)
    {
        // ^\d+-[NIP][1-6]$
        var dash = id.IndexOf('-');
        if (dash <= 0 || id.Length != dash + 3) return false;
        for (var i = 0; i < dash; i++) if (!char.IsAsciiDigit(id[i])) return false;
        if (id[dash + 1] is not ('N' or 'I' or 'P')) return false;
        return id[dash + 2] is >= '1' and <= '6';
    }

    /// <summary>
    /// The QA gate from spec 5.4, extended for the three routes, and the joins from spec 2.
    /// </summary>
    /// <remarks>
    /// The exporter already refuses to write data that fails these, so a failure here means
    /// the data was hand-edited or assembled from more than one export run.
    /// </remarks>
    private static void Validate(
        List<Chip> chipList,
        Dictionary<string, ActionRow> actions,
        Dictionary<string, IssueFileRow> issueFiles,
        List<Question> questionList,
        Dictionary<string, CapabilityFlag> capabilityFlags,
        Dictionary<string, Worksheet> worksheets,
        Dictionary<string, Phase> phases)
    {
        var problems = new List<string>();

        foreach (var chip in chipList)
        {
            if (!ChipIdShape(chip.Id)) problems.Add($"chip {chip.Id}: id is not <question>-<N|I|P><1-6>");

            // Spec 4.5: every chip carries a locus tag, and it is as load-bearing as the
            // action mapping. An absent tag is not neutral, because neutral is a claim that
            // the chip locates no cause, and it is what drives the content route in 4.6.
            if (string.IsNullOrEmpty(chip.Locus))
            {
                problems.Add($"chip {chip.Id}: no locus tag. Absent is not neutral (spec 4.5)");
            }
            else if (!Vocabulary.LocusValues.Contains(chip.Locus))
            {
                problems.Add($"chip {chip.Id}: locus \"{chip.Locus}\" is not one of {string.Join(", ", Vocabulary.LocusValues)}");
            }
            if (string.IsNullOrEmpty(chip.Track)) problems.Add($"chip {chip.Id}: no track attribute (spec 7.1a)");

            var rungOneIds = Vocabulary.Routes.Select(r => chip.Rung1[r]).Where(x => !string.IsNullOrEmpty(x)).ToList();

            // Spec 4.4.1: a Positive chip carries a single action and does not route.
            if (chip.Polarity == "positive")
            {
                if (rungOneIds.Count != 1)
                {
                    problems.Add(
                        $"chip {chip.Id}: a Positive chip carries one rung one action and does not "
                        + $"route, this one carries {rungOneIds.Count}");
                }
            }
            else
            {
                if (string.IsNullOrEmpty(chip.Rung1.Ask)) problems.Add($"chip {chip.Id}: no ask route, which is the tally key");
                if (string.IsNullOrEmpty(chip.Rung1.Team)) problems.Add($"chip {chip.Id}: no team route");
                var isGap = capabilityFlags.TryGetValue(chip.IssueFile, out var gapFlag) && gapFlag.CapabilityGap;
                if (isGap && string.IsNullOrEmpty(chip.Rung1.Content))
                {
                    problems.Add($"chip {chip.Id}: {chip.IssueFile} is a capability gap but there is no content route");
                }
                if (!isGap && !string.IsNullOrEmpty(chip.Rung1.Content))
                {
                    problems.Add($"chip {chip.Id}: {chip.IssueFile} is not a capability gap but a content route is set");
                }
            }

            var declaredGap = capabilityFlags.TryGetValue(chip.IssueFile, out var f2) && f2.CapabilityGap;
            if (chip.CapabilityGap != declaredGap)
            {
                problems.Add($"chip {chip.Id}: capability_gap disagrees with capability_flags.json for {chip.IssueFile}");
            }

            actions.TryGetValue(chip.Rung2, out var rung2);
            foreach (var route in Vocabulary.Routes)
            {
                var id = chip.Rung1[route];
                if (string.IsNullOrEmpty(id)) continue;
                if (!actions.TryGetValue(id, out var a))
                {
                    problems.Add($"chip {chip.Id}: {route} route {id} does not resolve");
                    continue;
                }
                if (a.Kind == "Gesture") problems.Add($"chip {chip.Id}: {route} route {id} is a Gesture");
                if (a.Kind != "Conversational") problems.Add($"chip {chip.Id}: {route} route {id} is {a.Kind}, not Conversational");
                if (a.IssueFile != chip.IssueFile) problems.Add($"chip {chip.Id}: {route} route {id} sits in {a.IssueFile}, chip says {chip.IssueFile}");
                if (string.IsNullOrEmpty(a.WorkedIf)) problems.Add($"chip {chip.Id}: {route} route {id} has no worked-if");
                if (string.IsNullOrEmpty(a.NoteType)) problems.Add($"chip {chip.Id}: {route} route {id} has no note type");
                if (id == chip.Rung2) problems.Add($"chip {chip.Id}: {route} route and rung two are the same action");
            }
            if (rung2 is null) { problems.Add($"chip {chip.Id}: rung two {chip.Rung2} does not resolve"); continue; }
            if (rung2.Kind == "Gesture") problems.Add($"chip {chip.Id}: rung two {rung2.Id} is a Gesture");
            if (rung2.Kind != "Structural") problems.Add($"chip {chip.Id}: rung two {rung2.Id} is {rung2.Kind}, not Structural");
            if (rung2.IssueFile != chip.IssueFile) problems.Add($"chip {chip.Id}: rung two sits in {rung2.IssueFile}, chip says {chip.IssueFile}");
            if (string.IsNullOrEmpty(rung2.WorkedIf)) problems.Add($"chip {chip.Id}: rung two {rung2.Id} has no worked-if");
            if (!issueFiles.ContainsKey(chip.IssueFile)) problems.Add($"chip {chip.Id}: issue file {chip.IssueFile} has no entry in issue_files.json");
        }

        foreach (var q in questionList)
        {
            var total = q.Chips["negative"].Count + q.Chips["indifferent"].Count + q.Chips["positive"].Count;
            if (total != 18) problems.Add($"question {q.Id}: {total} chips, expected 18");
            foreach (var polarity in new[] { "negative", "indifferent", "positive" })
            {
                if (q.Chips[polarity].Count != 6)
                {
                    problems.Add($"question {q.Id}: {q.Chips[polarity].Count} {polarity} chips, expected 6");
                }
            }
        }

        foreach (var action in actions.Values)
        {
            if (!issueFiles.ContainsKey(action.IssueFile))
            {
                problems.Add($"action {action.Id}: issue file {action.IssueFile} has no entry in issue_files.json");
            }
        }

        // The content route hands a manager a worksheet. A code that no longer resolves
        // would show them something that is not there, so it halts rather than degrading.
        var deadCodes = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var flag in capabilityFlags.Values)
        {
            if (!issueFiles.ContainsKey(flag.IssueFile))
            {
                problems.Add($"capability_flags: {flag.IssueFile} has no entry in issue_files.json");
            }
            if (flag.CapabilityGap && flag.Worksheets.Count == 0)
            {
                problems.Add($"capability_flags {flag.IssueFile}: a capability gap with no worksheet");
            }
            foreach (var code in flag.Worksheets)
            {
                if (worksheets.ContainsKey(code)) continue;
                if (!deadCodes.TryGetValue(code, out var files)) { files = []; deadCodes[code] = files; }
                files.Add(flag.IssueFile);
            }
        }

        // Spec 4.6. A file that nominates a locus-discovery run must carry the action that
        // delivers it, authored for the locus route rather than borrowed from another one.
        foreach (var flag in capabilityFlags.Values)
        {
            if (flag.LocusDiscoveryPhases.Count == 0) continue;
            var id = flag.LocusDiscoveryAction;
            if (string.IsNullOrEmpty(id))
            {
                problems.Add(
                    $"capability_flags {flag.IssueFile}: a locus-discovery run with no locus-discovery "
                    + "action to deliver it (spec 4.6)");
                continue;
            }
            if (!actions.TryGetValue(id, out var action))
            {
                problems.Add($"capability_flags {flag.IssueFile}: locus-discovery action {id} does not resolve");
                continue;
            }
            if (action.Route != "locus")
            {
                problems.Add(
                    $"capability_flags {flag.IssueFile}: {id} is on the {action.Route ?? "unrouted"} "
                    + "route, not the locus route");
            }
            if (action.IssueFile != flag.IssueFile)
            {
                problems.Add($"capability_flags {flag.IssueFile}: locus-discovery action {id} sits in {action.IssueFile}");
            }
        }

        // Spec 5.1.3c. Every phase declares whether it is a timed block inside the session
        // or work that happens outside it. An untimed phase with no timing tag would be
        // counted as zero minutes, which is exactly what lets a run with two weeks of
        // preparation in front of it past a cap on session length.
        foreach (var worksheet in worksheets.Values)
        {
            foreach (var phase in worksheet.Phases)
            {
                if (string.IsNullOrEmpty(phase.Timing))
                {
                    problems.Add($"phase {phase.Id}: no timing. Every phase declares session or async (spec 5.1.3c)");
                }
                else if (phase.Timing is not ("session" or "async"))
                {
                    problems.Add($"phase {phase.Id}: timing \"{phase.Timing}\" is not session or async");
                }
                // Length checked explicitly: an empty duration would otherwise read as a
                // timed phase, which is the exact confusion 5.1.3c exists to remove.
                var timed = phase.Minutes.Length == 2
                    && phase.Minutes[0] is { } lo && double.IsFinite(lo)
                    && phase.Minutes[1] is { } hi && double.IsFinite(hi);
                if (phase.Timing == "session" && !timed) problems.Add($"phase {phase.Id}: a session phase with no usable duration");
                if (phase.Timing == "async" && timed)
                {
                    problems.Add(
                        $"phase {phase.Id}: an async phase carries a duration. It is not a timed block, "
                        + "and giving it one would put it into the session total (spec 5.1.3c)");
                }
            }
        }

        // Spec 5.1.3b. A phase that surfaces something is never delivered without the phase
        // that responds to it, so every run must be closed under requires_phases.
        foreach (var flag in capabilityFlags.Values)
        {
            foreach (var (kind, ids) in new[]
                     {
                         ("content run", flag.ContentPhases),
                         ("locus-discovery run", flag.LocusDiscoveryPhases),
                     })
            {
                if (ids.Count == 0) continue;
                var inRun = ids.ToHashSet(StringComparer.Ordinal);
                foreach (var id in ids)
                {
                    if (!phases.TryGetValue(id, out var phase))
                    {
                        problems.Add($"capability_flags {flag.IssueFile}: {kind} names phase {id}, which does not exist");
                        continue;
                    }
                    foreach (var required in phase.RequiresPhases)
                    {
                        if (inRun.Contains(required)) continue;
                        problems.Add(
                            $"capability_flags {flag.IssueFile}: the {kind} delivers {id} ({phase.Role}) "
                            + $"without {required}, which it requires alongside it. A phase that surfaces "
                            + "something is never delivered without the phase that responds to it (spec 5.1.3b)");
                    }
                }
            }
            if (flag.CapabilityGap && flag.ContentPhases.Count == 0)
            {
                problems.Add($"capability_flags {flag.IssueFile}: a capability gap with no content run");
            }
            if (flag.LocusDiscoveryPhases.Count == 0)
            {
                problems.Add(
                    $"capability_flags {flag.IssueFile}: no locus-discovery run. Every issue file needs "
                    + "one, including files that are not capability gaps, because any condition can be "
                    + "reported without a locus (spec 4.6)");
            }
        }

        if (deadCodes.Count > 0)
        {
            var lines = deadCodes.OrderBy(e => e.Key, StringComparer.Ordinal)
                .Select(e => $"  {e.Key}  named by {e.Value.Count} file(s): {string.Join(", ", e.Value)}");
            problems.Add(
                $"{deadCodes.Count} worksheet code(s) in capability_flags.json do not exist in the "
                + $"worksheet library:\n{string.Join("\n", lines)}\n"
                + "  The content route would hand a manager a worksheet that is not there.");
        }

        if (problems.Count > 0)
        {
            var shown = string.Join("\n  ", problems.Take(25));
            var more = problems.Count > 25 ? $"\n  ... and {problems.Count - 25} more" : "";
            throw new DataException(
                $"the kit's data failed validation with {problems.Count} problem(s):\n  {shown}{more}\n\n"
                + "Fix the workbook and re-run the exporters. Never edit the JSON.");
        }
    }
}
