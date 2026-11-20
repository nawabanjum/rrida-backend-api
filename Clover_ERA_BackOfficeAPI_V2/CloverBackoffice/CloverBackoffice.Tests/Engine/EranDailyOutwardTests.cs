using CloverBackoffice.Services.Implementation;
using Xunit;

namespace CloverBackoffice.Tests.Engine
{
    /// <summary>
    /// D36 (MVP c36935d) and the check-back wording (59de560 / 0729864) on our side of Eran: the
    /// guard checks the sixth field, a message stored with five stays valid, and the fixed card
    /// carries the outward route in its own block and says nothing about when the check-back lands.
    /// </summary>
    public class EranDailyOutwardTests
    {
        private const string Outward =
            """{"situation":"nothing_cleared","team_state":"Cruise","outward_condition":"Approvals wait on another team","outward_move":"Raise the approval delay with the head of operations","outward_goes_to":"Head of operations"}""";

        [Fact]
        public void The_fixed_card_puts_the_outward_route_in_its_own_block_and_not_in_the_body()
        {
            var card = EranGuardFallback.ForFeedItem("daily", "nothing_cleared", Outward);

            Assert.True(EranGuard.TryDailyParts(card, out var parts));
            Assert.StartsWith("Approvals wait on another team. The cause sits outside the team.", parts["outward"]);
            Assert.Contains("Raise the approval delay with the head of operations. Goes to: Head of operations.", parts["outward"]);
            Assert.DoesNotContain("Approvals wait", parts["body"]);
            Assert.Equal("", parts["one_thing"]);
            Assert.True(EranGuard.CheckFeedItem("daily", "nothing_cleared", Outward, card).Passed);
        }

        [Fact]
        public void Without_an_outward_condition_the_block_is_empty()
        {
            var card = EranGuardFallback.ForFeedItem("daily", "nothing_cleared", """{"situation":"nothing_cleared","team_state":"Cruise"}""");

            Assert.True(EranGuard.TryDailyParts(card, out var parts));
            Assert.Equal("", parts["outward"]);
        }

        [Fact]
        public void The_fixed_card_names_the_check_back_and_never_when_it_lands()
        {
            const string payload =
                """{"situation":"action_confirmed","team_state":"Drift","action_text":"Ask about handoffs","how_you_will_know":"handoffs land on time","confirmation_due_in":"today"}""";

            var card = EranGuardFallback.ForFeedItem("daily", "action_confirmed", payload);

            Assert.True(EranGuard.TryDailyParts(card, out var parts));
            Assert.Contains("The check-back comes when a question on it comes round again.", parts["body"]);
            Assert.DoesNotMatch(@"\d", parts["body"]);
            Assert.True(EranGuard.CheckFeedItem("daily", "action_confirmed", payload, card).Passed);
        }

        [Fact]
        public void A_move_still_being_measured_is_said_on_a_quiet_day()
        {
            const string payload =
                """{"situation":"nothing_cleared","team_state":"Cruise","awaiting_a_check_back":"Blockers Not Cleared is still being measured"}""";

            var card = EranGuardFallback.ForFeedItem("daily", "nothing_cleared", payload);

            Assert.True(EranGuard.TryDailyParts(card, out var parts));
            Assert.EndsWith("Blockers Not Cleared is still being measured.", parts["body"]);
        }

        [Fact]
        public void The_guard_checks_the_outward_field_like_the_body()
        {
            const string prose =
                """{"headline":"Approvals are the thing.","body":"The reading is steady.","outward":"Three people said approvals are slow.","one_thing":"","say_this":"","what_tells_you":""}""";

            var verdict = EranGuard.CheckFeedItem("daily", "nothing_cleared", Outward, prose);

            Assert.False(verdict.Passed);
            Assert.Contains(verdict.Breaches, b => b.Field == "outward");
        }

        [Fact]
        public void A_message_stored_before_the_sixth_field_still_reads_and_passes()
        {
            const string prose =
                """{"headline":"A steady week.","body":"Nothing settled into a pattern yet.","one_thing":"","say_this":"","what_tells_you":""}""";

            Assert.True(EranGuard.TryDailyParts(prose, out var parts));
            Assert.Equal("", parts["outward"]);
            Assert.True(EranGuard.CheckFeedItem("daily", "nothing_cleared", """{"situation":"nothing_cleared"}""", prose).Passed);
        }
    }
}
