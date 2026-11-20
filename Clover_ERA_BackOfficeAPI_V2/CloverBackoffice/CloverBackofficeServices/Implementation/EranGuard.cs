using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CloverBackoffice.Services.Implementation
{
    public enum GuardSeverity { Hard, Warn }

    /// <summary>One broken rule. <see cref="Match"/> is for tests only and is never logged.</summary>
    public sealed record GuardBreach(string Label, GuardSeverity Severity, string? Field, string Match);

    public sealed class GuardVerdict
    {
        public GuardVerdict(IReadOnlyList<GuardBreach> breaches) => Breaches = breaches;

        public IReadOnlyList<GuardBreach> Breaches { get; }

        /// <summary>No hard breach. Style rules only warn.</summary>
        public bool Passed => Breaches.All(b => b.Severity != GuardSeverity.Hard);

        public bool Warned => Passed && Breaches.Count > 0;

        public IReadOnlyList<string> HardLabels =>
            Breaches.Where(b => b.Severity == GuardSeverity.Hard).Select(b => b.Label).Distinct().ToList();

        public IReadOnlyList<string> AllLabels => Breaches.Select(b => b.Label).Distinct().ToList();

        /// <summary>Labels only, e.g. "a count of people; system vocabulary". Safe to store and log.</summary>
        public string RulesText => string.Join("; ", Passed ? AllLabels : HardLabels);
    }

    /// <summary>
    /// The authored text Eran was handed: library and engine wording that may be echoed. A match
    /// that this text also contains is the library speaking, not Eran, so it is not a breach
    /// ("Ask one person…", "contributors", "30/60/90"). Numbers and flags are never part of it.
    /// </summary>
    public sealed class GuardSource
    {
        private static readonly string[] AuthoredFields =
        [
            "action_text", "how_you_will_know", "triggering_condition", "nearest_condition", "what_happened_last",
            "worksheet", "worksheet_substitution", "issue_title", "move", "how_it_came_through", "share_of_team",
            "outward_condition", "outward_move", "outward_goes_to", "outward_how_you_will_know", "outward_strength",
            "awaiting_a_check_back", "outcome", "how_read", "next_step", "active_cycle", "two_line_trend",
            "answer_balance", "how_the_questions_compare", "where_the_causes_sit", "actions_tried", "actions_taken",
            // Not MVP fields: the manager's own actions on the weekly and monthly messages, in
            // their own words (ManagerActionEngineActivity).
            ManagerActionEngineActivity.WeeklyKey, ManagerActionEngineActivity.MonthlyKey
        ];

        public static readonly GuardSource Empty = new(string.Empty, null, false);

        private GuardSource(string text, string? actionText, bool hasAction)
        {
            Text = text;
            ActionText = actionText;
            HasAction = hasAction;
        }

        public string Text { get; }
        public string? ActionText { get; }
        public bool HasAction { get; }

        public static GuardSource FromText(string? text, string? actionText = null) =>
            new(text ?? string.Empty, actionText, !string.IsNullOrWhiteSpace(actionText));

        public static GuardSource FromPayload(string? payloadJson)
        {
            if (string.IsNullOrWhiteSpace(payloadJson)) return Empty;
            try
            {
                using var doc = JsonDocument.Parse(payloadJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return Empty;
                var parts = new List<string>();
                foreach (var field in AuthoredFields)
                    if (doc.RootElement.TryGetProperty(field, out var v)) Collect(v, parts);
                var action = doc.RootElement.TryGetProperty("action_text", out var a) && a.ValueKind == JsonValueKind.String
                    ? a.GetString() : null;
                return new GuardSource(string.Join("\n", parts), action, !string.IsNullOrWhiteSpace(action));
            }
            catch (JsonException)
            {
                return Empty;
            }
        }

        private static void Collect(JsonElement v, List<string> into)
        {
            if (v.ValueKind == JsonValueKind.String)
            {
                into.Add(v.GetString() ?? "");
            }
            else if (v.ValueKind == JsonValueKind.Array)
            {
                foreach (var x in v.EnumerateArray()) Collect(x, into);
            }
            else if (v.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in v.EnumerateObject()) Collect(p.Value, into);
            }
        }

        /// <summary>Worksheet codes the system itself handed over this turn (the chat's tool results).</summary>
        private readonly HashSet<string> _codes = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The same source, also allowing the worksheet codes found in <paramref name="text"/> (only
        /// the codes: never its wording, which may hold counts).
        /// </summary>
        public GuardSource WithCodesFrom(string? text)
        {
            var copy = new GuardSource(Text, ActionText, HasAction);
            copy._codes.UnionWith(_codes);
            if (!string.IsNullOrEmpty(text))
                foreach (Match m in EranGuard.WorksheetCodes(text)) copy._codes.Add(m.Value);
            return copy;
        }

        public bool AllowsCode(string code) => _codes.Contains(code) || Contains(code);

        /// <summary>Whether the authored text holds this phrase, whitespace-insensitive, on word edges.</summary>
        public bool Contains(string phrase)
        {
            if (Text.Length == 0 || string.IsNullOrWhiteSpace(phrase)) return false;
            var pattern = Regex.Replace(Regex.Escape(phrase.Trim()), @"(?:\\\s|\\ )+", @"\s+");
            return Regex.IsMatch(Text, $@"(?<![\w-]){pattern}(?![\w-])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
        }
    }

    /// <summary>
    /// Eran's rules, checked on everything Eran writes before a manager sees it (the brief
    /// eran-guard-dev-brief.md). A port of the MVP's src/eran/guard.js, with the same labels, made
    /// to block: codes, system vocabulary, counts of people, hints at an individual and invented
    /// actions fail; the style rules only warn. Tuned on every message Eran had written on Dev
    /// (1,170): the only ones still failing are team notes saying "several of you".
    /// </summary>
    public static class EranGuard
    {
        public const string DailyShape = "not in the daily shape";
        public const string MissingAction = "a missing action";
        public const string QuietDayAction = "an action on a quiet day";
        public const string InventedAction = "an invented action";
        public const string Individual = "an individual";

        /// <summary>A check that could not finish (a regex timeout) fails closed under this label.</summary>
        public const string Unchecked = "a check that could not finish";

        private const RegexOptions Opt = RegexOptions.Compiled | RegexOptions.CultureInvariant;
        private const RegexOptions OptI = Opt | RegexOptions.IgnoreCase;
        // Generous: the inputs are a few hundred words and match in microseconds, but a cold start
        // under load must not turn a slow match into a failure.
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

        private const string Tens = "(?:twenty|thirty|forty|fifty|sixty|seventy|eighty|ninety|a\\s+hundred|hundred)(?:[\\s-](?:one|two|three|four|five|six|seven|eight|nine))?";
        private const string NumWords = "(?:" + Tens + "|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen|fifteen|sixteen|seventeen|eighteen|nineteen)";
        private const string NumTwoUp = @"(?:\d+|" + Tens + "|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen|fifteen|sixteen|seventeen|eighteen|nineteen)";
        private const string People = @"(?:people|persons|members|colleagues|staff|respondents|responses|answers|reports|of\s+the\s+team|of\s+your\s+team|said|reported|raised|selected|described|flagged|mentioned|named|chose|picked)";
        private const string Said = "(?:said|say|raised|reported|selected|named|flagged|mentioned|described|chose|picked|feel|felt|think|thought|agree|agreed|answered)";

        private static Regex R(string pattern, RegexOptions options = OptI) => new(pattern, options, Timeout);

        /// <summary>Identifiers of any kind. None of these exist as far as the manager is concerned.</summary>
        private static readonly (Regex Rx, string Label)[] Codes =
        [
            (R(@"\bMA-[A-Z]+-\d+(?:-\d+)?\b", Opt), "an action or issue file code"),
            (R(@"\b\d{3}-[A-Z]\d\b", Opt), "a chip id"),
            (R(@"\b[CELV]\d{1,2}-P\d\b"), "a worksheet phase id"),
        ];

        /// <summary>A worksheet code is allowed only when Eran was handed it; otherwise it is invented.</summary>
        private static readonly Regex WorksheetCode = R(@"\b(?:C|E|L|OP|R|V)\d{1,2}\b", Opt);

        internal static MatchCollection WorksheetCodes(string text) => WorksheetCode.Matches(text);

        /// <summary>The preamble's system vocabulary, on word boundaries ("chipped in" passes).</summary>
        private static readonly Regex[] Vocabulary =
        [
            .. new[]
            {
                "issue files?", "action library", "the mapping", "chip sets?", "chips?",
                "rung (?:one|two|1|2)", "the ladder", "tall(?:y|ies)", "the floor", "cleared the floor",
                "contributors?", "votes", "coaching directions?", "micro-actions?", "worked-if",
                "note types?", "deterministic layer", "the engine", "the system selected",
                "capability gaps?", "locus", "accumulators?"
            }.Select(w => R($@"\b{w}\b"))
        ];

        private const string CountOfPeople = "a count of people";

        private static readonly (Regex Rx, string Label)[] Counting =
        [
            (R($@"\b(?:\d+|{NumWords})\s+(?:of\s+(?:the\s+)?)?{People}\b"), CountOfPeople),
            (R($@"\b{NumTwoUp}\s+of\s+(?:you|them|us)\b"), CountOfPeople),
            (R($@"\b(?:both\s+of\s+them|both\s+of\s+you|both\s+(?:people|members)|both\s+{Said}|everyone\s+(?:but|except)|all\s+but\s+(?:one|two|a\s+few)|nobody\s+else|no\s+one\s+else)\b"), CountOfPeople),
            (R($@"\b(?:the\s+)?(?:majority|minority)\s+of\s+(?:the\s+)?(?:\d+|{NumWords})\b"), CountOfPeople),
            (R($@"\bhalf\s+of\s+(?:the\s+)?(?:\d+|{NumWords})\b"), CountOfPeople),
            (R(@"\b\d+\s+(?:of|out\s+of)\s+\d+\b"), "an x of y"),
            (R(@"(?<![\d/])(?!24\s*/\s*7\b)\d+\s*/\s*\d+(?![\d/])"), "an x of y"),
            (R($@"\b(?:two[\s-]thirds|one[\s-]third|three[\s-]quarters|a\s+(?:third|quarter)\s+of\s+(?:you|them|us|people|the\s+team|your\s+team)|a\s+(?:third|quarter)\s+{Said}|(?:\d+|{NumWords})\s*(?:%|per\s*cent\b))"), "a fraction of the team"),
        ];

        /// <summary>
        /// A vague count of people ("several of you"). Counted everywhere except a team note, whose
        /// MVP task gives "Several of you are waiting on access…" as its own example shape.
        /// </summary>
        private static readonly Regex VagueCount =
            R(@"\b(?:a\s+couple\s+of|a\s+few|several|a\s+handful\s+of)\s+(?:people|members|colleagues|staff|of\s+them|of\s+you)\b");

        private static readonly Regex Threshold = R(@"\bthreshold\b");

        private const string Singular =
            @"(?:(?:one\s+person|a\s+single\s+person|some(?:one|body)|a\s+team\s+member|a\s+member\s+of\s+(?:your|the)\s+team|"
            + @"one\s+(?:of\s+them|of\s+you|of\s+your\s+people|team\s+member|member)|only\s+one\s+(?:person|member)|just\s+one\s+(?:person|member)|"
            + @"the\s+(?:newest|quietest|youngest|oldest|loudest|most\s+senior|most\s+junior|longest-serving)\s+(?:person|member|one|colleague))"
            + @"(?:\s+(?:on|in|from)\s+(?:your|the)\s+team)?)";

        // Not a subject: "more than one person answered", "a word with one person is all it asks",
        // "if someone has a rough week" (a hypothetical, not a report).
        private const string NotSubject =
            @"(?<!\b(?:than|least|with|to|for|ask|pick|choose|from|about|of|by|if|when|whenever|unless|until)\s)";

        private const string Reporting =
            @"(?:\s*,?\s+(?:who|that)\s+|\s+)"
            + @"(?:(?:really|already|still|clearly|also|never|always|just|has\s+been|had\s+been|is\s+being)\s+)?"
            + "(?:said|says|told|raised|mentioned|reported|named|flagged|selected|chose|picked|described|wrote|"
            + "feels|felt|thinks|thought|is|was|has|had|isn't|wasn't|hasn't|keeps|kept|seems|seemed|struggles|"
            + "went|answered|rated|disagrees|disagreed|complained|admitted|noticed|wants|wanted|doesn't|didn't|can't|cannot)";

        /// <summary>
        /// A singular person as the one who said or is something: a hint at who answered. Checked
        /// where Eran describes the team or speaks to it, not in an instruction ("Ask one person
        /// whether…").
        /// </summary>
        private static readonly Regex SingularSubject = R($@"{NotSubject}\b{Singular}{Reporting}\b");

        /// <summary>"This was raised by one person", "most of it came from someone on your team".</summary>
        private static readonly Regex FromSingular =
            R($@"\b(?:came|comes|coming|raised|said|reported|flagged|mentioned|named|written)\s+(?:mostly\s+|only\s+|just\s+)?(?:from|by)\s+{Singular}\b");

        private static readonly Regex[] IndividualAnywhere =
        [
            R(@"\b(?:he|she|him|his|her|hers|herself|himself)\b"),
            R(@"\byour\s+(?:engineer|developer|designer|analyst|nurse|agent|rep|colleague|new\s+starter|newest\s+hire)\s+(?:said|says|told|mentioned|raised|reported|feels)\b"),
        ];

        private static readonly (Regex Rx, string Label)[] Style =
        [
            (R("—", Opt), "an em dash"),
            (R(@"^\s*I\b", Opt | RegexOptions.Multiline), "a sentence opening with \"I\""),
            (R(@"\b(?:Great question|Absolutely|I'd be happy to|Happy to help)\b"), "a filler opener"),
        ];

        // D36 (MVP c36935d): six fields. A message stored before then has no "outward", which
        // reads as an empty one (TryDailyParts), so it stays valid.
        private static readonly string[] DailyFields = ["headline", "body", "outward", "one_thing", "say_this", "what_tells_you"];

        /// <summary>One piece of text. <paramref name="describesTeam"/> is false for instruction fields.</summary>
        public static List<GuardBreach> CheckText(string? text, GuardSource source, string kind, string? field = null, bool describesTeam = true)
        {
            var found = new List<GuardBreach>();
            if (string.IsNullOrWhiteSpace(text)) return found;

            // A word the library itself uses ("contributors") may be echoed as it stands.
            void Word(string label, Match m)
            {
                if (!source.Contains(m.Value)) found.Add(new GuardBreach(label, GuardSeverity.Hard, field, m.Value));
            }

            // A count or a person may be echoed only with what follows it: "Two people used the
            // same template" copies the worked-if, "Two people said the handoffs stall" does not.
            void Phrase(string label, Match m)
            {
                if (!source.Contains(WithNextWords(text, m, 2))) found.Add(new GuardBreach(label, GuardSeverity.Hard, field, m.Value));
            }

            try
            {
                foreach (var (rx, label) in Codes)
                    foreach (Match m in rx.Matches(text)) found.Add(new GuardBreach(label, GuardSeverity.Hard, field, m.Value));

                foreach (Match m in WorksheetCode.Matches(text))
                    if (!source.AllowsCode(m.Value)) found.Add(new GuardBreach(InventedAction, GuardSeverity.Hard, field, m.Value));

                foreach (var rx in Vocabulary)
                    foreach (Match m in rx.Matches(text)) Word("system vocabulary", m);

                foreach (var (rx, label) in Counting)
                    foreach (Match m in rx.Matches(text)) Phrase(label, m);
                if (kind != "note")
                    foreach (Match m in VagueCount.Matches(text)) Phrase(CountOfPeople, m);

                // The cross-team prompts tell Eran to say the threshold is provisional.
                if (kind is not ("cross_team" or "cross_team_note"))
                    foreach (Match m in Threshold.Matches(text)) Word("a threshold", m);

                foreach (var rx in IndividualAnywhere)
                    foreach (Match m in rx.Matches(text)) Phrase(Individual, m);
                if (describesTeam)
                {
                    foreach (Match m in SingularSubject.Matches(text)) Phrase(Individual, m);
                    foreach (Match m in FromSingular.Matches(text)) Phrase(Individual, m);
                }

                foreach (var (rx, label) in Style)
                    foreach (Match m in rx.Matches(text)) found.Add(new GuardBreach(label, GuardSeverity.Warn, field, m.Value));
            }
            catch (RegexMatchTimeoutException)
            {
                // Fail closed: text that could not be checked is not delivered.
                found.Add(new GuardBreach(Unchecked, GuardSeverity.Hard, field, ""));
            }

            return found;
        }

        /// <summary>The match and up to <paramref name="words"/> words after it, from the reply.</summary>
        private static string WithNextWords(string text, Match m, int words)
        {
            var rest = text[(m.Index + m.Length)..];
            var next = Regex.Match(rest, $@"^(?:[\s,]+[\w'’-]+){{0,{words}}}", RegexOptions.CultureInvariant, Timeout);
            return m.Value + next.Value;
        }

        /// <summary>
        /// A feed item's prose as it is stored (a daily is the five-field JSON written by
        /// EranNarratorService.NormaliseDaily). <paramref name="rawDailyIsJson"/> is false when the
        /// model answered a daily in prose rather than JSON, which fails.
        /// </summary>
        public static GuardVerdict CheckFeedItem(string kind, string? situation, string? payloadJson, string? prose, bool rawDailyIsJson = true)
        {
            var source = GuardSource.FromPayload(payloadJson);
            var breaches = new List<GuardBreach>();

            if (kind != "daily")
            {
                breaches.AddRange(CheckText(prose, source, kind));
                return new GuardVerdict(breaches);
            }

            if (!rawDailyIsJson || !TryDailyParts(prose, out var parts))
            {
                breaches.Add(new GuardBreach(DailyShape, GuardSeverity.Hard, null, ""));
                return new GuardVerdict(breaches);
            }

            foreach (var f in DailyFields)
                breaches.AddRange(CheckText(parts[f], source, kind, f, describesTeam: f is "headline" or "body" or "outward" or "say_this"));

            var instructions = parts["one_thing"].Length + parts["say_this"].Length + parts["what_tells_you"].Length;
            if (source.HasAction && (situation is "action_first_day" or "action_reminder") && parts["one_thing"].Length == 0)
                breaches.Add(new GuardBreach(MissingAction, GuardSeverity.Hard, "one_thing", ""));
            // A quiet day: no action in the payload and no action situation.
            var actionDay = situation is "action_first_day" or "action_reminder" or "action_confirmed";
            if (!source.HasAction && !actionDay && instructions > 0)
                breaches.Add(new GuardBreach(QuietDayAction, GuardSeverity.Hard, null, ""));

            return new GuardVerdict(breaches);
        }

        /// <summary>A chat reply, against the situation the chat was given.</summary>
        public static GuardVerdict CheckChat(string? reply, GuardSource source) =>
            new(CheckText(reply, source, "chat"));

        /// <summary>The six daily parts from stored or raw JSON; false when it is not that shape.</summary>
        public static bool TryDailyParts(string? json, out Dictionary<string, string> parts)
        {
            parts = DailyFields.ToDictionary(f => f, _ => "");
            if (string.IsNullOrWhiteSpace(json)) return false;
            try
            {
                using var doc = JsonDocument.Parse(json.Trim());
                if (doc.RootElement.ValueKind != JsonValueKind.Object
                    || !doc.RootElement.TryGetProperty("body", out var body) || body.ValueKind != JsonValueKind.String)
                    return false;
                foreach (var f in DailyFields)
                    parts[f] = doc.RootElement.TryGetProperty(f, out var v) && v.ValueKind == JsonValueKind.String
                        ? (v.GetString() ?? "").Trim() : "";
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static readonly Regex Risk = R(
            @"\b(?:suicid\w*|self[-\s]?harm\w*|(?:harm|hurt|kill)\w*\s+(?:themselves|himself|herself|myself)|end(?:ing)?\s+it\s+all|unsafe|not\s+safe|abus\w*|harass\w*|bull(?:y|ied|ying)|threat\w*|violen\w*|assault\w*|in\s+danger|crisis|breakdown|panic\s+attacks?|burn(?:t|ed)\s+out|burnout)\b");

        /// <summary>Does the text suggest risk to a person? The prompt then requires "speak to them directly today".</summary>
        public static bool SuggestsRisk(string? text) => !string.IsNullOrWhiteSpace(text) && Risk.IsMatch(text);

        public const string SafetyLine = "If anything suggests someone's safety is at risk, speak to them directly today.";

        /// <summary>
        /// The one retry's extra instruction. Rule labels only, never the offending words, so the
        /// model is not primed with what it must not write.
        /// </summary>
        public static string RetryFeedback(GuardVerdict verdict) =>
            new StringBuilder()
                .Append("Your last reply broke these rules: ").Append(verdict.RulesText).Append(". ")
                .Append("Write it again so that it breaks none of them: speak about the team as a whole, never count ")
                .Append("people, never name or hint at an individual, use no system words or codes, and name only the ")
                .Append("action and worksheet you were given. If anything suggests risk to a person's safety, still ")
                .Append("tell the manager to speak to them directly today.")
                .ToString();
    }
}
