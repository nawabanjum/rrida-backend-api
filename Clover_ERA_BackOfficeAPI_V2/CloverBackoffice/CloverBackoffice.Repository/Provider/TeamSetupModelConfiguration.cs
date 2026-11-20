using CloverBackoffice.Infrastructure.Models.TeamSetups;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Repository.Provider
{
    /// <summary>
    /// Relational configuration for Team Setup: the per-unit setup row, the team's
    /// goals and the goal history.
    ///
    /// Three new tables. Nothing existing is altered — BusinessUnits already holds the
    /// team name, the team size and the invite short code, and keeping the rest off it
    /// leaves the ten foreign keys that point at it alone.
    ///
    /// Foreign keys are RESTRICT throughout, the choice CustomQuestions and
    /// TeamDailyFeedback make: a company or unit holding setup rows must not be
    /// deleted out from under them.
    /// </summary>
    public static class TeamSetupModelConfiguration
    {
        public static void Apply(ModelBuilder b)
        {
            b.Entity<TeamSetup>(e =>
            {
                // Table name and schema come from the [Table] attribute.
                e.HasKey(x => x.Id);

                // One live setup per unit. Filtered, so a soft-deleted setup does not
                // block the unit being set up again.
                e.HasIndex(x => x.UnitId)
                    .IsUnique()
                    .HasFilter("\"IsDel\" = false")
                    .HasDatabaseName("UX_TeamSetups_Unit");

                // Every setup and team-health screen filters by the owning tenant, the
                // same access path CustomQuestions indexes for.
                e.HasIndex(x => new { x.ProfileId, x.UnitId })
                    .HasFilter("\"IsDel\" = false")
                    .HasDatabaseName("IX_TeamSetups_Profile_Unit");

                e.HasOne(x => x.Profile).WithMany()
                    .HasForeignKey(x => x.ProfileId)
                    .OnDelete(DeleteBehavior.Restrict);

                e.HasOne(x => x.BusinessUnit).WithMany()
                    .HasForeignKey(x => x.UnitId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            b.Entity<TeamGoal>(e =>
            {
                e.HasKey(x => x.Id);

                e.Property(x => x.IsActive).HasDefaultValue(true);

                // Two goals must never claim the same position in the manager's list.
                // Filtered on both flags so a retired or removed goal frees its slot —
                // the same shape the goals table carried before it was dropped.
                e.HasIndex(x => new { x.UnitId, x.DisplayOrder })
                    .IsUnique()
                    .HasFilter("\"IsActive\" = true AND \"IsDel\" = false")
                    .HasDatabaseName("UX_TeamGoals_Unit_Order_Active");

                e.HasIndex(x => new { x.ProfileId, x.UnitId })
                    .HasFilter("\"IsDel\" = false")
                    .HasDatabaseName("IX_TeamGoals_Profile_Unit");

                e.HasOne(x => x.Profile).WithMany()
                    .HasForeignKey(x => x.ProfileId)
                    .OnDelete(DeleteBehavior.Restrict);

                e.HasOne(x => x.BusinessUnit).WithMany()
                    .HasForeignKey(x => x.UnitId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            b.Entity<TeamGoalAudit>(e =>
            {
                e.HasKey(x => x.AuditId);

                // History of one goal, newest first.
                e.HasIndex(x => new { x.TeamGoalId, x.LoggedAt })
                    .HasDatabaseName("IX_TeamGoalAudits_Goal_LoggedAt");

                // "What were this team's goals on that date?" — replayed across every
                // goal the unit has ever had, so the unit leads and the stamp follows.
                e.HasIndex(x => new { x.UnitId, x.LoggedAt })
                    .HasDatabaseName("IX_TeamGoalAudits_Unit_LoggedAt");

                e.HasIndex(x => x.OperationType)
                    .HasDatabaseName("IX_TeamGoalAudits_Operation");

                // Deliberately NO foreign key to TeamGoals: an audit trail must outlive
                // the row it describes, including a hard delete. The same choice
                // CustomQuestionAudits and TeamDailyFeedbackAuditLogs make.
            });
        }
    }
}
