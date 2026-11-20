namespace Rrida.Engine;

/// <summary>One recorded answer. This is what a day's check-in produces.</summary>
public sealed class Response
{
    public required int Day { get; init; }
    public required string MemberId { get; init; }
    public required int QuestionId { get; init; }
    public required string Track { get; init; }
    public required string Smiley { get; init; }
    public required IReadOnlyList<string> Chips { get; init; }

    /// <summary>
    /// The richest thing the system receives and the most identifying (spec 4.3).
    /// </summary>
    /// <remarks>
    /// Never surfaced to a manager verbatim, never quoted in a team note, and never used to
    /// name or imply an individual. It is carried because a question producing prose
    /// instead of picks is worth knowing about the instrument.
    /// </remarks>
    public string? Text { get; init; }

    public bool FreeText => !string.IsNullOrEmpty(Text) && Chips.Count == 0;
}

/// <summary>
/// The atom: level, locus and cause. Deterministic spec 4.5, 4.5a to 4.5d.
/// </summary>
/// <remarks>
/// <para>
/// This is what the engine emits upward, and per §4.5 nothing else it produces matters as
/// much. Level is the smiley, how good or bad, and it feeds the vertical axis. Locus is the
/// chip's tag, where the cause lives, and it feeds the horizontal axis, which is what
/// separates Drift from Headwinds. Cause is what the chip names, and it feeds attribution,
/// the tally and the action.
/// </para>
/// <para>
/// <b>The engine does not resolve these into a state.</b> §7.1 is explicit that the state
/// is a RRIDA-level computation and that any version derived from Question 1 alone is an
/// inference wearing the name of a measurement. What lives here is the aggregate §4.5a
/// defines, and it stops there.
/// </para>
/// </remarks>
public static class Atom
{
    public sealed record Cause(string ChipId, string Label, string IssueFile);

    public sealed class AnswerAtom
    {
        public required int Day { get; init; }
        public required string MemberId { get; init; }
        public required string Track { get; init; }
        public required int QuestionId { get; init; }
        public required string Level { get; init; }
        public required string Polarity { get; init; }
        public required IReadOnlyList<string> Locus { get; init; }
        public required IReadOnlyList<Cause> Causes { get; init; }
        public required bool ZeroChip { get; init; }
        public required bool FreeText { get; init; }
    }

    /// <summary>
    /// One answer's atom.
    /// </summary>
    /// <remarks>
    /// A zero-chip answer carries a level and no locus (§4.5), so it moves the vertical
    /// axis and not the horizontal one. That is a real answer rather than a missing one,
    /// which is why the locus is an empty list rather than a null.
    /// <para>
    /// Spec 4.3: a free-text answer contributes its level to the vertical axis, casts no
    /// vote in any tally and counts toward no floor, because it names no action the layer
    /// can route. Its locus is neutral, because the layer does not infer a locus from prose,
    /// and inferring one is how a team gets pushed toward Drift or Headwinds on a reading
    /// nobody made.
    /// </para>
    /// </remarks>
    public static AnswerAtom For(EngineContext ctx, Response response)
    {
        var chips = response.Chips.Select(ctx.Data.Chip).OfType<Chip>().ToList();
        var freeText = response.FreeText || (!string.IsNullOrEmpty(response.Text) && chips.Count == 0);
        var locus = freeText
            ? (IReadOnlyList<string>)["neutral"]
            : chips.Select(c => c.Locus).OfType<string>().ToList();

        return new AnswerAtom
        {
            Day = response.Day,
            MemberId = response.MemberId,
            Track = response.Track,
            QuestionId = response.QuestionId,
            Level = response.Smiley,
            Polarity = Vocabulary.SmileyPolarity.GetValueOrDefault(response.Smiley, ""),
            Locus = locus,
            Causes = chips.Select(c => new Cause(c.Id, c.Label, c.IssueFile)).ToList(),
            ZeroChip = chips.Count == 0,
            FreeText = freeText,
        };
    }

    public sealed class LocusCounts
    {
        public int Internal { get; set; }
        public int External { get; set; }
        public int Neutral { get; set; }

        public int this[string locus]
        {
            get => locus switch
            {
                "internal" => Internal, "external" => External, "neutral" => Neutral, _ => 0,
            };
            set
            {
                switch (locus)
                {
                    case "internal": Internal = value; break;
                    case "external": External = value; break;
                    case "neutral": Neutral = value; break;
                }
            }
        }
    }

    public sealed class HorizontalPosition
    {
        public required int Internal { get; init; }
        public required int External { get; init; }
        public required int Unlocated { get; init; }
        public required int Located { get; init; }
        public required double? ExternalShare { get; init; }
        public required string Leaning { get; init; }
    }

    public sealed class Convergence
    {
        public required int Internal { get; init; }
        public required int External { get; init; }
        public required int Located { get; init; }
        public required double? ExternalShare { get; init; }
        public required string Reading { get; init; }
    }

