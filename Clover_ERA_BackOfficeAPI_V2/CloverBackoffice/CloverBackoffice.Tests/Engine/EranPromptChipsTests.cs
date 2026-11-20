using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Services.Implementation;
using Xunit;
using static CloverBackoffice.Services.Implementation.EranPromptChips;

namespace CloverBackoffice.Tests.Engine
{
    /// <summary>The suggested questions under the Eran thread: the MVP's chips, then the team's own.</summary>
    public class EranPromptChipsTests
    {
        private static EngineTeamStateDto State(string? vertical = "stall", bool calibrating = false, bool headwinds = false, int day = 40) =>
            new() { Day = day, Vertical = calibrating ? null : vertical, IsCalibrating = calibrating, HeadwindsFlag = headwinds };

        private static EngineActiveActionDto Action(bool confirmed = false, int delivered = 40, int? signalRead = null) =>
            new() { Issue = "Weekly Reflection Cadence and Rituals", DeliveredDay = delivered, IsConfirmed = confirmed, SignalReadDay = signalRead };

        [Fact]
        public void With_nothing_else_to_say_the_chips_are_the_MVPs()
        {
            Assert.Equal(new[]
            {
                "Why did weekly reflection cadence and rituals come up rather than something else?",
                "Draft the message to my team",
                "What does Stall actually mean for me?"
            }, For(new Facts(State(), Action())));

            Assert.Equal(new[]
            {
                "What would have to change for something to become active?",
                "What does Drift actually mean for me?"
            }, For(new Facts(State("drift"), null)));
        }

        [Fact]
        public void No_reading_no_chips() => Assert.Empty(For(new Facts(null, Action())));

        [Fact]
        public void Calibrating_asks_what_to_do_meanwhile()
        {
            Assert.Equal(new[]
            {
                "What should I be doing while the team is calibrating?",
                "What does calibrating actually mean for me?"
            }, For(new Facts(State(calibrating: true), null, OutwardCondition: "Workload", VerticalAtLastReading: "cruise")));
        }

        [Fact]
        public void An_outward_condition_takes_the_free_slot_before_the_state()
        {
            Assert.Equal(new[]
            {
                "Why did weekly reflection cadence and rituals come up rather than something else?",
                "Draft the message to my team",
                "What can I do about workload and capacity when it sits outside the team?",
                "What does Stall actually mean for me?"
            }, For(new Facts(State(), Action(), OutwardCondition: "Workload and Capacity", VerticalAtLastReading: "drift")));
        }

        [Fact]
        public void With_a_move_running_one_slot_is_left_and_a_state_change_comes_before_the_move()
        {
            var prompts = For(new Facts(State(day: 44), Action(delivered: 40), VerticalAtLastReading: "drift"));

            Assert.Equal(Max, prompts.Count);
            Assert.Equal("What moved the team from Drift to Stall?", prompts[2]);
            Assert.Equal("What does Stall actually mean for me?", prompts[^1]);
        }

        [Theory]
        [InlineData(false, 40, null, 44, "What if I can't get to this move this week?")]
        [InlineData(true, 40, null, 44, "How will I know whether the move worked?")]
        [InlineData(false, 44, null, 44, null)]   // delivered today: nothing to chase yet
        [InlineData(true, 40, 47, 48, null)]      // read already
        public void Where_the_move_stands(bool confirmed, int delivered, int? signalRead, int today, string? expected)
        {
            var prompts = For(new Facts(State(day: today), Action(confirmed, delivered, signalRead), VerticalAtLastReading: "stall"));

            if (expected is null) Assert.Equal(3, prompts.Count);
            else Assert.Equal(expected, prompts[2]);
        }

        [Fact]
        public void Between_cycles_with_headwinds_fills_both_free_slots()
        {
            Assert.Equal(new[]
            {
                "What would have to change for something to become active?",
                "What is coming at the team from outside?",
                "What did working on psychological safety show?",
                "What does Cruise actually mean for me?"
            }, For(new Facts(State("cruise", headwinds: true), null, LastFinishedIssue: "Psychological Safety")));
        }

        [Fact]
        public void Headwinds_is_never_the_state_word()
        {
            var prompts = For(new Facts(State("drift", headwinds: true), null));

            Assert.DoesNotContain(prompts, p => p.Contains("does Headwinds"));
            Assert.Equal("What does Drift actually mean for me?", prompts[^1]);
        }

        [Fact]
        public void Managers_own_action_fills_a_free_slot_only()
        {
            Assert.Equal(new[]
            {
                "What would have to change for something to become active?",
                "How do I follow through on \"Run a retro on the release\"?",
                "What does Drift actually mean for me?"
            }, For(new Facts(State("drift"), null, OwnActionTitle: "Run a retro on the release")));

            var overdue = For(new Facts(State("drift"), null, OwnActionTitle: "Run a retro", OwnActionOverdue: true));
            Assert.Contains("How do I get \"Run a retro\" moving again?", overdue);

            // Full: the MVP chips and the team's own data come first.
            var full = For(new Facts(State("cruise", headwinds: true), null, LastFinishedIssue: "Psychological Safety",
                OwnActionTitle: "Run a retro"));
            Assert.DoesNotContain(full, p => p.Contains("Run a retro"));
        }
    }
}
