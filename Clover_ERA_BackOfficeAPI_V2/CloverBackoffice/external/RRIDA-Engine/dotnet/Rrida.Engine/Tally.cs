namespace Rrida.Engine;

/// <summary>
/// One action's accumulated evidence inside the window.
/// </summary>
/// <remarks>
/// A vote is one (member, day, action) triple: a member contributes at most once per day
/// and at most once per micro-action per day, so two chips sharing a rung one action are
/// one vote from that member, not two (spec 4.3). Distinct contributors are counted across
/// the window, not per day (spec 6.3).
/// </remarks>
public sealed class TallyEntry
{
    public required string ActionId { get; init; }
    public int Votes { get; set; }
    public HashSet<string> Contributors { get; } = new(StringComparer.Ordinal);
    public double SeverityWeight { get; set; }
    public SortedSet<int> QuestionIds { get; } = [];
    public string IssueFile { get; set; } = "";

    /// <summary>chipId to the members who picked it, so the panel can show what converged.</summary>
    public Dictionary<string, HashSet<string>> ChipContributors { get; } = new(StringComparer.Ordinal);

    /// <summary>Insertion order of chip ids, so iteration is reproducible across runtimes.</summary>
    public List<string> ChipOrder { get; } = [];

    public int? FirstCrossedDay { get; set; }
}

public sealed class WindowedTallies
{
    public required int Day { get; init; }
    public required int From { get; init; }
    public required int To { get; init; }
    public required Floor Floor { get; init; }

    /// <summary>Insertion-ordered, because the reference's Map is and the order reaches the output.</summary>
    public required IReadOnlyDictionary<string, List<TallyEntry>> Tallies { get; init; }

    public required IReadOnlyList<(int Day, int QuestionId)> QuestionsInWindow { get; init; }
    public required int ResponseCount { get; init; }

    public List<TallyEntry> Of(string name) => Tallies[name];
}

public sealed class Floor
{
    public required int Enrolled { get; init; }
    public required int Percentage { get; init; }
    public required int AbsoluteMinimum { get; init; }
    public required int Required { get; init; }
    public required string GovernedBy { get; init; }
    public required double SmallestEligibleShare { get; init; }
    public required bool AskRouteReachable { get; init; }
    public required string Arithmetic { get; init; }
}

/// <summary>
/// Signal aggregation: the window, the three tallies, the floor, and selection.
/// Deterministic spec sections 6.1 to 6.4.
/// </summary>
/// <remarks>
/// Everything here is a pure function of the team's recorded responses and the day. No
/// state is cached, so any day's arithmetic can be asked for at any time and gives the same
/// answer the engine acted on.
/// </remarks>
public static class Tally
{
    /// <summary>
    /// How strongly something came through, said qualitatively.
    /// </summary>
    /// <remarks>
    /// The layer knows exactly how many distinct people contributed, because that is what the
    /// floor is measured against. <b>The manager is never told.</b> The signal is anonymous
    /// and aggregated: a count invites them to work out who, and on a team of seven a number
    /// like two is close to naming. Explaining the threshold is worse again, because it turns
    /// a reading into a formula the manager starts playing against.
    /// <para>
    /// Here rather than on the simulation because it is a statement about a contributor count
    /// and a floor, which is this module's subject, and because the watch list needs it and
    /// the team cannot reach the simulation. Words and never a number.
    /// </para>
    /// </remarks>
    public static string SignalStrength(int contributors, int floor)
    {
        if (contributors >= floor * 2) return "came through strongly, and kept coming through";
        if (contributors > floor) return "came through clearly, from more than one direction";
        if (contributors >= floor) return "came through consistently rather than once";
        return "is the closest thing to a pattern, but it is not yet consistent enough to act on";
    }

    /// <summary>
    /// The deliverable buckets, and only those.
    /// </summary>
    /// <remarks>
    /// D31 added a fourth, outward, which is built and never selected from. It is
    /// deliberately not in here: this list is consumed by the succession rule through
    /// SpreadAndChurn and FileQuestionSpread, both of which answer what to hand the manager
    /// next, and a condition nobody can be handed must not get a vote in that. Use
    /// <see cref="AllTallies"/> to walk every bucket, which is what a reporting surface
    /// wants and what the conformance fixtures write down.
    /// </remarks>
    public static readonly string[] Tallies = ["fix", "build", "yellow"];

    /// <summary>Every bucket, deliverable or not. For reporting, never for selection.</summary>
    public static readonly string[] AllTallies = ["fix", "build", "yellow", "outward"];

