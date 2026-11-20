using CloverBackoffice.Infrastructure.DTO;

namespace CloverBackoffice.Services.Implementation
{
    /// <summary>
    /// The suggested questions under the Eran thread. The MVP's chips come first and are worded
    /// as it words them (public/app.js renderPromptChips); the slots it leaves are filled from
    /// what the team's data shows today, so the questions follow the team rather than repeating.
    /// At most four, with "what does the state mean" always last. A question names a theme or a
    /// state, never a member or a count.
    /// </summary>
    public static class EranPromptChips
    {
        public const int Max = 4;

        /// <param name="State">The team's reading today (<see cref="EngineTeamStateDto.Day"/> is today's day).</param>
        /// <param name="ActiveAction">The move in front of the manager, or the selected cycle's.</param>
        /// <param name="OutwardCondition">D31: the cause outside the team on the newest daily message.</param>
        /// <param name="VerticalAtLastReading">The state on the last weekly reading before the current one (D25).</param>
        /// <param name="LastFinishedIssue">The issue of the move that just resolved, while the team is between cycles.</param>
        /// <param name="OwnActionTitle">The manager's most pressing open action of their own (api/actions).</param>
        /// <param name="OwnActionOverdue">Whether that action is past its due date.</param>
        public sealed record Facts(
            EngineTeamStateDto? State,
            EngineActiveActionDto? ActiveAction,
            string? OutwardCondition = null,
            string? VerticalAtLastReading = null,
            string? LastFinishedIssue = null,
            string? OwnActionTitle = null,
            bool OwnActionOverdue = false);

        /// <summary>Longest action title quoted in a question.</summary>
        public const int OwnActionTitleMax = 60;

        public static List<string> For(Facts facts)
        {
            var state = facts.State;
            if (state is null) return [];

            // The MVP's chips, as it builds them.
            var front = new List<string>();
            var issue = facts.ActiveAction?.Issue?.Trim();
            if (!string.IsNullOrEmpty(issue))
            {
                front.Add($"Why did {issue.ToLowerInvariant()} come up rather than something else?");
                front.Add("Draft the message to my team");
            }
            else if (!state.IsCalibrating)
            {
                front.Add("What would have to change for something to become active?");
            }

            var word = state.IsCalibrating ? "calibrating" : StateWord(state.Vertical);
            var last = word is null ? null : $"What does {word} actually mean for me?";

            var room = Max - front.Count - (last is null ? 0 : 1);
            var extra = FromTheData(facts, state).Distinct().Where(q => !front.Contains(q)).Take(room);

            var prompts = front.Concat(extra).ToList();
            if (last is not null) prompts.Add(last);
            return prompts;
        }

        /// <summary>The team's own questions, most pressing first.</summary>
        private static IEnumerable<string> FromTheData(Facts facts, EngineTeamStateDto state)
        {
            if (state.IsCalibrating)
            {
                yield return "What should I be doing while the team is calibrating?";
                yield break;
            }

            // A cause outside the team is the one thing the manager cannot fix inside it (D31).
            var outward = facts.OutwardCondition?.Trim();
            if (!string.IsNullOrEmpty(outward))
                yield return $"What can I do about {outward.ToLowerInvariant()} when it sits outside the team?";
            else if (state.HeadwindsFlag)
                yield return "What is coming at the team from outside?";

            var before = StateWord(facts.VerticalAtLastReading);
            var now = StateWord(state.Vertical);
            if (before is not null && now is not null && before != now)
                yield return $"What moved the team from {before} to {now}?";

            var action = facts.ActiveAction;
            if (action is not null && !string.IsNullOrWhiteSpace(action.Issue))
            {
                if (!action.IsConfirmed && state.Day > action.DeliveredDay)
                    yield return "What if I can't get to this move this week?";
                else if (action.IsConfirmed && action.SignalReadDay is null)
                    yield return "How will I know whether the move worked?";
            }

            var finished = facts.LastFinishedIssue?.Trim();
            if (action is null && !string.IsNullOrEmpty(finished))
                yield return $"What did working on {finished.ToLowerInvariant()} show?";

            // Not an MVP chip: a follow-up on the manager's own action, only when a slot is free.
            var own = facts.OwnActionTitle?.Trim();
            if (!string.IsNullOrEmpty(own))
            {
                if (own.Length > OwnActionTitleMax) own = own[..(OwnActionTitleMax - 1)].TrimEnd() + "…";
                yield return facts.OwnActionOverdue
                    ? $"How do I get \"{own}\" moving again?"
                    : $"How do I follow through on \"{own}\"?";
            }
        }

        /// <summary>Cruise, Drift or Stall. Headwinds is a flag and never a state (D19).</summary>
        private static string? StateWord(string? vertical)
        {
            var v = vertical?.Trim();
            if (string.IsNullOrEmpty(v)) return null;
            return char.ToUpperInvariant(v[0]) + v[1..].ToLowerInvariant();
        }
    }
}
