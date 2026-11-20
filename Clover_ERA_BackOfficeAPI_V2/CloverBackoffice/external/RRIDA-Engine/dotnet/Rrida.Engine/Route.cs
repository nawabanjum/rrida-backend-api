namespace Rrida.Engine;

public sealed class LocusMix
{
    public int Internal { get; set; }
    public int External { get; set; }
    public int Neutral { get; set; }
    public int Untagged { get; set; }
    public int Total => Internal + External + Neutral + Untagged;

    /// <summary>
    /// A strict majority. An even split is not a concentration: the rule is about a
    /// concentration of neutral being a finding, not about neutral merely being present.
    /// </summary>
    public bool MajorityNeutral => Total > 0 && Neutral * 2 > Total;
}

/// <summary>D30. Where the fortnightly session cap changed the move, and by how much.</summary>
public sealed record CadenceHold(string From, string To, int Since, int CadenceDays, string Reason);

public sealed class Routing
{
    public required string Route { get; init; }
    public required string? ActionId { get; init; }
    public required string? StoredIn { get; init; }
    public required double Share { get; init; }
    public required string ShareText { get; init; }
    public required double Threshold { get; init; }
    public required bool CapabilityGap { get; init; }
    public required bool AboveThreshold { get; init; }
    public required string Reason { get; init; }
    public required LocusMix Locus { get; init; }

    public (string From, string? Kind)? FellBack { get; init; }

    /// <summary>
    /// D30. Set when the fortnightly cap turned a session into a conversation. Reported
    /// rather than silent: a manager who was handed a conversation where the share said
    /// session is owed the reason, and the machinery panel is where it is checked.
    /// </summary>
    public CadenceHold? CadenceHeld { get; init; }

    public string? RunKind { get; init; }
    public Worksheets.Delivery? WorksheetChoice { get; init; }
    public List<Worksheet> WorksheetsDelivered { get; init; } = [];
    public List<Phase> Phases { get; init; } = [];
    public Worksheets.RunMinutes? RunMinutes { get; init; }
    public List<(string Token, string Text)> Contraindications { get; init; } = [];

    /// <summary>
    /// Where a substitution fired, what the manager confirms is the substitution condition
    /// rather than the action's own, which is about an exercise the team did not run
    /// (spec 5.1.4). Null everywhere else.
    /// </summary>
    public string? SubstitutionCondition { get; init; }
}

/// <summary>
/// Rung one route selection. Deterministic spec 5.1.1, build spec 4.4.2.
/// </summary>
/// <remarks>
/// <para>
/// A one-to-one is an information-gathering move. It is the right first step when two of
/// nine people reported something and the manager needs to know more. It is the wrong first
/// step when seven of nine reported it, because the team has already said it, and asking
/// one person reads as not having listened. Some conditions are not solved by the manager
/// talking to anyone at all: those are capability gaps, and the first move is to put
/// something in front of the team.
/// </para>
/// <para>
/// So every routed chip carries three rung one actions and the layer picks between them
/// with arithmetic it already holds. This is not a judgement, and Eran is told the result
/// rather than asked for it.
/// </para>
/// </remarks>
public static class Route
{
    /// <summary>
    /// D30. The least time that may pass between two worksheet sessions, in the team's own
    /// days.
    /// </summary>
    /// <remarks>
    /// Two of the team's working weeks. The rule is "one worksheet session per two-week
    /// cycle, never more", and until D30 nothing in the engine enforced it: it was held up
    /// by the authored data being thin, and the moment the data looked like people it broke.
    /// Measured on eight teams over ninety asked days, three of them were running a session
    /// every 1.6 to 1.8 weeks.
    /// <para>
    /// Counted in the team's own days, like everything else since D24, so a team that works
    /// five days a week gets two of its weeks and not ten dates.
    /// </para>
    /// </remarks>
    public const int SessionCadenceDays = 10;

