using System.Text.Json;

namespace Rrida.Engine;

/// <summary>
/// What a rotation has to be able to answer.
/// </summary>
/// <remarks>
/// <para>
/// An interface rather than a class, because a bench needs to supply its own: the chip
/// bench fixes one question on day one of a six-day cycle so the window holds nothing but
/// the selections being tested. The reference does the same by handing the engine a plain
/// object of the right shape; here the shape has a name.
/// </para>
/// <para>
/// <b>Every day handed to any of these is a rotation day</b>, which under D24 amended is
/// the team's own asked-day index. It is never a calendar day.
/// </para>
/// </remarks>
public interface IRotation
{
    int CycleLength { get; }
    IReadOnlyList<string> TracksAsked { get; }
    int? QuestionIdForDay(int day, string track = "individual");
    Question? QuestionForDay(int day, string track = "individual");
    List<Rotation.Slot> SlotsForDay(int day);
    int? NextOccurrence(int questionId, int fromDay);
    List<int> OccurrencesBetween(int questionId, int from, int to);

    // D27. The signal read is keyed to the issue file rather than to the question that
    // happened to surface it, so a rotation has to answer which questions fill a bucket and
    // when that bucket is next filled.
    int PeriodFor(string track = "individual");
    IReadOnlySet<int> QuestionsCarrying(string? issueFile, string track = "individual");
    int? NextReadDay(string? issueFile, int fromDay, string track = "individual");
}

/// <summary>
/// The question rotation (deterministic spec 4.1).
/// </summary>
/// <remarks>
/// <para>
/// Six-day cycle across the six CLOVER elements, fixed order. The six-day return period is
/// what makes the signal read in spec 9.3 possible, so <see cref="NextOccurrence"/> and
/// <see cref="QuestionIdForDay"/> are the only places that know about it.
/// </para>
/// <para>
/// <b>Every day handed to this class is a rotation day, which under D24 amended is the
/// team's own asked-day index.</b> It is never a calendar day. Both are plain integers, so
/// passing one where the other is wanted is silent, and it was silent five times in the
/// reference. Cross with <see cref="Calendar.RotationDay"/> and nowhere else.
/// </para>
/// </remarks>
public sealed class Rotation : IRotation
{
    public sealed record Entry(int Day, string Element, int QuestionId, int? TeamQuestionId);

    private readonly DataBank _data;
    private readonly List<int> _individualIds;
    private readonly List<int?> _pinnedTeamIds;
    private readonly List<int> _teamOrder;
    private readonly string _scheduleMode;
    private readonly bool _teamScheduled;

    /// <summary>
    /// Which questions fill each issue file, per track.
    /// </summary>
    /// <remarks>
    /// D27. An issue file is the bucket; a question is one of several taps that fill it. The
    /// signal read keys on the bucket, so it has to know every question that can fill it and
    /// not only the one that happened to be asked the day a signal crossed the floor.
    /// <para>
    /// Built once, in the constructor, from the chip bank. Deriving it per read would be the
    /// same work thousands of times a run, and the bank cannot change after load.
    /// </para>
    /// </remarks>
    private readonly Dictionary<string, Dictionary<string, HashSet<int>>> _carriersByTrack;

    public int CycleLength { get; }
    public IReadOnlyList<Entry> Entries { get; }
    public IReadOnlyList<int> QuestionIds => _individualIds;
    public IReadOnlyList<string> Tracks => Vocabulary.Tracks;

    /// <summary>Tracks with a question on every day of the cycle. A half-filled track is not asked.</summary>
    public IReadOnlyList<string> TracksAsked { get; }