    /// <summary>
    /// The floor (spec 6.3): 33 per cent of enrolled members rounded down, with an absolute
    /// minimum of 2. A team of 6 gives 1 from the percentage, so 2 governs. A team of 10 gives 3.
    /// </summary>
    public static Floor ComputeFloor(int enrolled, EngineConfig config)
    {
        var percentage = (int)Math.Floor(config.FloorPercentage * enrolled);
        var minimum = config.FloorAbsoluteMinimum;
        var required = Math.Max(minimum, percentage);
        var smallestShare = enrolled > 0 ? (double)required / enrolled : 1.0;
        return new Floor
        {
            Enrolled = enrolled,
            Percentage = percentage,
            AbsoluteMinimum = minimum,
            Required = required,
            GovernedBy = percentage > minimum ? "percentage"
                : percentage < minimum ? "absolute minimum"
                : $"both, they agree at {minimum}",
            // Spec 5.1.5. The smallest eligible signal is the floor itself, so the smallest
            // possible share is required/enrolled. At three enrolled the minimum governs,
            // which makes that 0.67: every eligible signal on a team of three is above half
            // and the ask route cannot fire there at all. Four is the smallest size at which
            // it can. Benign, and computed rather than discovered: a manager of three who
            // asks why they never get a one-to-one action deserves an answer.
            SmallestEligibleShare = smallestShare,
            AskRouteReachable = smallestShare <= 0.5,
            Arithmetic = $"floor({config.FloorPercentage} x {enrolled}) = {percentage}; "
                + $"max({minimum}, {percentage}) = {required}",
        };
    }

