using System;
using CloverBackoffice.Infrastructure.Models.TeamQuestions;

namespace CloverBackoffice.Infrastructure.BusinessModel
{
    /// <summary>
    /// Translates between the wording used in the RRIDA requirement workbooks / API payloads
    /// and the enums the Team Questions chain stores.
    ///
    /// The workbook writes labels with spaces ("Goal Affinity", "Direction and learning") while
    /// the enums are PascalCase. Both spellings are accepted on the way in so an API caller can
    /// post either, and the label form is always what comes back out — the back office screens
    /// show the workbook's wording, not the enum name.
    /// </summary>
    public static class TeamQuestionEnumMapper
    {
        // ── Team Element ─────────────────────────────────────────────────────

        public static TeamElement? ParseTeamElement(string? value)
        {
            var key = Normalize(value);

            return key switch
            {
                "directionandlearning" => TeamElement.DirectionAndLearning,
                "designandmechanics" => TeamElement.DesignAndMechanics,
                "whattheteambelieves" => TeamElement.WhatTheTeamBelieves,
                _ => null
            };
        }

        public static string ToLabel(TeamElement value) => value switch
        {
            TeamElement.DirectionAndLearning => "Direction and learning",
            TeamElement.DesignAndMechanics => "Design and mechanics",
            TeamElement.WhatTheTeamBelieves => "What the team believes",
            _ => value.ToString()
        };

        // ── Sub-category (the eight team conditions) ─────────────────────────

        public static TeamSubCategory? ParseSubCategory(string? value)
        {
            var key = Normalize(value);

            return key switch
            {
                "goalaffinity" => TeamSubCategory.GoalAffinity,
                "results" => TeamSubCategory.Results,
                "improvementloop" => TeamSubCategory.ImprovementLoop,
                "structure" => TeamSubCategory.Structure,
                "coordinationquality" => TeamSubCategory.CoordinationQuality,
                "accountability" => TeamSubCategory.Accountability,
                "potency" => TeamSubCategory.Potency,
                "belonging" => TeamSubCategory.Belonging,
                _ => null
            };
        }

        public static string ToLabel(TeamSubCategory value) => value switch
        {
            TeamSubCategory.GoalAffinity => "Goal Affinity",
            TeamSubCategory.ImprovementLoop => "Improvement Loop",
            TeamSubCategory.CoordinationQuality => "Coordination Quality",
            _ => value.ToString()
        };

        // ── Variant ─────────────────────────────────────────────────────────

        /// <summary>
        /// The workbook qualifies the variant in brackets — "Generic (no goal registered)" and
        /// "Goal-anchored (goal shown on screen)" — so only the leading word is significant.
        /// </summary>
        public static TeamQuestionVariant? ParseVariant(string? value)
        {
            var key = Normalize(value);

            if (key.Length == 0)
            {
                return null;
            }

            if (key.StartsWith("generic", StringComparison.Ordinal))
            {
                return TeamQuestionVariant.Generic;
            }

            if (key.StartsWith("goalanchored", StringComparison.Ordinal))
            {
                return TeamQuestionVariant.GoalAnchored;
            }

            return null;
        }

        public static string ToLabel(TeamQuestionVariant value) => value switch
        {
            TeamQuestionVariant.Generic => "Generic",
            TeamQuestionVariant.GoalAnchored => "Goal-anchored",
            _ => value.ToString()
        };

        // ── Team State ──────────────────────────────────────────────────────

        /// <summary>
        /// Nullable throughout: the workbook leaves most Team State cells blank, and a blank
        /// cell means "not classified", not "invalid".
        /// </summary>
        public static TeamState? ParseTeamState(string? value)
        {
            var key = Normalize(value);

            return key switch
            {
                "cruise" => TeamState.Cruise,
                "drift" => TeamState.Drift,
                "headwinds" => TeamState.Headwinds,
                "stall" => TeamState.Stall,
                "external" => TeamState.External,
                "neutral" => TeamState.Neutral,
                _ => null
            };
        }

        public static string? ToLabel(TeamState? value) => value?.ToString();

        // ── Sentiment ───────────────────────────────────────────────────────

        public static TeamDailyReasonSentimentType? ParseSentiment(string? value)
        {
            var key = Normalize(value);

            return key switch
            {
                "positive" or "good" => TeamDailyReasonSentimentType.Positive,
                "negative" or "bad" => TeamDailyReasonSentimentType.Negative,
                _ => null
            };
        }

        /// <summary>
        /// Strips case, spaces, hyphens and any bracketed qualifier so "Goal-anchored (goal
        /// shown on screen)", "Goal Anchored" and "GoalAnchored" all reduce to one key.
        /// </summary>
        private static string Normalize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var text = value.Trim();

            var bracket = text.IndexOf('(');
            if (bracket >= 0)
            {
                text = text.Substring(0, bracket);
            }

            return text
                .Replace(" ", string.Empty)
                .Replace("-", string.Empty)
                .Replace("–", string.Empty)
                .Replace("_", string.Empty)
                .ToLowerInvariant();
        }
    }
}