    public Rotation(
        DataBank data,
        string individualRotationFile,
        string teamRotationFile,
        string scheduleFile)
    {
        _data = data;

        var rawDoc = JsonDocument.Parse(File.ReadAllText(individualRotationFile));
        var raw = rawDoc.RootElement;
        CycleLength = raw.GetProperty("cycleLength").GetInt32();
        if (!raw.TryGetProperty("questions", out var questions) || questions.ValueKind is not JsonValueKind.Array
            || questions.GetArrayLength() != CycleLength)
        {
            throw new DataException("rotation.json: questions must hold exactly cycleLength entries");
        }
        // D27 retired the six-day guard that stood here. It said the six-day cycle was load
        // bearing for the signal read, and while the read was keyed to the triggering
        // question that was true: the read period was the rotation's length, so a longer
        // rotation put every read further from the move it measured. The read is keyed to
        // the issue file now, so the rotation is free to be any length, and the thing worth
        // checking is that every condition the rotation can raise can also be read back.
        if (CycleLength < 1)
        {
            throw new DataException(
                $"rotation.json: cycleLength must be a whole number from one, not {CycleLength}");
        }

        var entries = new List<Entry>();
        var seenElements = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var entry in questions.EnumerateArray())
        {
            var day = entry.GetProperty("day").GetInt32();
            var element = entry.GetProperty("element").GetString()!;
            var questionId = entry.GetProperty("questionId").GetInt32();
            int? teamQuestionId = entry.TryGetProperty("teamQuestionId", out var tq)
                && tq.ValueKind is JsonValueKind.Number ? tq.GetInt32() : null;

            var q = data.QuestionOf(questionId)
                ?? throw new DataException($"rotation.json: question {questionId} is not in the bank");
            if (q.Element != element)
            {
                throw new DataException($"rotation.json: question {questionId} is {q.Element}, not {element}");
            }
            if (day != index + 1)
            {
                throw new DataException(
                    $"rotation.json: days must run 1..{questions.GetArrayLength()} in order, "
                    + $"and day {index + 1} says {day}");
            }
            // An element used to be allowed once per cycle, which was a six-day rotation's
            // rule written as a general one: with six days and six elements, once each was
            // the only way to cover them. A ninety-question rotation asks Communication
            // eighteen times because the bank holds eighteen Communication questions. What
            // still has to hold is that no element goes unasked, and that is checked below.
            seenElements.Add(q.Element);
            entries.Add(new Entry(day, element, questionId, teamQuestionId));
            index += 1;
        }

        // Checked against the elements the loaded bank actually uses, not against a
        // hard-coded six. Element is an open value (spec 7.1a): the team bank covers
        // drivers that are not CLOVER elements, and a rotation enumerating six would
        // reject that bank rather than rotate it.
        foreach (var element in DataBank.ElementsIn(data.QuestionsForTrack("individual")))
        {
            if (!seenElements.Contains(element))
            {
                throw new DataException($"rotation.json: {element} is missing from the cycle");
            }
        }

        Entries = entries;
        _individualIds = entries.Select(e => e.QuestionId).ToList();

        // The team bank ships its own rotation rather than having one inferred for it.
        var teamRotation = ReadOptional(teamRotationFile);
        _teamOrder = [];

        // A rotation that is present has to be usable, and that is checked here rather than
        // discovered later.
        //
        // The guard further down catches a team bank with no rotation at all. It does not
        // catch a rotation that is there and cannot fill the track: no questionOrder, an
        // empty one, or one naming questions that are not in the team bank. Each of those
        // leaves TrackScheduled quietly answering false, the track drops out of TracksAsked,
        // and the run carries on as a single-track product with nothing said. That is the
        // same half-state reached by a different door, and only the first door was shut.
        if (teamRotation is { } tr)
        {
            var hasOrder = tr.TryGetProperty("questionOrder", out var order)
                && order.ValueKind is JsonValueKind.Array;
            if (!hasOrder || order.GetArrayLength() == 0)
            {
                throw new DataException(
                    $"{Path.GetFileName(teamRotationFile)} is present and carries no usable "
                    + $"questionOrder ({(hasOrder ? "empty" : "absent")}). A rotation that cannot "
                    + "fill the track it belongs to is a half-loaded rotation, not an unscheduled one.");
            }
            foreach (var entry in order.EnumerateArray())
            {
                var id = entry.GetInt32();
                var q = data.QuestionOf(id)
                    ?? throw new DataException(
                        $"{Path.GetFileName(teamRotationFile)}: questionOrder names {id}, "
                        + "which is not in the bank");
                if (q.Track != "team")
                {
                    throw new DataException(
                        $"{Path.GetFileName(teamRotationFile)}: questionOrder names {id}, which is "
                        + $"on the {q.Track} track, not team");
                }
                _teamOrder.Add(id);
            }
        }

