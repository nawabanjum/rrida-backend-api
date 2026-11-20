namespace Rrida.Engine;

/// <summary>
/// Choosing what the content route delivers. Deterministic spec 5.1.3, 5.1.3a, 5.1.3b, 4.6.
/// </summary>
/// <remarks>
/// <para>
/// <b>The unit of delivery is a phase, not a worksheet</b> (spec 5.1.3a). A worksheet is a
/// sequence of phases, each with its own purpose, duration and output. Delivering a whole
/// exercise to get at a quarter of it wastes an hour of a team's week and makes the action
/// harder to say yes to. So the crosswalk points at a run of phases; the worksheet is the
/// container and the citation.
/// </para>
/// <para>
/// Four rules, and they are deliberately not the same kind of rule. <b>Prerequisites are
/// checkable</b>, so an unmet one is a fact and the route delivers the prerequisite
/// instead. <b>Contraindications are mostly not checkable, and the layer must not try</b>:
/// it does not know who is in a performance process and has no business finding out, so
/// they are stated to the manager and declined by them. <b>Difficulty sequences rather than
/// gates</b>: nothing is refused for being level three. And <b>a phase that surfaces
/// something is never delivered without the phase that responds to it</b>, which is
/// absolute (spec 5.1.3b).
/// </para>
/// </remarks>
public static class Worksheets
{
    /// <summary>
    /// The condition a prerequisite delivery is confirmed against (spec 5.1.4).
    /// </summary>
    /// <remarks>
    /// A content action's worked-if is coupled to the worksheet the action names, the same
    /// way its text is. Deliver V1 in place of V3 and that condition is about an exercise
    /// the team did not run, so the manager is being asked to confirm something that could
    /// not have happened. This is authored once and holds for any prerequisite delivery.
    /// </remarks>
    public const string SubstitutionCondition =
        "the prerequisite exercise ran with the whole team, you took the first turn, and the team "
        + "can say what it was preparing them for";

    /// <summary>
    /// What each contraindication says to a manager.
    /// </summary>
    /// <remarks>
    /// Manager-facing copy, so it lives here rather than in the export: the tokens are the
    /// worksheet library's vocabulary, the sentences are the product's. Anything unlisted
    /// falls back to the token with its underscores removed, so a worksheet that adds a new
    /// one still surfaces something readable rather than disappearing.
    /// </remarks>
    private static readonly Dictionary<string, string> ContraindicationText = new(StringComparer.Ordinal)
    {
        ["no_prior_trust_building"] = "the team has not done any trust-building work together yet",
        ["no_prior_vulnerability_experience"] = "the team has never done an exercise like this before",
        ["unresolved_interpersonal_conflicts"] = "there is an unresolved conflict between people on the team",
        ["active_unresolved_conflict"] = "there is an unresolved conflict running right now",
        ["active_unresolved_conflict_between_specific_members"] =
            "two people on the team are in an unresolved conflict with each other",
        ["team_member_on_pip"] = "anyone on the team is in a performance process",
        ["recent_layoffs_or_restructuring"] = "the team has been through redundancies or a restructure recently",
        ["recent_visible_failure_unresolved"] = "something has gone visibly wrong recently and is still unresolved",
        ["recent_failure_punished_visibly"] = "a recent failure was punished where the team could see it",
        ["active_blame_culture_unreformed"] = "things still tend to end with someone being blamed",
        ["manager_is_primary_source_of_overload_and_unaware"] =
            "you may be the main source of the overload, and have not yet worked that out",
        ["manager_cannot_protect_risk_takers"] =
            "you are not in a position to protect someone who takes a risk and it goes wrong",
        ["experimentation_budgets_cut"] = "there is no budget or time for anything experimental at the moment",
        ["run_with_peer_leaders_only_not_direct_reports"] =
            "the group is your direct reports rather than your peers",
    };

    public static string TextFor(string token) =>
        ContraindicationText.TryGetValue(token, out var text) ? text : token.Replace("_", " ");