    /// <summary>
    /// The locus balance of the chips that actually converged on the winning action.
    /// </summary>
    /// <remarks>
    /// Spec 4.6 turns on "the contributing chips on the winning action", not on the
    /// triggering chip alone and not on the whole file. Counted over distinct chips rather
    /// than over votes: two people picking the same neutral chip have named one unlocated
    /// cause between them, and counting the votes would let a popular chip decide the route
    /// on its own.
    /// </remarks>
    public static LocusMix MixOf(DataBank data, IEnumerable<string> chipIds)
    {
        var mix = new LocusMix();
        foreach (var id in chipIds.Distinct(StringComparer.Ordinal))
        {
            var chip = data.Chip(id);
            if (chip is null) continue;
            switch (chip.Locus)
            {
                case "internal": mix.Internal += 1; break;
                case "external": mix.External += 1; break;
                case "neutral": mix.Neutral += 1; break;
                default: mix.Untagged += 1; break;
            }
        }
        return mix;
    }

    /// <summary>
    /// Choose the route for a winning action.
    /// </summary>
    /// <param name="contributors">The distinct-contributor count on the winning action.</param>
    /// <param name="enrolled">The enrolled members, which the share is taken over.</param>
    /// <param name="contributingChipIds">
    /// Every chip that fed the winning action. Spec 4.6 turns on these, not on the
    /// triggering chip alone.
    /// </param>
    public static Routing Select(
        EngineContext ctx,
        Chip chip,
        int contributors,
        int enrolled,
        IReadOnlyDictionary<string, WorksheetRecord>? history = null,
        IReadOnlySet<string>? declined = null,
        IReadOnlyList<string>? contributingChipIds = null,
        int? lastSessionAskedDay = null,
        int? askedDayNow = null,
        int cadenceDays = SessionCadenceDays,
        double? threshold = null)
    {
        if (enrolled == 0) throw new InvalidOperationException("route selection needs the enrolled count");
        var line = threshold ?? ctx.Data.ShareThreshold;
        var share = (double)contributors / enrolled;
        var capabilityGap = ctx.Data.IsCapabilityGap(chip.IssueFile);
        var locus = MixOf(ctx.Data, contributingChipIds ?? [chip.Id]);

        // A Positive chip carries one action and does not route (spec 4.4.1). The build
        // tally is about extending something that is working; there is no diagnosis to make.
        if (chip.Polarity == "positive")
        {
            var stored = Vocabulary.Routes.FirstOrDefault(r => !string.IsNullOrEmpty(chip.Rung1[r]));
            return new Routing
            {
                Route = "none",
                ActionId = stored is null ? null : chip.Rung1[stored],
                StoredIn = stored,
                Share = share,
                ShareText = $"{contributors} of {enrolled}",
                Threshold = line,
                CapabilityGap = capabilityGap,
                AboveThreshold = share > line,
                Reason = "a Positive chip carries a single action and does not route; the build tally "
                    + "is about extending what is working, so there is no diagnosis to make",
                Locus = MixOf(ctx.Data, [chip.Id]),
            };
        }

        string route;
        string reason;
        // Spec 4.6, and it takes precedence over contributor share. A neutral tag means the
        // chip locates no cause. One neutral answer is noise; a concentration of them is a
        // finding in its own right, and the team is reporting something real it cannot yet
        // place. The response is not to guess the locus: asking one person will not help
        // because they cannot name it either, and telling the team will not help because
        // there is nothing to tell them yet. So the move is to go and find it with the team,
        // which is a content delivery by definition.
        if (locus.MajorityNeutral)
        {
            route = "content";
            reason = "the chips that converged on this action mostly locate no cause, so the team is "
                + "reporting something real that it cannot yet place. Asking one person will not help "
                + "because they cannot name it either, and telling the team will not help because "
                + "there is nothing to tell them yet. The move is to find it with them";
        }
        else if (share <= line)
        {
            route = "ask";
            reason = "fewer than half the team raised it, so the manager does not yet know enough "
                + "and the conversation is how they find out";
        }
        else if (!capabilityGap)
        {
            route = "team";
            reason = "most of the team has already said it, so the move is to respond rather than "
                + "to re-gather. Asking one person about something everyone reported reads as not "
                + "having listened";
        }
        else
        {
            route = "content";
            reason = "most of the team has already said it and this condition is a capability gap, "
                + "so the move is to put something in front of them rather than ask about something "
                + "they have already told you and cannot fix by being asked again";
        }

        // Which run the content route delivers. A majority-neutral signal gets the file's
        // locus-discovery run, whose output is data rather than change; everything else gets
        // the content run, which is the fix.
        var runKind = locus.MajorityNeutral ? "locus_discovery" : "content";

        Worksheets.Delivery? worksheetChoice = null;
        (string From, string? Kind)? fellBack = null;

        if (route == "content" && declined is not null && chip.Rung1.Content is { } contentId
            && declined.Contains(contentId))
        {
            // There is one content action per capability-gap file, so a declined one leaves
            // the route with nothing else to offer. Handing back the exercise the manager
            // has already said cannot be run here would read as not having listened, which
            // is the same mistake the share threshold exists to avoid.
            fellBack = ("content", null);
            route = "team";
            reason = "this condition is a capability gap, but the manager has already declined the "
                + "exercise behind it for this team, so the team conversation is the move instead";
        }
        if (route == "content")
        {
            worksheetChoice = Worksheets.ChooseDelivery(ctx, chip.IssueFile, history, runKind);
            if (!worksheetChoice.Ok)
            {
                fellBack = ("content", runKind);
                route = "team";
                reason = $"this condition needed a {(runKind == "locus_discovery" ? "locus-discovery" : "content")} "
                    + $"delivery, and it cannot be given to this team: {worksheetChoice.Reason} The team "
                    + "conversation is the move instead.";
            }
        }

        // D30. A session this soon after the last one is not delivered, however the share
        // fell.
        //
        // The cap is on the manager's time and the team's patience, not on the condition, so
        // the condition is not dropped: it takes the team conversation instead, which is the
        // same move made smaller. A library of 56 worksheets is depth, and depth delivered
        // weekly is just a workload with a nicer name.
        //
        // It does not apply to an unlocated signal. Spec 4.6 sends a majority-neutral
        // condition to content because there is nothing to tell the team yet and nobody to
        // usefully ask; the session is how the cause gets found. Capping that would
        // substitute a conversation the spec says will not help, which is worse than a
        // second session, so the cap governs the capability-gap case it was written for and
        // leaves the discovery case alone.
        CadenceHold? cadenceHeld = null;
        if (route == "content" && !locus.MajorityNeutral
            && lastSessionAskedDay is { } lastSession && askedDayNow is { } nowAsked)
        {
            var since = nowAsked - lastSession;
            if (since < cadenceDays)
            {
                var fallback = string.IsNullOrEmpty(chip.Rung1.Team) ? "ask" : "team";
                cadenceHeld = new CadenceHold(
                    "content", fallback, since, cadenceDays,
                    $"a worksheet session ran {since} of this team's days ago and the cap is one "
                    + $"every {cadenceDays}, so this is the conversation instead of a second session");
                route = fallback;
                reason = $"{reason}. A session was run {since} of this team's days ago, so this is "
                    + "the conversation rather than a second one inside the same fortnight";
            }
        }

        // Which action row carries the delivery. A locus-discovery delivery has its own
        // action (spec 4.6, closed): every issue file in use carries one, 85 of them, each
        // naming what the team is reporting and instructing the manager not to arrive with
        // an answer, because arriving with one is how the exercise stops producing the data
        // it exists to produce.
        var actionId = chip.Rung1[route];
        if (route == "content" && runKind == "locus_discovery")
        {
            var locusAction = ctx.Data.LocusDiscoveryAction(chip.IssueFile);
            if (locusAction is not null) actionId = locusAction.Id;
        }
        if (string.IsNullOrEmpty(actionId))
        {
            throw new InvalidOperationException(
                $"chip {chip.Id} has no {route} route, which the share of {contributors}/{enrolled} "
                + "selected. The loader should have caught this.");
        }

        var isContent = route == "content";
        return new Routing
        {
            Route = route,
            ActionId = actionId,
            StoredIn = route,
            Share = share,
            ShareText = $"{contributors} of {enrolled}",
            Threshold = line,
            CapabilityGap = capabilityGap,
            AboveThreshold = share > line,
            Reason = reason,
            FellBack = fellBack,
            CadenceHeld = cadenceHeld,
            Locus = locus,
            RunKind = isContent ? worksheetChoice!.Kind : null,
            WorksheetChoice = worksheetChoice,
            WorksheetsDelivered = isContent ? worksheetChoice!.Deliver : [],
            Phases = isContent ? worksheetChoice!.Run : [],
            RunMinutes = isContent ? worksheetChoice!.Minutes : null,
            Contraindications = isContent ? worksheetChoice!.Contraindications : [],
            SubstitutionCondition = isContent && worksheetChoice!.Substitution is not null
                ? Worksheets.SubstitutionCondition : null,
        };
    }

