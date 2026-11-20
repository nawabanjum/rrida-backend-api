using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Infrastructure.Models.TeamQuestions;
using CloverBackoffice.Repository.Provider;
using Microsoft.Extensions.Options;

namespace CloverBackoffice.Tests.TeamHealth
{
    /// <summary>
    /// Question banks and answers for the timeline tests. One specification and one
    /// reason per CLOVER dimension and per team condition, so an answer is just
    /// "day, category, sentiment".
    /// </summary>
    internal static class TimelineTestData
    {
        // Sentiments (both enums share the numbering).
        public const int Angry = 1, Sad = 2, Indifferent = 3, Happy = 4;

        public static IOptions<FeedbackScoreThresholdsOptions> Thresholds() =>
            Options.Create(new FeedbackScoreThresholdsOptions { ExcellentMin = 85, GoodMin = 65, NeedsWorkMin = 35, UrgentAttentionMin = 0 });

        private static uint IndividualReasonId(CloverCategory c) => 10u + (uint)c;
        private static uint TeamReasonId(TeamSubCategory c) => 200u + (uint)c;

        public static void SeedBanks(ApplicationDbContext db)
        {
            foreach (CloverCategory category in Enum.GetValues(typeof(CloverCategory)))
            {
                db.DailyQuestionSpecifications.Add(new DailyQuestionSpecification
                {
                    Id = (uint)category,
                    DailyQuestionSetGroupId = 1,
                    DailyQuestionSetGroup = null!,
                    DailyQuestionSet = null!,
                    CloverCategory = category,
                    Ordinal = (ushort)category,
                    ContentCultureId = 1,
                    ContentCulture = null!,
                    Question = null!,
                    Reasons = new List<DailyReason>()
                });
                db.DailyReasons.Add(new DailyReason
                {
                    Id = IndividualReasonId(category),
                    DailyReasonSentimentType = DailyReasonSentimentType.Positive,
                    Text = category + " reason",
                    RequiresFreeTextFeedback = false,
                    SpecificationId = (uint)category,
                    Specification = null!,
                    ContentCulture = null!
                });
            }

            foreach (TeamSubCategory condition in Enum.GetValues(typeof(TeamSubCategory)))
            {
                db.TeamDailyQuestionSpecifications.Add(new TeamDailyQuestionSpecification
                {
                    Id = 100u + (uint)condition,
                    TeamElement = ElementOf(condition),
                    SubCategory = condition,
                    Variant = TeamQuestionVariant.Generic,
                    Ordinal = (ushort)condition,
                    ContentCultureId = 1,
                    TeamDailyQuestionId = 1
                });
                db.TeamDailyReasons.Add(new TeamDailyReason
                {
                    Id = TeamReasonId(condition),
                    TeamDailyReasonSentimentType = TeamDailyReasonSentimentType.Positive,
                    Text = condition + " reason",
                    RequiresFreeTextFeedback = false,
                    SpecificationId = 100u + (uint)condition,
                    ContentCultureId = 1
                });
            }

            db.SaveChanges();
        }

        public static TeamElement ElementOf(TeamSubCategory c) => c switch
        {
            TeamSubCategory.GoalAffinity or TeamSubCategory.Results or TeamSubCategory.ImprovementLoop => TeamElement.DirectionAndLearning,
            TeamSubCategory.Structure or TeamSubCategory.CoordinationQuality or TeamSubCategory.Accountability => TeamElement.DesignAndMechanics,
            _ => TeamElement.WhatTheTeamBelieves
        };

        private static DateTime At(string day, int hour = 16) =>
            DateTime.SpecifyKind(DateTime.Parse(day).AddHours(hour), DateTimeKind.Utc);

        /// <summary>A screen-1 answer for a CLOVER dimension.</summary>
        public static void IndividualAnswer(ApplicationDbContext db, uint unitId, string day, CloverCategory category, int sentiment, int count = 1)
        {
            for (var i = 0; i < count; i++)
            {
                db.DailyFeedbacks.Add(new DailyFeedback
                {
                    SubmissionDateTime = At(day),
                    BusinessUnitId = unitId,
                    SentimentType = (DailyFeedbackSentimentType)sentiment,
                    DailyQuestionId = 1,
                    DailyReasonId = IndividualReasonId(category),
                    FreeText = null
                });
            }
            db.SaveChanges();
        }

        /// <summary>A screen-2 answer for a team condition.</summary>
        public static void TeamAnswer(ApplicationDbContext db, uint unitId, string day, TeamSubCategory condition, int sentiment, int count = 1)
        {
            for (var i = 0; i < count; i++)
            {
                db.TeamDailyFeedback.Add(new TeamDailyFeedback
                {
                    SubmissionDateTime = At(day),
                    BusinessUnitId = unitId,
                    SentimentType = (TeamDailyFeedbackSentimentType)sentiment,
                    TeamDailyQuestionId = 1,
                    TeamDailyReasonId = TeamReasonId(condition)
                });
            }
            db.SaveChanges();
        }
    }
}