    /// <summary>
    /// Has this team met a worksheet prerequisite?
    /// </summary>
    /// <remarks>
    /// Being given a worksheet is not the same as having done it. The prerequisite is met
    /// only where the cycle that delivered it closed, because a cycle that failed means the
    /// signal did not move, and a team that did not get anything out of V1 is not ready for V3.
    /// </remarks>
    public static bool HasCompleted(IReadOnlyDictionary<string, WorksheetRecord>? history, string code) =>
        history is not null && history.TryGetValue(code, out var record) && record.Closed;

    public sealed record ClosedRun(List<Phase> Run, List<(string Phase, string RequiredBy)> Added);

    /// <summary>
    /// Close a run under requires_phases (spec 5.1.3b).
    /// </summary>
    /// <remarks>
    /// Runs to a fixed point rather than one pass, because a companion can itself require
    /// another. Returned in the worksheet's own phase order, so a manager runs it in the
    /// order it was written rather than the order the closure happened to add things.
    /// </remarks>
    public static ClosedRun CloseRun(DataBank data, IEnumerable<string> phaseIds)
    {
        var chosen = new List<string>(phaseIds);
        var inRun = new HashSet<string>(chosen, StringComparer.Ordinal);
        var added = new List<(string, string)>();
        var growing = true;
        while (growing)
        {
            growing = false;
            foreach (var id in chosen.ToList())
            {
                var phase = data.Phase(id);
                if (phase is null) continue;
                foreach (var required in phase.RequiresPhases)
                {
                    if (!inRun.Add(required)) continue;
                    chosen.Add(required);
                    added.Add((required, id));
                    growing = true;
                }
            }
        }
        var run = chosen.Select(data.Phase).OfType<Phase>().ToList();
        run.Sort((a, b) => a.Worksheet == b.Worksheet
            ? a.Number.CompareTo(b.Number)
            : string.CompareOrdinal(a.Worksheet, b.Worksheet));
        return new ClosedRun(run, added);
    }

    public sealed class RunMinutes
    {
        public required double Low { get; init; }
        public required double High { get; init; }
        public required List<string> Untimed { get; init; }
        public required List<Phase> Async { get; init; }
        public required bool Known { get; init; }
    }

    /// <summary>
    /// How long a run takes, or unknown where the library does not say.
    /// </summary>
    /// <remarks>
    /// Unknown is returned as unknown rather than as zero: treating a missing duration as
    /// short is how a run gets past a cap it should have failed, and the cap exists to stop
    /// a team being handed more than they can sit through. Async phases are real work and
    /// are named separately, because a 45-minute session with two weeks of preparation in
    /// front of it is not a 45-minute commitment.
    /// </remarks>
    public static RunMinutes MinutesOf(IReadOnlyList<Phase> run)
    {
        double low = 0, high = 0;
        var async = new List<Phase>();
        var untimed = new List<string>();
        foreach (var phase in run)
        {
            if (phase.Timing == "async") { async.Add(phase); continue; }
            var lo = phase.Minutes.Length > 0 ? phase.Minutes[0] : null;
            var hi = phase.Minutes.Length > 1 ? phase.Minutes[1] : null;
            if (lo is not { } l || !double.IsFinite(l) || hi is not { } h || !double.IsFinite(h))
            {
                untimed.Add(phase.Id);
                continue;
            }
            low += l;
            high += h;
        }
        return new RunMinutes
        {
            Low = low, High = high, Untimed = untimed, Async = async, Known = untimed.Count == 0,
        };
    }

    /// <summary>The worksheets a run draws on, in the order they first appear.</summary>
    public static List<Worksheet> WorksheetsIn(DataBank data, IReadOnlyList<Phase> run)
    {
        var codes = new List<string>();
        foreach (var phase in run) if (!codes.Contains(phase.Worksheet)) codes.Add(phase.Worksheet);
        return codes.Select(data.Worksheet).OfType<Worksheet>().ToList();
    }