    /// <summary>
    /// Build all three tallies over the window.
    /// </summary>
    /// <param name="deliveryTracks">
    /// Which tracks may cast a vote. Injected rather than assumed, and with no default, for
    /// the same reason the state source is: a default here is a decision about which bank
    /// drives delivery, and that decision is not the engine's.
    /// </param>
    /// <param name="locusFilter">
    /// D21. Arrives with the resolved state and has no default. <c>null</c> means no filter
    /// and has to be said; a filter excluding "external" means the Headwinds flag is raised.
    /// The engine does not decide which causes a team owns.
    /// </param>
    public static WindowedTallies Build(
        EngineContext ctx,
        Team team,
        int day,
        IReadOnlyList<string> deliveryTracks,
        LocusFilter? locusFilter,
        bool locusFilterWasGiven)
    {
        if (!locusFilterWasGiven)
        {
            throw new InvalidOperationException(
                "Build needs a locusFilter. It is passed down with the resolved state, where null "
                + "means no filter and an exclude of [\"external\"] means the Headwinds flag is "
                + "raised. The engine does not decide which causes a team owns.");
        }
        if (deliveryTracks.Count == 0)
        {
            throw new InvalidOperationException(
                "Build needs deliveryTracks. Which question tracks may cast a vote is a "
                + "composition-root decision, not an engine default: both tracks are asked and both "
                + "produce atoms, but only some of them drive delivery.");
        }

        // The rolling window (spec 6.1), counted in the team's own days. D24: seven days a
        // team answered on, not seven dates. On a five-day week those seven span nine or ten
        // calendar days, and counting dates instead would put five days of answers in a
        // window calibrated for seven.
        var from = team.AskedWindowFrom(day, ctx.Config.WindowDays);
        var floor = ComputeFloor(team.EnrolledCountOn(day), ctx.Config);

        // D31. Four buckets, not three. The three above are keyed on the face: red and
        // orange fill fix, green fills build, yellow fills yellow. The fourth is keyed on the
        // chip instead, because what puts a condition in it is not how anyone felt but where
        // they said the cause sits. Nothing selects from it: LiveTally maps the three states
        // to the three face buckets and has no fourth entry, so EligibleActions is never
        // asked for this one.
        var tallies = AllTallies.ToDictionary(
            name => name,
            _ => new List<TallyEntry>(),
            StringComparer.Ordinal);
        var index = AllTallies.ToDictionary(
            name => name,
            _ => new Dictionary<string, TallyEntry>(StringComparer.Ordinal),
            StringComparer.Ordinal);

        // D24 amended. The rotation answers in the team's own asked-day index, so a calendar
        // day has to be translated before it is asked and not after. Fed dates it named the
        // wrong questions for every day of a five-day week, which reached the succession
        // rule through the file-question spread and changed which file was drawn next.
        var questionsInWindow = new List<(int Day, int QuestionId)>();
        for (var d = from; d <= day; d += 1)
        {
            if (!team.IsAskedOn(d)) continue;
            var qid = ctx.Rotation.QuestionIdForDay(Calendar.RotationDay(team, d));
            if (qid is not null) questionsInWindow.Add((d, qid.Value));
        }

        var responses = team.ResponsesBetween(from, day)
            .Where(r => deliveryTracks.Contains(r.Track))
            .ToList();

        foreach (var response in responses)
        {
            var bucket = Vocabulary.SmileyTally[response.Smiley];
            var tally = index[bucket];

            // Collapse this member's chips for this day down to distinct rung one actions.
            var perAction = new List<(string ActionId, List<Chip> Chips)>();
            // D31. And separately, down to distinct issue files for the outward bucket.
            var perOutward = new List<(string IssueFile, List<Chip> Chips)>();
            var perOutwardIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            var perActionIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var chipId in response.Chips)
            {
                var chip = ctx.Data.Chip(chipId);
                if (chip is null) continue;
                // D21. The Headwinds flag arrives with the state and filters rather than
                // suppresses: a chip naming a cause outside the team casts no vote, and the
                // rest of the answer still does. Interim, until the upward route exists.
                // D31. A negative chip naming a cause outside the team goes to the outward
                // bucket and to no other, whatever the face said and whether or not Headwinds
                // is up. Before the registry existed there was nowhere for it to go, which is
                // what D21's filter was standing in for.
                if (chip.Locus == "external" && chip.Polarity == "negative")
                {
                    if (!perOutwardIndex.TryGetValue(chip.IssueFile, out var oat))
                    {
                        oat = perOutward.Count;
                        perOutwardIndex[chip.IssueFile] = oat;
                        perOutward.Add((chip.IssueFile, []));
                    }
                    perOutward[oat].Chips.Add(chip);
                    continue;
                }
                // D21, for what is left of it. The flag arrives with the state and filters
                // rather than suppresses: a chip naming a cause outside the team casts no
                // vote, and the rest of the answer still does. With the outward bucket in
                // place this now reaches only external chips that are not negative, where
                // there is no upward move to offer.
                if (locusFilter is not null && chip.Locus is not null && locusFilter.Exclude.Contains(chip.Locus))
                {
                    continue;
                }
                // The vote goes on the chip's tally key, which is its ask-route action
                // (spec 4.2, build spec 4.4.1). Ask is the only one of the three routes that
                // is unique per chip; team is one per condition and content one per
                // capability-gap file, so counting on either would collapse distinct
                // conditions before the diagnosis has been made. The route swap happens
                // once, at selection, after the winner is known.
                if (!perActionIndex.TryGetValue(chip.TallyActionId, out var at))
                {
                    at = perAction.Count;
                    perActionIndex[chip.TallyActionId] = at;
                    perAction.Add((chip.TallyActionId, []));
                }
                perAction[at].Chips.Add(chip);
            }

            foreach (var (actionId, chips) in perAction)
            {
                if (!tally.TryGetValue(actionId, out var entry))
                {
                    entry = new TallyEntry { ActionId = actionId };
                    tally[actionId] = entry;
                    tallies[bucket].Add(entry);
                }
                entry.Votes += 1; // one vote from this member on this day, however many chips
                entry.Contributors.Add(response.MemberId);
                entry.SeverityWeight += Vocabulary.Severity[response.Smiley];
                entry.QuestionIds.Add(response.QuestionId);
                entry.IssueFile = ctx.Data.Action(actionId)!.IssueFile;
                foreach (var chip in chips)
                {
                    if (!entry.ChipContributors.TryGetValue(chip.Id, out var members))
                    {
                        members = new HashSet<string>(StringComparer.Ordinal);
                        entry.ChipContributors[chip.Id] = members;
                        entry.ChipOrder.Add(chip.Id);
                    }
                    members.Add(response.MemberId);
                }
                if (entry.FirstCrossedDay is null && entry.Contributors.Count >= floor.Required)
                {
                    entry.FirstCrossedDay = response.Day;
                }
            }

            // D31. The outward bucket is keyed on the issue file, not on an action. The three
            // face buckets key on the chip's ask action because the diagnosis has not been
            // made yet and ask is the only route unique per chip. There is no diagnosis to
            // make here: the whole claim is that the cause is outside the team, and the
            // condition has exactly one outward move, so the file is the key and the registry
            // supplies the move.
            var outwardIndex = index["outward"];
            foreach (var (issueFile, chips) in perOutward)
            {
                if (!outwardIndex.TryGetValue(issueFile, out var entry))
                {
                    entry = new TallyEntry { ActionId = issueFile, IssueFile = issueFile };
                    outwardIndex[issueFile] = entry;
                    tallies["outward"].Add(entry);
                }
                entry.Votes += 1;
                entry.Contributors.Add(response.MemberId);
                entry.SeverityWeight += Vocabulary.Severity[response.Smiley];
                entry.QuestionIds.Add(response.QuestionId);
                foreach (var chip in chips)
                {
                    if (!entry.ChipContributors.TryGetValue(chip.Id, out var members))
                    {
                        members = new HashSet<string>(StringComparer.Ordinal);
                        entry.ChipContributors[chip.Id] = members;
                        entry.ChipOrder.Add(chip.Id);
                    }
                    members.Add(response.MemberId);
                }
                if (entry.FirstCrossedDay is null && entry.Contributors.Count >= floor.Required)
                {
                    entry.FirstCrossedDay = response.Day;
                }
            }
        }