        _pinnedTeamIds = [];
        foreach (var entry in entries)
        {
            if (entry.TeamQuestionId is not { } pinned) { _pinnedTeamIds.Add(null); continue; }
            var q = data.QuestionOf(pinned)
                ?? throw new DataException(
                    $"rotation.json day {entry.Day}: teamQuestionId {pinned} is not in the bank");
            if (q.Track != "team")
            {
                throw new DataException(
                    $"rotation.json day {entry.Day}: question {q.Id} is on the {q.Track} track, not team");
            }
            if (q.Element != entry.Element)
            {
                throw new DataException(
                    $"rotation.json day {entry.Day}: team question {q.Id} is {q.Element}, not {entry.Element}");
            }
            _pinnedTeamIds.Add(q.Id);
        }

        // How the two tracks share a day, read as data rather than decided here.
        var schedule = ReadOptional(scheduleFile);
        _scheduleMode = schedule is { } s && s.TryGetProperty("mode", out var m) && m.GetString() is { } mode
            ? mode : "individual_only";
        if (schedule is not { } sched || !sched.TryGetProperty("modes", out var modes)
            || !modes.TryGetProperty(_scheduleMode, out var modeDef))
        {
            throw new DataException($"schedule.json names mode \"{_scheduleMode}\", which it does not define");
        }
        if (!(modeDef.TryGetProperty("implemented", out var impl) && impl.ValueKind is JsonValueKind.True))
        {
            throw new DataException(
                $"schedule.json names mode \"{_scheduleMode}\", which is recorded but not implemented. "
                + "The only implemented mode is both_daily.");
        }

        // Whether the team track walks its own order is decided once, here, and not on
        // every call. Nothing about the mode or the order can change after load: the mode is
        // validated just above and the order is frozen JSON. Re-deriving it per call was the
        // only route by which a fully loaded rotation could answer null for a track it
        // reports as asked. A decision belongs where it is validated.
        _teamScheduled = _scheduleMode == "both_daily" && _teamOrder.Count > 0;

        _carriersByTrack = new Dictionary<string, Dictionary<string, HashSet<int>>>(StringComparer.Ordinal);
        foreach (var track in Vocabulary.Tracks)
        {
            _carriersByTrack[track] = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        }
        foreach (var chip in data.ChipList)
        {
            if (!_carriersByTrack.TryGetValue(chip.Track, out var byFile)) continue;
            if (string.IsNullOrEmpty(chip.IssueFile)) continue;
            if (!byFile.TryGetValue(chip.IssueFile, out var carriers))
            {
                carriers = new HashSet<int>();
                byFile[chip.IssueFile] = carriers;
            }
            carriers.Add(chip.QuestionId);
        }

        TracksAsked = Vocabulary.Tracks.Where(TrackScheduled).ToList();

        // The schedule says both banks are asked every day and the team bank is loaded, so
        // its rotation has to be there. Missing, the old code let the order fall to empty,
        // every day of the team track answer null, the track drop out of TracksAsked, and
        // the run carry on as a single-track product with nothing said.
        if (_scheduleMode == "both_daily" && data.QuestionsForTrack("team").Count > 0 && teamRotation is null)
        {
            throw new DataException(
                $"{data.QuestionsForTrack("team").Count} team questions are loaded and schedule.json "
                + "asks both banks every day, but the team bank ships no rotation. "
                + "Expected rotation/team.json.");
        }

