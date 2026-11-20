using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Infrastructure.Models.Mobile;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Repository.Provider
{
    /// <summary>
    /// Relational configuration for the tables the mobile app owns.
    ///
    /// These tables were created by the mobile API and already hold live data, so
    /// this maps to what is in the database rather than declaring what EF would
    /// otherwise generate. Two columns deviate from the house convention and are
    /// pinned here; both would fail at runtime, not build time, if left to
    /// convention.
    /// </summary>
    public static class MobileModelConfiguration
    {
        public static void Apply(ModelBuilder b)
        {
            b.Entity<DailyReflection>(e =>
            {
                e.HasKey(x => x.Id);

                // The column is integer. Convention would map this to bigint because
                // the app's own id space is uint, and the insert would then fail.
                e.Property(x => x.PersonalBusinessUnitId).HasColumnType("integer");

                // Postgres text[] — the app sends three parallel lists of lines.
                e.Property(x => x.AchievementsLines).HasColumnType("text[]");
                e.Property(x => x.NotDoneLines).HasColumnType("text[]");
                e.Property(x => x.BlockersLines).HasColumnType("text[]");

                e.HasIndex(x => new { x.BusinessUnitId, x.ReflectionLocalDate })
                    .HasDatabaseName("IX_DailyReflections_Unit_Date");

                // NOTE: the live constraint is DailyReflections_BusinessUnitId_fkey,
                // declared ON DELETE CASCADE. Restrict here does not contradict it —
                // this setting governs only what EF itself does when asked to delete a
                // BusinessUnit it is tracking, and nothing in this solution ever does:
                // teams are soft-deleted with IsDel. Restrict is the stricter of the
                // two, so an EF-initiated delete fails loudly rather than silently
                // taking a team's reflection history with it. A raw SQL delete still
                // cascades, because that is what the database says.
                e.HasOne(x => x.BusinessUnit).WithMany()
                    .HasForeignKey(x => x.BusinessUnitId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            b.Entity<TimeAllocation>(e =>
            {
                e.HasKey(x => x.Id);

                e.Property(x => x.Hours).HasColumnType("numeric");

                // Allocations are meaningless without their reflection, so they go with
                // it. The live constraint (FK_TimeEntryBand_DailyReflections_...) has
                // NO database cascade, so this is what actually cleans them up: EF
                // issues the child DELETEs itself before deleting the parent. Without
                // it, deleting a reflection through EF would fail on the constraint.
                e.HasOne(x => x.DailyReflection).WithMany(r => r.TimeAllocations)
                    .HasForeignKey(x => x.DailyReflectionId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            b.Entity<FeedBack>(e =>
            {
                e.HasKey(x => x.Id);

                e.Property(x => x.FeedbackText).HasColumnType("text");

                // The only timestamp in the schema without a time zone. Npgsql
                // rejects a UTC-kind DateTime for this type, so the column type is
                // stated and the repository strips the kind before writing.
                e.Property(x => x.UserSubmittedAtUtc)
                    .HasColumnType("timestamp without time zone");

                e.HasIndex(x => x.BusinessUnitId).HasDatabaseName("IX_FeedBack_Unit");

                e.HasOne(x => x.BusinessUnit).WithMany()
                    .HasForeignKey(x => x.BusinessUnitId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // Subscriptions is written by mobile company registration. The entity
            // already existed here without a DbSet; the table has always been there.
            b.Entity<Subscription>(e =>
            {
                e.ToTable("Subscriptions");
                e.HasKey(x => x.Id);
            });
        }
    }
}
