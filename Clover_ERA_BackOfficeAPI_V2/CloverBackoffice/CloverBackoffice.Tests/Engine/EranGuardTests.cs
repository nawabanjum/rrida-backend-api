using System.Diagnostics;
using System.Text.Json;
using CloverBackoffice.Services.Implementation;
using Xunit;

namespace CloverBackoffice.Tests.Engine
{
    /// <summary>
    /// Eran's guard on the brief's minimum cases (eran-guard-dev-brief.md section 5), plus the
    /// false positives found when the rules were run over every message Eran had written on Dev.
    /// </summary>
    public class EranGuardTests
    {
        private const string ActionPayload =
            """{"situation":"action_first_day","team_state":"Drift","action_text":"Ask one person what you should stop doing, and do not negotiate with the answer.","how_you_will_know":"Two people used the same template without being asked to.","worksheet":"V8 Values Mapping"}""";
        private const string QuietPayload = """{"situation":"nothing_cleared","team_state":"Stall","nearest_condition":"unclear priorities is the one being picked up"}""";

        private static string Daily(string body, string oneThing = "Ask one person what you should stop doing.", string sayThis = "", string whatTells = "") =>
            JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["headline"] = "Your team is drifting.", ["body"] = body, ["one_thing"] = oneThing,
                ["say_this"] = sayThis, ["what_tells_you"] = whatTells
            });

        private static GuardVerdict Weekly(string text, string payload = "{}") => EranGuard.CheckFeedItem("weekly", null, payload, text);

        [Theory]
        [InlineData("3 people raised this.", "a count of people")]
        [InlineData("It came from 2 of 9 on the team.", "an x of y")]
        [InlineData("A couple of people mentioned it.", "a count of people")]
        [InlineData("Everyone but one is fine.", "a count of people")]
        [InlineData("Eleven people answered.", "a count of people")]
        [InlineData("Two of them flagged it.", "a count of people")]
        [InlineData("It was 60 per cent of the team.", "a fraction of the team")]
        [InlineData("One person on your team has gone quiet.", "an individual")]
        [InlineData("She has gone quiet this week.", "an individual")]
        [InlineData("Someone on the team described feeling stuck.", "an individual")]
        [InlineData("Most of the team cleared the floor.", "system vocabulary")]
        [InlineData("The tally moved.", "system vocabulary")]
        [InlineData("The threshold was reached.", "a threshold")]
        [InlineData("This is MA-E-004.", "an action or issue file code")]
        [InlineData("Chip 191-I6 came up.", "a chip id")]
        [InlineData("Run Worksheet C3 next.", "an invented action")]
        [InlineData("Several of you have raised that growth feels unclear.", "a count of people")]
        [InlineData("There is one person on your team who has gone quiet.", "an individual")]
        [InlineData("One person, who said it twice, is worried.", "an individual")]
        [InlineData("This was raised by one person.", "an individual")]
        [InlineData("The quietest person on the team said it.", "an individual")]
        [InlineData("Someone has been carrying the load.", "an individual")]
        [InlineData("A third said the handoffs stall.", "a fraction of the team")]
        [InlineData("Sixty percent of them agree.", "a fraction of the team")]
        [InlineData("Thirty people answered.", "a count of people")]
        [InlineData("Both raised it this week.", "a count of people")]
        [InlineData("Half of the 8 agreed.", "a count of people")]
        public void Breaks_are_caught(string text, string label)
        {
            var verdict = Weekly(text);
            Assert.False(verdict.Passed);
            Assert.Contains(label, verdict.HardLabels);
        }

        [Theory]
        [InlineData("Most of the team said this, and fewer than half the team named the cause.")]
        [InlineData("The reading lands in 4 days, and the move runs for two weeks.")]
        [InlineData("If anyone is at risk, speak to them directly today.")]
        [InlineData("Everyone chipped in on the retro.")]
        [InlineData("Pick one person on the team and ask them what the riskiest thing to say would be.")]
        [InlineData("More than one person answered the prompt.")]
        [InlineData("A short, specific word with one person is all it asks.")]
        [InlineData("Put them on a list and stop one of them this week.")]
        [InlineData("Plan the next quarter with the team.")]
        [InlineData("Cover is needed 24/7 on this desk.")]
        [InlineData("If someone has a rough week, they can say so.")]
        public void Ordinary_writing_passes(string text) => Assert.True(Weekly(text).Passed, string.Join(", ", Weekly(text).HardLabels));

        [Fact]
        public void An_em_dash_warns_and_is_still_delivered()
        {
            var verdict = Weekly("The team is steady — for now.");
            Assert.True(verdict.Passed);
            Assert.True(verdict.Warned);
            Assert.Contains("an em dash", verdict.AllLabels);
        }

        [Fact]
        public void A_worksheet_named_in_the_payload_passes_and_any_other_is_invented()
        {
            Assert.True(EranGuard.CheckFeedItem("daily", "action_first_day", ActionPayload, Daily("Run Worksheet V8 with the team.")).Passed);

            var invented = EranGuard.CheckFeedItem("daily", "action_first_day", ActionPayload, Daily("Run Worksheet V3 with the team."));
            Assert.Equal(new[] { EranGuard.InventedAction }, invented.HardLabels);
        }

        [Fact]
        public void The_library_may_be_echoed()
        {
            // "one person" and "Two people" are the authored action and worked-if, not a hint.
            var prose = Daily("This came up clearly.", "Ask one person what you should stop doing.",
                whatTells: "Two people used the same template without being asked to.");
            Assert.True(EranGuard.CheckFeedItem("daily", "action_first_day", ActionPayload, prose).Passed);
        }

        [Fact]
        public void An_echoed_count_passes_only_with_the_words_the_library_put_round_it()
        {
            // The worked-if reads "Two people used the same template without being asked to."
            var echo = Daily("This came up clearly.", whatTells: "Two people used the same template without being asked to.");
            Assert.True(EranGuard.CheckFeedItem("daily", "action_first_day", ActionPayload, echo).Passed);

            var headcount = Daily("Two people said the handoffs keep stalling.");
            Assert.Contains("a count of people", EranGuard.CheckFeedItem("daily", "action_first_day", ActionPayload, headcount).HardLabels);
        }

        [Fact]
        public void A_team_note_may_say_several_of_you_as_its_MVP_task_does()
        {
            const string text = "Several of you are waiting on access longer than I realised, so I am chasing it.";
            Assert.True(EranGuard.CheckFeedItem("note", null, "{}", text).Passed);
            Assert.False(EranGuard.CheckFeedItem("weekly", null, "{}", text).Passed);
            Assert.False(EranGuard.CheckFeedItem("note", null, "{}", "Three of you are waiting on access.").Passed);
        }

        [Fact]
        public void Say_this_is_spoken_to_the_team_so_a_hint_there_fails()
        {
            var prose = Daily("This came up clearly.", sayThis: "One of you told me the handoffs are stalling.");
            Assert.Contains("an individual", EranGuard.CheckFeedItem("daily", "action_first_day", ActionPayload, prose).HardLabels);
        }

        [Fact]
        public void A_worksheet_the_chat_tools_returned_is_not_invented()
        {
            var source = GuardSource.FromText("The action reads: \"Ask about handoffs.\"");
            Assert.False(EranGuard.CheckChat("Try Worksheet E3 with them.", source).Passed);
            Assert.True(EranGuard.CheckChat("Try Worksheet E3 with them.", source.WithCodesFrom("E3 Priority Map: …")).Passed);
            // Only the codes are taken from the tools, never their wording.
            Assert.False(EranGuard.CheckChat("3 of 8 responded.", source.WithCodesFrom("3 of 8 responded.")).Passed);
        }

        [Fact]
        public void A_missing_one_thing_and_a_quiet_day_action_fail()
        {
            var missing = EranGuard.CheckFeedItem("daily", "action_reminder", ActionPayload, Daily("Still open.", oneThing: ""));
            Assert.Contains(EranGuard.MissingAction, missing.HardLabels);

            var extra = EranGuard.CheckFeedItem("daily", "nothing_cleared", QuietPayload, Daily("Quiet week.", oneThing: "", sayThis: "Tell them it is fine."));
            Assert.Contains(EranGuard.QuietDayAction, extra.HardLabels);

            Assert.True(EranGuard.CheckFeedItem("daily", "nothing_cleared", QuietPayload, Daily("Quiet week.", oneThing: "")).Passed);
        }

        [Fact]
        public void A_daily_written_as_prose_fails()
        {
            var stored = EranNarratorService.NormaliseDaily("Just a paragraph about the team.", out var wasJson);
            Assert.False(wasJson);
            Assert.Equal(new[] { EranGuard.DailyShape }, EranGuard.CheckFeedItem("daily", "calibrating", "{}", stored, wasJson).HardLabels);
        }

        [Fact]
        public void The_threshold_may_be_called_provisional_on_a_cross_team_message()
        {
            Assert.True(EranGuard.CheckFeedItem("cross_team", null, "{}", "The threshold which triggered this is provisional.").Passed);
        }

        [Fact]
        public void The_retry_feedback_carries_rule_labels_never_the_words()
        {
            var verdict = Weekly("Three people said Sarah is struggling, and she has gone quiet.");
            var feedback = EranGuard.RetryFeedback(verdict);

            Assert.Contains("a count of people", feedback);
            Assert.Contains("an individual", feedback);
            Assert.DoesNotContain("Three people", feedback);
            Assert.DoesNotMatch(@"\bshe\b|Sarah", feedback);
            Assert.Contains("speak to them directly today", feedback);
        }

        [Fact]
        public void The_chat_card_names_the_action_and_keeps_the_safety_line()
        {
            var card = EranGuardFallback.ForChat("Ask one person what slows them down.", "One of my team said they want to harm themselves");
            Assert.StartsWith(EranGuard.SafetyLine, card);
            Assert.Contains("Your action this week is: Ask one person what slows them down.", card);
            Assert.True(EranGuard.CheckChat(card, GuardSource.FromText("Ask one person what slows them down.")).Passed);

            Assert.Equal("I can't answer that one well right now. Ask me again in a moment.", EranGuardFallback.ForChat(null, "How are we doing?"));
        }

        [Fact]
        public void A_reply_is_checked_in_well_under_five_milliseconds()
        {
            var text = string.Join(" ", Enumerable.Repeat("Most of the team said the week felt rushed, and the move is a conversation.", 12));
            Weekly(text);
            var watch = Stopwatch.StartNew();
            for (var i = 0; i < 200; i += 1) Weekly(text, ActionPayload);
            watch.Stop();
            Assert.True(watch.Elapsed.TotalMilliseconds / 200 < 5, $"{watch.Elapsed.TotalMilliseconds / 200:F2} ms per reply");
        }
    }
}
