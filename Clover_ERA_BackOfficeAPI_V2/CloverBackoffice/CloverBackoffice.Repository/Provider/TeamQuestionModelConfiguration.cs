using CloverBackoffice.Infrastructure.Models.TeamQuestions;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Repository.Provider
{
    /// <summary>
    /// Relational configuration for the Team Questions chain and Custom Questions.
    ///
    /// The Team tables mirror the legacy CLOVER chain column for column, so the
    /// relationships mirror it too: reasons hang off the specification, micro-actions
    /// off the reason, and feedback references the question and the reason directly.
    ///
    /// Legacy leaves most of these relationships to EF convention and adds no unique
    /// constraints. The few added here are the ones that prevent silent corruption of
    /// the rotation, and they cost nothing.
    /// </summary>
    public static class TeamQuestionModelConfiguration
    {
        public static void Apply(ModelBuilder b)
        {
            ConfigureTeamChain(b);
            ConfigureCustomQuestions(b);
        }

        private static void ConfigureTeamChain(ModelBuilder b)
        {
            b.Entity<TeamDailyQuestion>(e =>
            {
                e.ToTable("TeamDailyQuestions");
                e.HasKey(x => x.Id);

                e.HasOne(x => x.ContentCulture).WithMany()
                    .HasForeignKey(x => x.ContentCultureId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            b.Entity<TeamQuestionCategory>(e =>
            {
                e.ToTable("TeamQuestionCategories", "public");
                e.HasKey(x => x.Id);

                // BY DEFAULT, not ALWAYS: the migration seeds the general category with an
                // explicit Id of 0, which an ALWAYS identity would refuse.
                e.Property(x => x.Id).ValueGeneratedOnAdd().UseIdentityByDefaultColumn();

                // Two categories with the same name would make the authoring screens
                // ambiguous. Filtered so a deleted name can be reused.
                e.HasIndex(x => x.Name)
                    .IsUnique()
                    .HasFilter("\"IsDel\" = false")
                    .HasDatabaseName("UX_TeamQuestionCategories_Name");

                e.HasIndex(x => x.IsActive).HasDatabaseName("IX_TeamQuestionCategories_IsActive");
            });

            b.Entity<TeamDailyQuestionSpecification>(e =>
            {
                e.ToTable("TeamDailyQuestionSpecifications");
                e.HasKey(x => x.Id);

                e.Property(x => x.CategoryId).HasDefaultValue(TeamQuestionCategory.DefaultCategoryId);

                // Two questions cannot occupy the same rotation slot for the same
                // condition and culture. Without this the served question is arbitrary,
                // which is a latent defect in the legacy chain worth not repeating.
                //
                // CategoryId is part of the key: each category runs its own rotation, so
                // Ask #1 for Goal Affinity exists once per category, not once overall.
                e.HasIndex(x => new { x.CategoryId, x.SubCategory, x.Variant, x.Ordinal, x.ContentCultureId })
                    .IsUnique()
                    .HasDatabaseName("UX_TeamDailyQuestionSpecifications_Category_SubCategory_Variant_Ordinal_Culture");

                e.HasIndex(x => x.TeamElement).HasDatabaseName("IX_TeamDailyQuestionSpecifications_TeamElement");
                e.HasIndex(x => x.CategoryId).HasDatabaseName("IX_TeamDailyQuestionSpecifications_Category");

                e.HasOne(x => x.Question).WithMany()
                    .HasForeignKey(x => x.TeamDailyQuestionId)
                    .OnDelete(DeleteBehavior.Restrict);

                e.HasOne(x => x.ContentCulture).WithMany()
                    .HasForeignKey(x => x.ContentCultureId)
                    .OnDelete(DeleteBehavior.Restrict);

                // Restrict, so a category holding questions cannot be deleted out from
                // under them and leave the rotation pointing at nothing.
                e.HasOne(x => x.Category).WithMany()
                    .HasForeignKey(x => x.CategoryId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            b.Entity<TeamDailyReason>(e =>
            {
                e.ToTable("TeamDailyReasons");
                e.HasKey(x => x.Id);

                e.HasIndex(x => x.SpecificationId).HasDatabaseName("IX_TeamDailyReasons_Specification");
                e.HasIndex(x => x.TeamState).HasDatabaseName("IX_TeamDailyReasons_TeamState");

                // Reasons belong to the specification, exactly as DailyReason does.
                e.HasOne(x => x.Specification).WithMany(s => s.Reasons)
                    .HasForeignKey(x => x.SpecificationId)
                    .OnDelete(DeleteBehavior.Cascade);

                e.HasOne(x => x.ContentCulture).WithMany()
                    .HasForeignKey(x => x.ContentCultureId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            b.Entity<TeamDailyMicroAction>(e =>
            {
                e.ToTable("TeamDailyMicroActions");
                e.HasKey(x => x.Id);

                e.HasIndex(x => x.TeamDailyReasonId).HasDatabaseName("IX_TeamDailyMicroActions_Reason");

                e.HasOne(x => x.Reason).WithMany()
                    .HasForeignKey(x => x.TeamDailyReasonId)
                    .OnDelete(DeleteBehavior.Cascade);

                e.HasOne(x => x.ContentCulture).WithMany()
                    .HasForeignKey(x => x.ContentCultureId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            b.Entity<TeamDailyFeedback>(e =>
            {
                // Legacy DailyFeedback sits in the public schema explicitly; match it.
                e.ToTable("TeamDailyFeedback", "public");
                e.HasKey(x => x.Id);

                // The index every dashboard and scoring query needs.
                e.HasIndex(x => new { x.BusinessUnitId, x.SubmissionDateTime })
                    .HasDatabaseName("IX_TeamDailyFeedback_Unit_Submitted");

                e.HasIndex(x => x.TeamDailyQuestionId).HasDatabaseName("IX_TeamDailyFeedback_Question");
                e.HasIndex(x => x.TeamDailyReasonId).HasDatabaseName("IX_TeamDailyFeedback_Reason");

                e.HasOne(x => x.BusinessUnit).WithMany()
                    .HasForeignKey(x => x.BusinessUnitId)
                    .OnDelete(DeleteBehavior.Restrict);

                // Answered content stays deletable-proof, so a historical reading can
                // always be reconstructed.
                e.HasOne(x => x.Question).WithMany()
                    .HasForeignKey(x => x.TeamDailyQuestionId)
                    .OnDelete(DeleteBehavior.Restrict);

                e.HasOne(x => x.Reason).WithMany()
                    .HasForeignKey(x => x.TeamDailyReasonId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            b.Entity<TeamDailyFeedbackAuditLog>(e =>
            {
                // Table name and schema come from the [Table] attribute, as they do on
                // DailyFeedbackAuditLog.
                e.HasKey(x => x.AuditId);

                // The audit reads its own newest row per feedback Id on every pass;
                // legacy scans for that. Cheap to index, same reasoning as
                // IX_CustomQuestionAudits_Question_LoggedAt.
                e.HasIndex(x => new { x.Id, x.VersionNumber })
                    .HasDatabaseName("IX_TeamDailyFeedbackAuditLogs_Id_Version");

                e.HasIndex(x => x.OperationType).HasDatabaseName("IX_TeamDailyFeedbackAuditLogs_Operation");

                // Deliberately NO foreign key to TeamDailyFeedback: the trail must
                // outlive the row it describes, including a hard delete. Same choice
                // DailyFeedbackAuditLogs makes.
            });
        }

        private static void ConfigureCustomQuestions(ModelBuilder b)
        {
            b.Entity<CustomQuestion>(e =>
            {
                e.ToTable("CustomQuestions", "public");
                e.HasKey(x => x.Id);

                // Every authoring and listing screen filters by the owning tenant.
                e.HasIndex(x => new { x.ProfileId, x.UnitId })
                    .HasFilter("\"IsDel\" = false")
                    .HasDatabaseName("IX_CustomQuestions_Profile_Unit");

                // The scheduler asks "what is due?" — this is the index for it.
                e.HasIndex(x => new { x.Status, x.ScheduledAt })
                    .HasDatabaseName("IX_CustomQuestions_Status_ScheduledAt");

                e.HasOne(x => x.ContentCulture).WithMany()
                    .HasForeignKey(x => x.ContentCultureId)
                    .OnDelete(DeleteBehavior.Restrict);

                // Restrict on both: a company or unit holding questions cannot be
                // deleted out from under them and leave the questions pointing at
                // nothing. Same choice TeamDailyFeedback makes for its unit.
                e.HasOne(x => x.Profile).WithMany()
                    .HasForeignKey(x => x.ProfileId)
                    .OnDelete(DeleteBehavior.Restrict);

                e.HasOne(x => x.BusinessUnit).WithMany()
                    .HasForeignKey(x => x.UnitId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            b.Entity<CustomQuestionDailyFeedback>(e =>
            {
                // Table name and schema come from the [Table] attribute, as they do on
                // TeamDailyFeedback.
                e.HasKey(x => x.Id);

                // Answers are read per question.
                e.HasIndex(x => x.CustomQuestionId)
                    .HasDatabaseName("IX_CustomQuestionDailyFeedback_Question");

                e.HasIndex(x => x.CreatedAt)
                    .HasDatabaseName("IX_CustomQuestionDailyFeedback_CreatedAt");

                // "What did this unit answer, and when?" is the reporting question, so
                // the unit leads and the date follows it.
                e.HasIndex(x => new { x.UnitId, x.CreatedAt })
                    .HasDatabaseName("IX_CustomQuestionDailyFeedback_Unit_CreatedAt");

                // Answered content stays deletable-proof, so a historical reading can
                // always be reconstructed.
                e.HasOne(x => x.CustomQuestion).WithMany()
                    .HasForeignKey(x => x.CustomQuestionId)
                    .OnDelete(DeleteBehavior.Restrict);

                // Restrict for the same reason CustomQuestions restricts on its unit: a
                // unit holding answers must not be deleted out from under them.
                e.HasOne(x => x.BusinessUnit).WithMany()
                    .HasForeignKey(x => x.UnitId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            b.Entity<CustomQuestionAudit>(e =>
            {
                e.HasKey(x => x.AuditId);

                // History is read per question, newest first.
                e.HasIndex(x => new { x.CustomQuestionId, x.LoggedAt })
                    .HasDatabaseName("IX_CustomQuestionAudits_Question_LoggedAt");

                e.HasIndex(x => x.OperationType).HasDatabaseName("IX_CustomQuestionAudits_Operation");

                // Deliberately NO foreign key to CustomQuestions: an audit trail must
                // outlive the row it describes, including a hard delete. The same
                // choice the existing audit tables make.
            });

            b.Entity<CustomQuestionDailyFeedbackAudit>(e =>
            {
                e.HasKey(x => x.AuditId);

                e.HasIndex(x => new { x.CustomQuestionDailyFeedbackId, x.VersionNumber })
                    .HasDatabaseName("IX_CustomQuestionDailyFeedbackAudits_Feedback_Version");

                e.HasIndex(x => x.OperationType)
                    .HasDatabaseName("IX_CustomQuestionDailyFeedbackAudits_Operation");

                // No foreign key, for the same reason as above.
            });
        }
    }
}