        return new WindowedTallies
        {
            Day = day,
            From = from,
            To = day,
            Floor = floor,
            Tallies = tallies,
            QuestionsInWindow = questionsInWindow,
            ResponseCount = responses.Count,
        };
    }

    /// <summary>Everything in one tally that has cleared the floor.</summary>
    public static List<TallyEntry> EligibleActions(WindowedTallies windowed, string tallyName) =>
        windowed.Of(tallyName).Where(e => e.Contributors.Count >= windowed.Floor.Required).ToList();

    public sealed record TieBreak(string Rule, string Detail);

    public sealed class Selection
    {
        public required TallyEntry Winner { get; init; }
        public required List<TallyEntry> Ranked { get; init; }
        public required TieBreak? TieBreak { get; init; }
    }

    /// <summary>
    /// Select the active action from a candidate set (spec 6.4).
    /// </summary>
    /// <remarks>
    /// Highest vote count wins. No composite score. Ties break on aggregate severity
    /// weight, then on whether the contributing chips came from more than one of the week's
    /// questions, then on which crossed the floor first, then on action id so the order is
    /// total and runs are reproducible.
    /// </remarks>
    public static Selection? SelectAction(IReadOnlyList<TallyEntry> candidates)
    {
        if (candidates.Count == 0) return null;

        var ranked = candidates.ToList();
        ranked.Sort(CompareCandidates);
        var winner = ranked[0];
        var runnerUp = ranked.Count > 1 ? ranked[1] : null;

        TieBreak? tieBreak = null;
        if (runnerUp is not null && runnerUp.Votes == winner.Votes)
        {
            if (Math.Abs(runnerUp.SeverityWeight - winner.SeverityWeight) > 0)
            {
                tieBreak = new TieBreak(
                    "aggregate severity weight",
                    $"{winner.ActionId} carried {Fmt1(winner.SeverityWeight)} against "
                    + $"{Fmt1(runnerUp.SeverityWeight)} for {runnerUp.ActionId}");
            }
            else if (winner.QuestionIds.Count != runnerUp.QuestionIds.Count)
            {
                tieBreak = new TieBreak(
                    "contributing chips came from more than one of the week's questions",
                    $"{winner.ActionId} drew from {winner.QuestionIds.Count} question(s), "
                    + $"{runnerUp.ActionId} from {runnerUp.QuestionIds.Count}");
            }
            else
            {
                tieBreak = new TieBreak(
                    "first to cross the floor in the window",
                    $"{winner.ActionId} crossed on day {winner.FirstCrossedDay}, "
                    + $"{runnerUp.ActionId} on day {runnerUp.FirstCrossedDay}");
            }
        }

        return new Selection { Winner = winner, Ranked = ranked, TieBreak = tieBreak };
    }

    /// <summary>One decimal place, as the reference's toFixed(1) writes it.</summary>
    private static string Fmt1(double value) =>
        value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

    private static int CompareCandidates(TallyEntry a, TallyEntry b)
    {
        if (b.Votes != a.Votes) return b.Votes - a.Votes;
        if (b.SeverityWeight != a.SeverityWeight) return b.SeverityWeight > a.SeverityWeight ? 1 : -1;
        var aMulti = a.QuestionIds.Count > 1 ? 1 : 0;
        var bMulti = b.QuestionIds.Count > 1 ? 1 : 0;
        if (bMulti != aMulti) return bMulti - aMulti;
        var aFirst = a.FirstCrossedDay ?? int.MaxValue;
        var bFirst = b.FirstCrossedDay ?? int.MaxValue;
        if (aFirst != bFirst) return aFirst - bFirst;
        // A total order, so runs are reproducible. The reference uses localeCompare here;
        // every action id in both banks is ASCII of one shape, and ordinal agrees with it on
        // all 2,919 of them (asserted by a test), so ordinal is the honest port and does not
        // depend on the machine's locale.
        return string.CompareOrdinal(a.ActionId, b.ActionId);
    }

    public sealed record TriggeringChip(string ChipId, Chip Chip, int Contributors);

    /// <summary>
    /// The triggering chip for a winning action: the chip that drew the most distinct
    /// contributors to it. This is the chip whose question the signal is read against
    /// (spec 9.3), so it needs to be the one carrying the most of the signal.
    /// </summary>
    public static TriggeringChip? Triggering(EngineContext ctx, TallyEntry entry)
    {
        TriggeringChip? best = null;
        foreach (var chipId in entry.ChipOrder)
        {
            var members = entry.ChipContributors[chipId];
            var chip = ctx.Data.Chip(chipId)!;
            var candidate = new TriggeringChip(chipId, chip, members.Count);
            if (best is null
                || candidate.Contributors > best.Contributors
                || (candidate.Contributors == best.Contributors
                    && string.CompareOrdinal(chipId, best.ChipId) < 0))
            {
                best = candidate;
            }
        }
        return best;
    }

    public sealed class FileSpread
    {
        public required string IssueFile { get; init; }
        public required List<int> QuestionIds { get; init; }
        public required int Count { get; init; }
        public required bool DrawsFromMoreThanOne { get; init; }
        public required Dictionary<string, int> VotesByTally { get; init; }
    }

    /// <summary>
    /// Which of the week's questions a given issue file is drawing votes from.
    /// </summary>
    /// <remarks>
    /// The succession rule (spec 8.2) releases a file that draws from only one question,
    /// because that file is that question's topic rather than a condition the team reports
    /// whatever they are asked. Votes are counted across all three tallies: the same
    /// condition reported at a different intensity is still the same condition.
    /// </remarks>
    public static FileSpread FileQuestionSpread(WindowedTallies windowed, string issueFile)
    {
        var questionIds = new SortedSet<int>();
        var perTally = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var name in Tallies)
        {
            foreach (var entry in windowed.Of(name))
            {
                if (entry.IssueFile != issueFile) continue;
                foreach (var qid in entry.QuestionIds) questionIds.Add(qid);
                perTally[name] = perTally.GetValueOrDefault(name) + entry.Votes;
            }
        }
        return new FileSpread
        {
            IssueFile = issueFile,
            QuestionIds = questionIds.ToList(),
            Count = questionIds.Count,
            DrawsFromMoreThanOne = questionIds.Count > 1,
            VotesByTally = perTally,
        };
    }

    public sealed record SpreadChurn(int Spread, int Churn, int CyclesCompared);

    /// <summary>
    /// File spread and file churn (spec 11). Logged from day one, never thresholded: the
    /// threshold that indicates confusion is set from a live baseline, not guessed.
    /// </summary>
    public static SpreadChurn SpreadAndChurn(WindowedTallies windowed, IReadOnlyList<Cycle> cycleHistory)
    {
        var files = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in Tallies)
        {
            foreach (var entry in windowed.Of(name)) files.Add(entry.IssueFile);
        }
        var churn = 0;
        for (var i = 1; i < cycleHistory.Count; i += 1)
        {
            if (cycleHistory[i].IssueFile != cycleHistory[i - 1].IssueFile) churn += 1;
        }
        return new SpreadChurn(files.Count, churn, Math.Max(0, cycleHistory.Count - 1));
    }
}

/// <summary>
/// D21. The Headwinds flag, handed down with the resolved state as a filter rather than a
/// state.
/// </summary>
/// <remarks>
/// A team whose members are naming causes outside the team is not a team with nothing to be
/// given; it is a team whose external causes are not its manager's to fix, and the filter
/// says so without silencing the week. "Excluded" is the interim: it becomes "routed to the
/// upward route" once that route exists in the library.
/// </remarks>
public sealed class LocusFilter
{
    public required IReadOnlyList<string> Exclude { get; init; }
}