    public sealed class Vector
    {
        public required int From { get; init; }
        public required int To { get; init; }
        public required string? Track { get; init; }
        public required int Answers { get; init; }
        public required int ZeroChipAnswers { get; init; }
        public required int FreeTextAnswers { get; init; }
        public required double FreeTextShare { get; init; }
        public required IReadOnlyDictionary<string, int> Levels { get; init; }
        public required LocusCounts NegativeLocus { get; init; }
        public required LocusCounts PositiveLocus { get; init; }
        public required HorizontalPosition Horizontal { get; init; }
        public required Convergence Convergence { get; init; }
        public required int Unlocated { get; init; }
    }

    /// <summary>
    /// The vector the state consumes. One per team per day, over the rolling window (§4.5a).
    /// </summary>
    /// <remarks>
    /// Counted per answer rather than per chip. A member who picks three internal chips has
    /// said one internal thing about one answer, and counting the chips would let chip-load
    /// stand in for agreement, which is the same mistake the distinct-contributor floor
    /// exists to prevent. Where one answer carries chips of more than one locus it
    /// contributes to each, because it genuinely named both.
    /// </remarks>
    public static Vector VectorFor(EngineContext ctx, Team team, int from, int to, string? track = null)
    {
        var responses = new List<Response>();
        for (var day = from; day <= to; day += 1) responses.AddRange(team.ResponsesBetween(day, day, track));

        var levels = Vocabulary.SmileyOrder.ToDictionary(s => s, _ => 0, StringComparer.Ordinal);
        var negatives = new LocusCounts();
        var positives = new LocusCounts();
        var answers = 0;
        var zeroChip = 0;
        var freeTextAnswers = 0;

        foreach (var response in responses)
        {
            var atom = For(ctx, response);
            answers += 1;
            levels[atom.Level] = levels.GetValueOrDefault(atom.Level) + 1;
            if (atom.ZeroChip) zeroChip += 1;
            if (atom.FreeText) freeTextAnswers += 1;

            // Indifferent answers carry a level but sit on neither side of the locus split.
            // The Yellow tally is where they do their work.
            var bucket = atom.Polarity == "negative" ? negatives
                : atom.Polarity == "positive" ? positives : null;
            if (bucket is null) continue;
            foreach (var locus in atom.Locus.Distinct(StringComparer.Ordinal))
            {
                if (Vocabulary.LocusValues.Contains(locus)) bucket[locus] += 1;
            }
        }

        return new Vector
        {
            From = from,
            To = to,
            Track = track,
            Answers = answers,
            ZeroChipAnswers = zeroChip,
            FreeTextAnswers = freeTextAnswers,
            FreeTextShare = answers > 0 ? (double)freeTextAnswers / answers : 0,
            Levels = levels,
            NegativeLocus = negatives,
            PositiveLocus = positives,
            Horizontal = HorizontalOf(negatives),
            Convergence = ConvergenceOf(positives),
            // A high unlocated count alongside a clear level signal is the team reporting
            // something real that it cannot yet place (§4.5b). Emitted separately because it
            // is a finding rather than a gap, and it drives the content route in §4.6.
            Unlocated = negatives.Neutral,
        };
    }

    /// <summary>
    /// The horizontal position: the external share among negative answers that carry a
    /// locus at all (§4.5b).
    /// </summary>
    /// <remarks>
    /// Neutral negatives are excluded from the ratio. They cannot pull either way without
    /// the tag being invented, and inventing it is how a team gets handed an action for a
    /// cause nobody named.
    /// </remarks>
    public static HorizontalPosition HorizontalOf(LocusCounts negatives)
    {
        var located = negatives.Internal + negatives.External;
        return new HorizontalPosition
        {
            Internal = negatives.Internal,
            External = negatives.External,
            Unlocated = negatives.Neutral,
            Located = located,
            ExternalShare = located > 0 ? (double)negatives.External / located : null,
            Leaning = located == 0 ? "no located negatives"
                : negatives.External > negatives.Internal ? "external"
                : negatives.Internal > negatives.External ? "internal" : "even",
        };
    }

    /// <summary>
    /// The same ratio on the positive side, answering a different question (§4.5c).
    /// </summary>
    /// <remarks>
    /// Internal-positive means an earned rise: the team built it, and the practice behind it
    /// is worth consolidating. External-positive means a tailwind, and nothing is banked on
    /// it. One tag axis, four quadrants.
    /// </remarks>
    public static Convergence ConvergenceOf(LocusCounts positives)
    {
        var located = positives.Internal + positives.External;
        return new Convergence
        {
            Internal = positives.Internal,
            External = positives.External,
            Located = located,
            ExternalShare = located > 0 ? (double)positives.External / located : null,
            Reading = located == 0 ? "no located positives"
                : positives.Internal > positives.External
                    ? "earned rise: the team built this, and the practice behind it is worth consolidating"
                    : positives.External > positives.Internal
                        ? "tailwind: nothing should be banked on it"
                        : "evenly split between earned and tailwind",
        };
    }
}
