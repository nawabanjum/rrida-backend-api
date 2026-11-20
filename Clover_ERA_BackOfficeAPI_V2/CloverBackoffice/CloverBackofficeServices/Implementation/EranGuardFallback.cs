using System.Text.Json;

namespace CloverBackoffice.Services.Implementation
{
    /// <summary>
    /// The fixed card a manager sees when Eran's reply breaks a rule twice, or cannot be written
    /// at all (the brief, section 2). Built only from the payload's authored text and fixed
    /// sentences, never from model output, so it is the same for the same payload every time.
    /// Every card is run through <see cref="EranGuard"/> before it is stored.
    /// </summary>
    public static class EranGuardFallback
    {
        /// <summary>A card for a feed item, in the shape its prose is stored in.</summary>
        public static string ForFeedItem(string kind, string? situation, string? payloadJson)
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson);
            var p = doc.RootElement.ValueKind == JsonValueKind.Object ? doc.RootElement : default;

            return kind switch
            {
                "daily" => Daily(situation, p),
                "outcome" => Outcome(p),
                "note" => Note(p),
                "weekly" => Weekly(p),
                "monthly" => Monthly(p),
                "escalation" => Escalation(p),
                "cross_team" or "cross_team_note" => CrossTeam(p),
                _ => "There is no new message from Eran today."
            };
        }

        /// <summary>The daily headline alone: what is delivered if even the full card fails the guard.</summary>
        public static string DailyHeadlineOnly(string? payloadJson)
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson);
            var p = doc.RootElement.ValueKind == JsonValueKind.Object ? doc.RootElement : default;
            return DailyJson(Headline(Str(p, "situation"), p), "", "", "", "", "");
        }

        /// <summary>The chat's card. Names this week's action when there is one.</summary>
        public static string ForChat(string? actionText, string? question)
        {
            var text = string.IsNullOrWhiteSpace(actionText)
                ? "I can't answer that one well right now. Ask me again in a moment."
                : $"I can't answer that one well right now. Your action this week is: {Sentence(actionText)}";
            return EranGuard.SuggestsRisk(question) ? $"{EranGuard.SafetyLine} {text}" : text;
        }

        // ── daily ───────────────────────────────────────────────────────────

        private static string Daily(string? situation, JsonElement p)
        {
            situation ??= Str(p, "situation");
            var headline = Headline(situation, p);
            var action = Str(p, "action_text");
            var hasAction = action is not null && (situation is "action_first_day" or "action_reminder" or "action_confirmed");

            var body = situation switch
            {
                // Only the first sentence: the rest of the condition is guidance written for Eran
                // ("…which is the name of the problem… Do not write it up as a fault.").
                "action_first_day" => Str(p, "triggering_condition") is { } t ? FirstSentence(t) : "There is a move for you this week.",
                // MVP 0729864: nothing says when the check-back lands; it comes when a question
                // on it comes round again, and that is the whole of what is said.
                "action_reminder" => "Your move this week is still open.",
                "action_confirmed" => "You have confirmed this move. " + CheckBackComes,
                "calibrating" => "The first readings are still coming in. There is nothing to act on yet.",
                "between_cycles" => (Str(p, "what_happened_last") is { } last ? Sentence(Capitalise(last)) + " " : "")
                                    + "Nothing new has been picked up yet.",
                _ => "Nothing has come through strongly enough to act on yet."
                     + (Str(p, "nearest_condition") is { } near ? $" Closest so far: {Sentence(near)}" : "")
            };
            // MVP 59de560: "{issue} is still being measured", a move already confirmed whose
            // check-back has not come yet.
            if (!hasAction && Str(p, "awaiting_a_check_back") is { } awaiting)
                body += $" {Sentence(Capitalise(awaiting))}";

            var outward = Outward(p);
            if (!hasAction) return DailyJson(headline, body, outward, "", "", "");

            // After a prerequisite substitution the action text names another worksheet; the
            // worksheet field is what is handed over and it wins (the daily task's rule).
            var oneThing = Str(p, "worksheet_substitution") is not null && Str(p, "worksheet") is { } sheet
                ? $"Run {sheet} with the team."
                : Sentence(action!);
            return DailyJson(headline, body, outward, oneThing, "", Str(p, "how_you_will_know") is { } k ? Sentence(Capitalise(k)) : "");
        }

        /// <summary>MVP 0729864, the phrase given instead of an interval: true at any rotation length.</summary>
        private const string CheckBackComes = "The check-back comes when a question on it comes round again.";

        /// <summary>
        /// D36 (MVP c36935d): the outward route's own block, shaped as the MVP's stub shapes it
        /// (src/eran/client.js stubText): the payload's own authored wording, not a written
        /// version of it. Empty when the payload carries no outward condition.
        /// </summary>
        private static string Outward(JsonElement p)
        {
            if (Str(p, "outward_condition") is not { } condition) return "";
            var text = $"{Sentence(condition)} The cause sits outside the team.";
            if (Str(p, "outward_move") is { } move)
                text += $"\n\n{Sentence(move)}" + (Str(p, "outward_goes_to") is { } to ? $" Goes to: {Sentence(to)}" : "");
            return text;
        }

        private static string Headline(string? situation, JsonElement p) =>
            situation == "calibrating"
                ? "Your team's first readings are still coming in."
                : Str(p, "team_state")?.ToLowerInvariant() switch
                {
                    "cruise" => "Your team is in a steady place this week.",
                    "drift" => "Your team is drifting this week.",
                    "stall" => "Your team has stalled this week.",
                    _ => "Your team's first readings are still coming in."
                };

        private static string DailyJson(string headline, string body, string outward, string oneThing, string sayThis, string whatTellsYou) =>
            JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["headline"] = headline,
                ["body"] = body,
                ["outward"] = outward,
                ["one_thing"] = oneThing,
                ["say_this"] = sayThis,
                ["what_tells_you"] = whatTellsYou
            });

        // ── the other kinds: a fixed template each ───────────────────────────

        // The engine's own fields here (how_read, next_step, outcome, active_cycle) are written for
        // Eran to word, not for a reader, so these cards are templates on the facts alone. Never a
        // verdict on whether the move worked (the outcome task's rule).
        private static string Outcome(JsonElement p)
        {
            // MVP 59de560: whether the move worked is the check-back, never a reading.
            var text = "The check-back on this move is in. "
                       + (Bool(p, "signal_moved") == true
                           ? "What the team reports has shifted since the move was made."
                           : "There is no clear change in what the team reports yet.");
            if (Bool(p, "manager_confirmed") == false)
                text += " It was not confirmed as done, so nothing can be credited to it.";
            return text;
        }

        private static string Note(JsonElement p)
        {
            var area = Str(p, "issue_title") is { } t ? $" on {t.ToLowerInvariant()}" : "";
            return $"Thank you for your answers{area}. I have been looking at what came up, and I will keep checking how it goes.";
        }

        private static string Weekly(JsonElement p)
        {
            var after = Str(p, "state_after");
            var text = after is null
                ? "This week's reading on your team is in."
                : Bool(p, "changed") == true && Str(p, "state_before") is { } before
                    ? $"Your team moved from {before} to {after} this week."
                    : $"Your team held at {after} this week.";
            var active = Str(p, "active_cycle");
            text += active is null || active.StartsWith("nothing", StringComparison.OrdinalIgnoreCase)
                ? " Nothing new came through strongly enough to act on this week."
                : " A move is running for your team this week.";
            return text;
        }

        private static string Monthly(JsonElement p)
        {
            var text = Str(p, "state_start") is { } start && Str(p, "state_end") is { } end
                ? $"This month your team went from {start} to {end}."
                : "This month's summary is in.";
            var areas = p.ValueKind == JsonValueKind.Object && p.TryGetProperty("actions_taken", out var taken)
                        && taken.ValueKind == JsonValueKind.Array
                ? taken.EnumerateArray().Select(a => Str(a, "area")).Where(a => a is not null).Distinct().ToList()
                : [];
            text += areas.Count > 0 ? $" Moves this month: {string.Join("; ", areas)}." : " No move ran this month.";
            if (Str(p, "two_line_trend") is { } trend) text += $" Overall, {Sentence(trend)}";
            return text;
        }

        private static string Escalation(JsonElement p) =>
            (Str(p, "issue_title") is { } t ? $"{t} has come up again without moving." : "This area has come up again without moving.")
            + " That is a conversation rather than another action: ask the team what is underneath it.";

        private static string CrossTeam(JsonElement p) =>
            (Str(p, "issue_title") is { } t ? $"{t} is coming up across more than one of your teams." : "The same area is coming up across more than one of your teams.")
            + " The response sits at your level rather than inside any one team. The threshold that raised this is provisional.";

        // ── helpers ──────────────────────────────────────────────────────────

        private static string? Str(JsonElement p, string name) =>
            p.ValueKind == JsonValueKind.Object && p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(v.GetString())
                ? v.GetString()!.Trim()
                : null;

        private static bool? Bool(JsonElement p, string name) =>
            p.ValueKind == JsonValueKind.Object && p.TryGetProperty(name, out var v)
                ? v.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => null }
                : null;

        /// <summary>The first sentence, ending at a full stop that follows a word or a closing quote.</summary>
        private static string FirstSentence(string text)
        {
            var m = System.Text.RegularExpressions.Regex.Match(text.Trim(), "^.+?[.!?](?=\\s+[A-Z]|$)",
                System.Text.RegularExpressions.RegexOptions.Singleline, TimeSpan.FromSeconds(1));
            return Sentence(m.Success ? m.Value : text);
        }

        private static string Sentence(string text)
        {
            var t = text.Trim();
            return t.Length == 0 || t[^1] is '.' or '!' or '?' ? t : t + ".";
        }

        private static string Capitalise(string text) =>
            text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
    }
}