    public sealed record Substitution(
        string Wanted, string? WantedTitle, string Delivered, string? DeliveredTitle,
        List<string> Unmet, List<string> Chain, List<string> UnlevelledCandidates, string Reason);

    public sealed class Delivery
    {
        public required bool Ok { get; init; }
        public required string Kind { get; init; }
        public string? Reason { get; init; }
        public List<Phase> Run { get; init; } = [];
        public List<Worksheet> Deliver { get; init; } = [];
        public List<(string Phase, string RequiredBy)> AddedForSafety { get; init; } = [];
        public RunMinutes? Minutes { get; init; }
        public Substitution? Substitution { get; init; }
        public List<(string Token, string Text)> Contraindications { get; init; } = [];
        public List<string> Prerequisites { get; init; } = [];
    }

    /// <summary>
    /// What the content route hands over, as a run of phases.
    /// </summary>
    /// <param name="kind">
    /// "content" for the fix, or "locus_discovery" for the answer to a majority-neutral
    /// signal (spec 4.6). The second is usually the same exercise framed as finding out
    /// rather than as the answer, and it exists on every issue file rather than only on
    /// capability gaps, because any condition can be reported without a locus.
    /// </param>
    public static Delivery ChooseDelivery(
        EngineContext ctx,
        string issueFile,
        IReadOnlyDictionary<string, WorksheetRecord>? history,
        string kind = "content")
    {
        var authored = ctx.Data.PhaseRun(issueFile, kind);
        var label = kind == "locus_discovery" ? "locus-discovery run" : "content run";

        if (authored.Count == 0)
        {
            return new Delivery
            {
                Ok = false, Kind = kind,
                Reason = $"{issueFile} has no {label}, so there is nothing to put in front of the team.",
            };
        }

        // The prerequisite rule runs on the worksheet, because that is what declares one.
        // Where a run draws on more than one worksheet, each has to be satisfied.
        var sources = WorksheetsIn(ctx.Data, authored);
        var unmet = new List<string>();
        foreach (var worksheet in sources)
        {
            foreach (var code in worksheet.Prerequisites)
            {
                if (!HasCompleted(history, code) && !unmet.Contains(code)) unmet.Add(code);
            }
        }

        var run = authored;
        Substitution? substitution = null;

        if (unmet.Count > 0)
        {
            var resolved = ResolveSubstitute(ctx.Data, sources[0], unmet, history, []);
            if (!resolved.Ok)
            {
                return new Delivery { Ok = false, Kind = kind, Reason = resolved.Reason };
            }
            // The substitute is delivered whole. A prerequisite is the thing that builds
            // what the wanted exercise assumes, and excerpting it would deliver a fraction
            // of the foundation and then rely on it.
            run = resolved.Worksheet!.Phases;
            substitution = new Substitution(
                sources[0].Code, sources[0].Title,
                resolved.Worksheet.Code, resolved.Worksheet.Title,
                unmet, resolved.Chain!, resolved.UnlevelledCandidates ?? [],
                $"{sources[0].Title} assumes {string.Join(" and ", unmet)} has been run and closed "
                + $"first, and this team has not done that, so {resolved.Worksheet.Title} is "
                + "delivered instead. The condition has not changed; the starting point has.");
        }

        var closed = CloseRun(ctx.Data, run.Select(p => p.Id));
        var minutes = MinutesOf(closed.Run);

        // Where the required companions push the delivery past what a manager can run, the
        // content route is not available (spec 5.1.3b). Unknown duration cannot be shown to
        // exceed the cap, so it is delivered and flagged rather than refused on a guess. The
        // cap is on session time (spec 5.1.3d): async phases are real work and are stated to
        // the manager, but they are not minutes in the room.
        var cap = ctx.Data.RunMinutesCap;
        if (minutes.Known && minutes.High > cap)
        {
            var text = minutes.Low == minutes.High
                ? $"{minutes.Low} minutes" : $"{minutes.Low} to {minutes.High} minutes";
            if (minutes.Async.Count > 0)
            {
                text += $", plus {minutes.Async.Count} piece(s) of work outside the session";
            }
            return new Delivery
            {
                Ok = false, Kind = kind, Minutes = minutes,
                Reason = $"the {label} for {issueFile} runs to {text} once the phases it "
                    + $"requires alongside it are included, past the {cap} minutes of session time a "
                    + "manager is asked to take on. Better a smaller move than a truncated exercise.",
            };
        }

        var delivered = WorksheetsIn(ctx.Data, closed.Run);
        return new Delivery
        {
            Ok = true,
            Kind = kind,
            Run = closed.Run,
            AddedForSafety = closed.Added,
            Minutes = minutes,
            Deliver = delivered,
            Substitution = substitution,
            // Inherited from the worksheet, because a phase cannot be safer than the
            // exercise it comes from (spec 5.1.3b).
            Contraindications = delivered
                .SelectMany(w => w.Contraindications.Select(t => (Token: t, Text: TextFor(t))))
                .ToList(),
            Prerequisites = delivered.SelectMany(w => w.Prerequisites).ToList(),
        };
    }