    /// <summary>
    /// The worksheet this cycle actually handed over, or null where it handed over none.
    /// </summary>
    /// <remarks>
    /// Rung two is structural and delivers no worksheet, so the rung is explicit for the
    /// same reason it is on <see cref="DeliveredActionText"/>: a cycle that laddered mutates
    /// in place, so by the time a month is summarised its rung is two even for the move that
    /// delivered the worksheet.
    /// </remarks>
    public static Worksheet? DeliveredWorksheet(Cycle cycle, int? rung = null)
    {
        var r = rung ?? cycle.Rung;
        if (r != 1 || cycle.Routing is null || cycle.Routing.WorksheetsDelivered.Count == 0) return null;
        return cycle.Routing.WorksheetsDelivered[0];
    }

    /// <summary>
    /// What the manager actually did, for anything that reports the action after the fact.
    /// </summary>
    /// <remarks>
    /// A substitution displaces three things, not one: the worksheet delivered, the
    /// condition confirmed, and the description of what was done (spec 5.1.4). The third is
    /// the one that reaches the team, and it is the one that matters most, because the team
    /// was in the room. A note saying the manager ran the structured feedback exchange when
    /// they ran the Vulnerability Circle is not a wording error: it is the loop reporting
    /// something that did not happen, to the only people who can tell.
    /// </remarks>
    public static string DeliveredActionText(EngineContext ctx, Cycle cycle, int? rung = null)
    {
        var r = rung ?? cycle.Rung;
        var worksheet = DeliveredWorksheet(cycle, r);
        if (worksheet is not null && cycle.Routing?.SubstitutionCondition is not null)
        {
            return $"Ran {worksheet.Title} ({worksheet.Code}) with the team, taking the first turn.";
        }
        var actionId = r == 2 ? cycle.Rung2ActionId : cycle.Rung1ActionId;
        return ctx.Data.Action(actionId)!.Text;
    }

