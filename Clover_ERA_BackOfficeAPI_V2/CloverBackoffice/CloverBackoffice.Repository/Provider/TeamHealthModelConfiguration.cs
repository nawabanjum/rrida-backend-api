using CloverBackoffice.Infrastructure.Models.TeamHealth;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Repository.Provider
{
    /// <summary>
    /// Relational configuration for Team Health milestones. One new table; nothing
    /// existing is altered. Foreign keys are RESTRICT, as for TeamSetups.
    /// </summary>
    public static class TeamHealthModelConfiguration
    {
        public static void Apply(ModelBuilder b)
        {
            b.Entity<TeamMilestone>(e =>
            {
                e.HasKey(x => x.Id);

                e.Property(x => x.MilestoneDate).HasColumnType("date");

                // The timeline reads one team's milestones over a date window.
                e.HasIndex(x => new { x.UnitId, x.MilestoneDate })
                    .HasFilter("\"IsDel\" = false")
                    .HasDatabaseName("IX_TeamMilestones_Unit_Date");

                e.HasIndex(x => new { x.ProfileId, x.UnitId })
                    .HasFilter("\"IsDel\" = false")
                    .HasDatabaseName("IX_TeamMilestones_Profile_Unit");

                e.HasOne(x => x.Profile).WithMany()
                    .HasForeignKey(x => x.ProfileId)
                    .OnDelete(DeleteBehavior.Restrict);

                e.HasOne(x => x.BusinessUnit).WithMany()
                    .HasForeignKey(x => x.UnitId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            b.Entity<TeamWeeklyFlag>(e =>
            {
                e.HasKey(x => x.Id);

                e.Property(x => x.WeekStartDate).HasColumnType("date");
                e.Property(x => x.WeekEndDate).HasColumnType("date");

                // One live flag per team per week; also the index the week list reads.
                e.HasIndex(x => new { x.UnitId, x.WeekStartDate })
                    .IsUnique()
                    .HasFilter("\"IsDel\" = false")
                    .HasDatabaseName("UX_TeamWeeklyFlags_Unit_Week");

                e.HasOne(x => x.Profile).WithMany()
                    .HasForeignKey(x => x.ProfileId)
                    .OnDelete(DeleteBehavior.Restrict);

                e.HasOne(x => x.BusinessUnit).WithMany()
                    .HasForeignKey(x => x.UnitId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // The manager's record of an engine action, keyed by the engine's natural
            // key so an engine re-run never loses it. See TeamAction.cs.
            b.Entity<TeamActionState>(e =>
            {
                e.HasKey(x => x.Id);

                e.Property(x => x.DueDate).HasColumnType("date");

                // Optimistic concurrency: a write carrying an older version fails.
                e.Property(x => x.Version).IsConcurrencyToken();

                // One record per rung; also stops two first-writes racing into two rows.
                e.HasIndex(x => new { x.UnitId, x.CycleKey, x.Rung })
                    .IsUnique()
                    .HasDatabaseName("UX_TeamActionStates_Unit_Cycle_Rung");

                e.HasOne(x => x.Profile).WithMany()
                    .HasForeignKey(x => x.ProfileId)
                    .OnDelete(DeleteBehavior.Restrict);

                e.HasOne(x => x.BusinessUnit).WithMany()
                    .HasForeignKey(x => x.UnitId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            b.Entity<TeamActionComment>(e =>
            {
                e.HasKey(x => x.Id);

                e.HasIndex(x => new { x.UnitId, x.CycleKey, x.Rung })
                    .HasFilter("\"IsDel\" = false")
                    .HasDatabaseName("IX_TeamActionComments_Unit_Cycle_Rung");

                e.HasOne(x => x.Profile).WithMany()
                    .HasForeignKey(x => x.ProfileId)
                    .OnDelete(DeleteBehavior.Restrict);

                e.HasOne(x => x.BusinessUnit).WithMany()
                    .HasForeignKey(x => x.UnitId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            b.Entity<TeamActionAudit>(e =>
            {
                e.Property(x => x.DueDate).HasColumnType("date");

                e.HasIndex(x => new { x.UnitId, x.CycleKey, x.Rung })
                    .HasDatabaseName("IX_TeamActionAudits_Unit_Cycle_Rung");
            });

            // Actions a manager created themselves (POST api/actions). See ManagerAction.cs.
            b.Entity<ManagerAction>(e =>
            {
                e.HasKey(x => x.Id);

                e.Property(x => x.StartDate).HasColumnType("date");
                e.Property(x => x.DueDate).HasColumnType("date");
                // No foreign key to EngineCycleDeliveries: the engine rebuilds those rows, so the
                // link is matched on (cycle key, rung, action key), as TeamActionStates is.
                e.Property(x => x.EngineSuggestedOn).HasColumnType("date");

                // A team's actions by status: the list and what Eran is told.
                e.HasIndex(x => new { x.UnitId, x.Status })
                    .HasFilter("\"IsDel\" = false")
                    .HasDatabaseName("IX_ManagerActions_Unit_Status");

                // A profile's actions (the list without a teamId).
                e.HasIndex(x => new { x.ProfileId, x.CreatedBy })
                    .HasFilter("\"IsDel\" = false")
                    .HasDatabaseName("IX_ManagerActions_Profile_CreatedBy");

                e.HasOne(x => x.Profile).WithMany()
                    .HasForeignKey(x => x.ProfileId)
                    .OnDelete(DeleteBehavior.Restrict);

                e.HasOne(x => x.BusinessUnit).WithMany()
                    .HasForeignKey(x => x.UnitId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // Comments on a manager's own action (POST api/actions/{id}/comments).
            b.Entity<ManagerActionComment>(e =>
            {
                e.HasKey(x => x.Id);

                // An action's comments, oldest first.
                e.HasIndex(x => x.ManagerActionId)
                    .HasFilter("\"IsDel\" = false")
                    .HasDatabaseName("IX_ManagerActionComments_Action");

                e.HasOne(x => x.ManagerAction).WithMany()
                    .HasForeignKey(x => x.ManagerActionId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
        }
    }
}
