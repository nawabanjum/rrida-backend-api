using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Infrastructure.Models.TeamQuestions;
using CloverBackoffice.Repository.Provider;

namespace CloverBackoffice.Tests.TeamHealth
{
    /// <summary>Custom question rows for the Team Health custom-question tests.</summary>
    internal static class CustomQuestionTestData
    {
        public static void SeedCulture(ApplicationDbContext db)
        {
            db.ContentCultures.Add(new ContentCulture { Id = 1, CultureName = "en-US" });
            db.SaveChanges();
        }

        /// <summary>A custom question asked (or to be asked) on <paramref name="day"/> at 16:00 UTC.</summary>
        public static uint AddQuestion(ApplicationDbContext db, uint unitId, string day, string text,
            CustomQuestionStatus status = CustomQuestionStatus.Scheduled)
        {
            var q = new CustomQuestion
            {
                Text = text,
                ContentCultureId = 1,
                ProfileId = unitId == MilestoneTestData.TeamB ? MilestoneTestData.ProfileB : MilestoneTestData.ProfileA,
                UnitId = unitId,
                ScheduledAt = DateTime.SpecifyKind(DateTime.Parse(day).AddHours(16), DateTimeKind.Utc),
                Status = status,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.CustomQuestions.Add(q);
            db.SaveChanges();
            return q.Id;
        }

        public static void AddAnswers(ApplicationDbContext db, uint questionId, uint unitId, int count)
        {
            for (var i = 0; i < count; i++)
                db.CustomQuestionDailyFeedback.Add(new CustomQuestionDailyFeedback
                {
                    CustomQuestionId = questionId,
                    UnitId = unitId,
                    FreeText = "answer " + i,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            db.SaveChanges();
        }
    }
}