    /// <summary>
    /// The condition this cycle is actually confirmed against.
    /// </summary>
    /// <remarks>
    /// One accessor rather than four readers of the action's worked-if, because the
    /// substitution rule has to hold in the manager's card, in what Eran is told, in the
    /// closure check and in the machinery panel, and a rule that holds in three of those is
    /// not a rule. Rung two is structural and does not route, so it always reports its own.
    /// </remarks>
    public static string? DeliveredWorkedIf(EngineContext ctx, Cycle cycle)
    {
        if (cycle.Rung == 1 && cycle.Routing?.SubstitutionCondition is { } condition) return condition;
        return ctx.Data.Action(cycle.ActionId)?.WorkedIf;
    }

    /// <summary>
    /// How Eran is allowed to describe the share.
    /// </summary>
    /// <remarks>
    /// Proportions, never counts. A manager told that three people said something starts
    /// working out which three, and at the floor of two that is close to naming them. The
    /// exact numbers stay in the machinery panel (build spec 4.4.2).
    /// </remarks>
    public static string ShareInWords(double share, double threshold)
    {
        if (share <= threshold) return "fewer than half the team";
        if (share >= 0.9) return "almost everyone on the team";
        if (share >= 0.75) return "most of the team";
        return "more than half the team";
    }
}