        // A track that is asked must have a question on every day of its cycle. Checked
        // here because the alternative is finding out at day sixteen of a run, in whichever
        // caller happens to be driving a team answer, with a message about a chip.
        foreach (var track in TracksAsked)
        {
            var cycle = track == "team" ? Math.Max(CycleLength, Math.Max(_teamOrder.Count, 1)) : CycleLength;
            for (var day = 1; day <= cycle; day += 1)
            {
                var id = track == "team" ? TeamQuestionForDay(day) : _individualIds[(day - 1) % CycleLength];
                if (id is null)
                {
                    throw new DataException(
                        $"the {track} rotation reports itself as asked and has no question for day "
                        + $"{day} of {cycle}. Schedule mode is \"{_scheduleMode}\", "
                        + (track == "team" ? $"{_teamOrder.Count} team questions are ordered, " : "")
                        + $"and {data.QuestionsForTrack(track).Count} {track} questions are loaded.");
                }
            }
        }
    }

    private static JsonElement? ReadOptional(string file)
    {
        // One read rather than an exists-then-read pair. The pair is a
        // time-of-check-to-time-of-use gap, and the failure it admits is the worst kind: a
        // file that is present reads as absent, the caller takes its "not configured"
        // branch, and the system comes up half built with nothing said.
        string text;
        try
        {
            text = File.ReadAllText(file);
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        catch (IOException err)
        {
            throw new DataException($"{Path.GetFileName(file)} could not be read: {err.Message}");
        }
        try
        {
            return JsonDocument.Parse(text).RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new DataException($"{Path.GetFileName(file)} is not valid JSON");
        }
    }

    /// <summary>
    /// The team question for a rotation day.
    /// </summary>
    /// <remarks>
    /// Under both_daily the team track walks its own ninety questions in Q ID order, one a
    /// day, so any one question returns every ninety days and the fifteen-day condition
    /// cycle repeats six times inside that. It does not share the individual track's
    /// six-day cycle length: the two tracks are on different clocks, and pretending
    /// otherwise is what broke the old element-pairing.
    /// </remarks>
    private int? TeamQuestionForDay(int day)
    {
        var pinned = Entries[(day - 1) % CycleLength].TeamQuestionId;
        if (pinned is not null) return pinned;
        if (!_teamScheduled) return null;
        return _teamOrder[(day - 1) % _teamOrder.Count];
    }

    private bool TrackScheduled(string track)
    {
        if (track == "individual") return _individualIds.All(id => id != 0);
        if (_data.QuestionsForTrack("team").Count == 0) return false;
        // Checked across a full team cycle rather than across the individual one: the two
        // are different lengths and a six-day sample would say nothing about day seven.
        var horizon = Math.Max(CycleLength, Math.Max(_teamOrder.Count, 1));
        for (var day = 1; day <= horizon; day += 1)
        {
            if (TeamQuestionForDay(day) is null) return false;
        }
        return true;
    }

    /// <summary>
    /// The question for a rotation day. <b>Day 1 is the team's first asked day, not a date.</b>
    /// </summary>
    public int? QuestionIdForDay(int day, string track = "individual")
    {
        // Under D24 this is the team's own asked-day index, and a team that has not been
        // asked yet is at zero. Indexing a cycle from zero reaches entry -1, and the
        // failure then lands somewhere with no bearing on the cause.
        if (day < 1) throw new DataException($"a rotation day is a position from one, not {day}");
        int? id;
        if (track == "team")
        {
            id = TeamQuestionForDay(day);
        }
        else
        {
            if (track != "individual") throw new DataException($"unknown track \"{track}\"");
            id = _individualIds[(day - 1) % CycleLength];
        }

        // A track this rotation reports as asked may never answer null.
        //
        // The two were only ever tied together once, at load, and never again, so a null
        // arriving later went out as an answer. The classifier is keyed on the question: a
        // null question finds no locus profile, contributes nothing, and the horizontal axis
        // comes back unlocated where it should have said external. That reaches the manager
        // as a missing Headwinds flag, which D24 records as the one to remember because it
        // is a plausible answer rather than a crash.
        //
        // A track that is NOT asked answering null is not a fault, and is how a caller tells
        // the two apart, so only the contradiction is refused.
        if (id is null && TracksAsked.Contains(track))
        {
            throw new DataException(
                $"the {track} rotation reports itself as asked and answered nothing for rotation "
                + $"day {day}. Schedule mode is \"{_scheduleMode}\", {_teamOrder.Count} team "
                + $"questions are ordered, the team track is {(_teamScheduled ? "" : "not ")}"
                + $"scheduled, and {_data.QuestionsForTrack(track).Count} {track} questions are "
                + "loaded. A rotation that says it asks a track and then answers null for it is a "
                + "loader that half worked, and it says so here rather than reaching a manager as "
                + "a missing flag.");
        }
        return id;
    }

    public Question? QuestionForDay(int day, string track = "individual")
    {
        var id = QuestionIdForDay(day, track);
        return id is null ? null : _data.QuestionOf(id.Value);
    }

    public sealed record Slot(string Track, int QuestionId, Question Question);

    /// <summary>
    /// The day's check-in, in the order it is presented: individual first, then team.
    /// Tracks with no bank behind them are absent rather than null.
    /// </summary>
    public List<Slot> SlotsForDay(int day)
    {
        var slots = new List<Slot>();
        foreach (var track in TracksAsked)
        {
            var id = QuestionIdForDay(day, track);
            if (id is null) continue;
            slots.Add(new Slot(track, id.Value, _data.QuestionOf(id.Value)!));
        }
        return slots;
    }

    public string? TrackOfQuestion(int questionId) => _data.QuestionOf(questionId)?.Track;

    /// <summary>How many days before this track's rotation comes back to where it started.</summary>
    public int PeriodFor(string track = "individual") =>
        track == "team" ? Math.Max(CycleLength, Math.Max(_teamOrder.Count, 1)) : CycleLength;

    /// <summary>The questions whose chips fill one issue file, on one track.</summary>
    public IReadOnlySet<int> QuestionsCarrying(string? issueFile, string track = "individual")
    {
        if (issueFile is null) return EmptyCarriers;
        return _carriersByTrack.TryGetValue(track, out var byFile)
            && byFile.TryGetValue(issueFile, out var carriers) ? carriers : EmptyCarriers;
    }

    private static readonly HashSet<int> EmptyCarriers = new();

    /// <summary>
    /// The first day at or after <paramref name="fromDay"/> on which this condition can be
    /// read at all.
    /// </summary>
    /// <remarks>
    /// D27, and the reason it exists. Keyed to the question, the read period was a property
    /// of the rotation's length rather than of the condition: six days on the demo's
    /// six-question cycle, ninety on the real one. At ninety every cycle's read landed a
    /// working quarter after the move it was measuring, so nothing closed, no rung two was
    /// ever reached, and the comparison that did eventually run held a baseline four months
    /// stale and reported the difference to the manager as whether their conversation
    /// worked. Keyed to the file, the read lands on the next day any question fills that
    /// bucket, which is days away at any rotation length.
    /// </remarks>
    public int? NextReadDay(string? issueFile, int fromDay, string track = "individual")
    {
        var carriers = QuestionsCarrying(issueFile, track);
        if (carriers.Count == 0) return null;
        var period = PeriodFor(track);
        var day = Math.Max(1, fromDay);
        for (var i = 0; i < period; i += 1, day += 1)
        {
            var id = QuestionIdForDay(day, track);
            if (id is { } q && carriers.Contains(q)) return day;
        }
        return null;
    }

    /// <summary>The first rotation day at or after <paramref name="fromDay"/> on which the question is asked.</summary>
    public int? NextOccurrence(int questionId, int fromDay)
    {
        if (!_individualIds.Contains(questionId)) return null;
        var day = Math.Max(1, fromDay);
        // At most one cycle of probing; the rotation guarantees a hit inside six days.
        for (var i = 0; i < CycleLength; i += 1, day += 1)
        {
            if (QuestionIdForDay(day) == questionId) return day;
        }
        return null;
    }

    /// <summary>
    /// Every rotation day in [from, to] on which this question was asked.
    /// </summary>
    /// <remarks>
    /// <b>Answers in rotation space.</b> Fed a calendar window it returns a rotation day
    /// that a caller then reads as a date, and a signal read's baseline is taken on the
    /// wrong day. That was one of the five day-space defects; six of the eight scenarios
    /// changed outcome on it alone.
    /// </remarks>
    public List<int> OccurrencesBetween(int questionId, int from, int to)
    {
        var days = new List<int>();
        for (var day = Math.Max(1, from); day <= to; day += 1)
        {
            if (QuestionIdForDay(day) == questionId) days.Add(day);
        }
        return days;
    }
}