    private sealed record Resolved(
        bool Ok, Worksheet? Worksheet, List<string>? Chain, List<string>? UnlevelledCandidates, string? Reason);

    /// <summary>Walk the unmet prerequisites to one that can actually be delivered.</summary>
    private static Resolved ResolveSubstitute(
        DataBank data, Worksheet wanted, List<string> unmet,
        IReadOnlyDictionary<string, WorksheetRecord>? history, HashSet<string> seen)
    {
        // Unknown difficulty sorts last, which is the conservative reading: an unlevelled
        // worksheet is not assumed to be the easy one. Recorded rather than hidden, because
        // two worksheets in the library carry no difficulty and on three files that is what
        // decides which substitute goes out.
        var candidates = unmet
            .Select(data.Worksheet).OfType<Worksheet>()
            .OrderBy(w => w.Difficulty ?? 99)
            .ThenBy(w => w.Code, StringComparer.Ordinal)
            .ToList();
        var unlevelled = candidates.Where(w => w.Difficulty is null).Select(w => w.Code).ToList();

        if (candidates.Count == 0)
        {
            return new Resolved(false, null, null, null,
                $"{wanted.Code} assumes {string.Join(" and ", unmet)} has been done first, and that is "
                + "not in the worksheet library, so there is nothing to deliver in its place.");
        }

        Resolved? firstFailure = null;
        foreach (var candidate in candidates)
        {
            if (seen.Contains(candidate.Code)) continue;
            var deeper = candidate.Prerequisites.Where(c => !HasCompleted(history, c)).ToList();
            if (deeper.Count == 0)
            {
                return new Resolved(true, candidate, [wanted.Code, candidate.Code], unlevelled, null);
            }
            var next = ResolveSubstitute(
                data, candidate, deeper, history,
                new HashSet<string>(seen, StringComparer.Ordinal) { candidate.Code });
            if (next.Ok)
            {
                return new Resolved(true, next.Worksheet, [wanted.Code, .. next.Chain!], next.UnlevelledCandidates, null);
            }
            firstFailure ??= next;
        }
        return firstFailure ?? new Resolved(false, null, null, null,
            $"{wanted.Code} assumes {string.Join(" and ", unmet)}, and none of those can be delivered "
            + "to this team either.");
    }
}

/// <summary>
/// Which worksheets a team has been given, and whether the cycle that carried one closed.
/// </summary>
/// <remarks>
/// Prerequisites are checkable precisely because this exists (spec 5.1.3), and being given
/// a worksheet is not the same as having got anything out of it, so the closed flag is what
/// a prerequisite check reads.
/// </remarks>
public sealed class WorksheetRecord
{
    public required string Code { get; init; }
    public string? Title { get; init; }
    public int DeliveredDay { get; set; }
    public int FirstDeliveredDay { get; init; }
    public string? CycleId { get; set; }
    public bool Closed { get; set; }
    public int? ClosedDay { get; set; }
}
