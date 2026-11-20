using System;

namespace CloverBackoffice.Infrastructure.Models.TeamQuestions
{
    /// <summary>
    /// The words the check-in screen puts on screen for each team enum.
    ///
    /// These live here, beside the enums they describe, so a value and its wording
    /// cannot drift apart. They are deliberately NOT a lookup table: the categorisation
    /// values are plain int enums by design, and a table would put the eight conditions
    /// in two places at once.
    ///
    /// The client is sent the value AND its wording. It needs the value to post an
    /// answer back and the wording to render the header, and having the backend own
    /// both keeps a shipped app from hard-coding "1 means Goal Affinity" — which would
    /// then be wrong the day a condition is renamed.
    ///
    /// Every method takes an unrecognised value rather than throwing: the enum can gain
    /// a member before this file catches up, and a header with an ugly label is a far
    /// better outcome for a member mid-check-in than a 500.
    /// </summary>
    public static class TeamQuestionDisplay
    {
        /// <summary>The condition's name as the screen shows it — "Goal Affinity".</summary>
        public static string Label(TeamSubCategory value) => value switch
        {
            TeamSubCategory.GoalAffinity => "Goal Affinity",
            TeamSubCategory.Results => "Results",
            TeamSubCategory.ImprovementLoop => "Improvement Loop",
            TeamSubCategory.Structure => "Structure",
            TeamSubCategory.CoordinationQuality => "Coordination Quality",
            TeamSubCategory.Accountability => "Accountability",
            TeamSubCategory.Potency => "Potency",
            TeamSubCategory.Belonging => "Belonging",
            _ => Humanise(value.ToString())
        };

        /// <summary>
        /// The two-letter badge beside the heading — "GA" for Goal Affinity. Two
        /// characters for every condition, including the single-word ones, so the badge
        /// is the same size whichever question is being asked.
        /// </summary>
        public static string ShortLabel(TeamSubCategory value) => value switch
        {
            TeamSubCategory.GoalAffinity => "GA",
            TeamSubCategory.Results => "RS",
            TeamSubCategory.ImprovementLoop => "IL",
            TeamSubCategory.Structure => "ST",
            TeamSubCategory.CoordinationQuality => "CQ",
            TeamSubCategory.Accountability => "AC",
            TeamSubCategory.Potency => "PO",
            TeamSubCategory.Belonging => "BE",
            _ => Initials(value.ToString())
        };

        public static string Label(TeamElement value) => value switch
        {
            TeamElement.DirectionAndLearning => "Direction and Learning",
            TeamElement.DesignAndMechanics => "Design and Mechanics",
            TeamElement.WhatTheTeamBelieves => "What the Team Believes",
            _ => Humanise(value.ToString())
        };

        public static string Label(TeamQuestionVariant value) => value switch
        {
            TeamQuestionVariant.Generic => "Generic",
            TeamQuestionVariant.GoalAnchored => "Goal Anchored",
            _ => Humanise(value.ToString())
        };

        public static string Label(TeamState value) => value switch
        {
            TeamState.Cruise => "Cruise",
            TeamState.Drift => "Drift",
            TeamState.Headwinds => "Headwinds",
            TeamState.Stall => "Stall",
            TeamState.External => "External",
            TeamState.Neutral => "Neutral",
            _ => Humanise(value.ToString())
        };

        /// <summary>
        /// Fallback wording for a value this file does not know: "ImprovementLoop"
        /// becomes "Improvement Loop". An undefined enum value has no name at all and
        /// arrives here as its number, which is passed through unchanged.
        /// </summary>
        private static string Humanise(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }

            var builder = new System.Text.StringBuilder(name.Length + 8);

            for (var i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
                {
                    builder.Append(' ');
                }

                builder.Append(name[i]);
            }

            return builder.ToString();
        }

        /// <summary>Fallback badge: the first two characters, upper-cased.</summary>
        private static string Initials(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }

            return name.Length == 1
                ? name.ToUpperInvariant()
                : name[..2].ToUpperInvariant();
        }
    }
}
