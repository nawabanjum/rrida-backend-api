using CloverBackoffice.Infrastructure.Models.DeterministicLayer;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Repository.Provider
{
    /// <summary>
    /// Relational configuration for the deterministic layer's reference data.
    ///
    /// Two things this file exists to state:
    ///
    ///   * the authored keys are unique WITHIN a version, never globally. Loading
    ///     version 2 of the same pack must not collide with version 1, and the
    ///     engine always reads one version;
    ///   * the list properties are Postgres text[], not a join table. They are
    ///     authored arrays that are read whole and never queried element by
    ///     element, and a load that split them into rows would make an immutable
    ///     snapshot harder to reason about, not easier.
    /// </summary>
    public static class DeterministicLayerModelConfiguration
    {
        public static void Apply(ModelBuilder b)
        {
            b.Entity<RefDataVersion>(e =>
            {
                e.ToTable("RefDataVersions");
                e.HasKey(x => x.Id);

                // One active version at a time. A partial unique index says so in the
                // database rather than leaving it to whoever writes the next loader.
                e.HasIndex(x => x.IsActive)
                    .IsUnique()
                    .HasFilter("\"IsActive\" = true")
                    .HasDatabaseName("UX_RefDataVersions_IsActive");
            });

            b.Entity<RefQuestion>(e =>
            {
                e.ToTable("RefQuestions");
                e.HasKey(x => x.Id);
                e.HasOne(x => x.Version).WithMany().HasForeignKey(x => x.VersionId).OnDelete(DeleteBehavior.Cascade);

                e.HasIndex(x => new { x.VersionId, x.QuestionKey })
                    .IsUnique()
                    .HasDatabaseName("UX_RefQuestions_Version_QuestionKey");
            });

            b.Entity<RefChip>(e =>
            {
                e.ToTable("RefChips");
                e.HasKey(x => x.Id);
                e.HasOne(x => x.Version).WithMany().HasForeignKey(x => x.VersionId).OnDelete(DeleteBehavior.Cascade);

                e.HasIndex(x => new { x.VersionId, x.ChipKey })
                    .IsUnique()
                    .HasDatabaseName("UX_RefChips_Version_ChipKey");

                // The daily read: every chip of the question being asked.
                e.HasIndex(x => new { x.VersionId, x.QuestionKey, x.Polarity })
                    .HasDatabaseName("IX_RefChips_Version_Question_Polarity");

                // Selection reads the winning action's chips by issue file.
                e.HasIndex(x => new { x.VersionId, x.IssueFileKey })
                    .HasDatabaseName("IX_RefChips_Version_IssueFile");
            });

            b.Entity<RefIssueFile>(e =>
            {
                e.ToTable("RefIssueFiles");
                e.HasKey(x => x.Id);
                e.HasOne(x => x.Version).WithMany().HasForeignKey(x => x.VersionId).OnDelete(DeleteBehavior.Cascade);

                e.HasIndex(x => new { x.VersionId, x.IssueFileKey })
                    .IsUnique()
                    .HasDatabaseName("UX_RefIssueFiles_Version_IssueFileKey");
            });

            b.Entity<RefAction>(e =>
            {
                e.ToTable("RefActions");
                e.HasKey(x => x.Id);
                e.HasOne(x => x.Version).WithMany().HasForeignKey(x => x.VersionId).OnDelete(DeleteBehavior.Cascade);

                e.HasIndex(x => new { x.VersionId, x.ActionKey })
                    .IsUnique()
                    .HasDatabaseName("UX_RefActions_Version_ActionKey");

                // Succession walks a file's actions to find a mechanism not yet installed.
                e.HasIndex(x => new { x.VersionId, x.IssueFileKey, x.Kind })
                    .HasDatabaseName("IX_RefActions_Version_IssueFile_Kind");
            });

            b.Entity<RefWorksheet>(e =>
            {
                e.ToTable("RefWorksheets");
                e.HasKey(x => x.Id);
                e.HasOne(x => x.Version).WithMany().HasForeignKey(x => x.VersionId).OnDelete(DeleteBehavior.Cascade);

                e.Property(x => x.Prerequisites).HasColumnType("text[]");
                e.Property(x => x.Contraindications).HasColumnType("text[]");

                e.HasIndex(x => new { x.VersionId, x.Code })
                    .IsUnique()
                    .HasDatabaseName("UX_RefWorksheets_Version_Code");
            });

            b.Entity<RefWorksheetPhase>(e =>
            {
                e.ToTable("RefWorksheetPhases");
                e.HasKey(x => x.Id);
                e.HasOne(x => x.Version).WithMany().HasForeignKey(x => x.VersionId).OnDelete(DeleteBehavior.Cascade);

                // A phase goes with its worksheet: the contraindications and
                // prerequisites it inherits belong to the worksheet, so a phase
                // outliving one would be unsafe to deliver.
                e.HasOne(x => x.Worksheet).WithMany(w => w.Phases)
                    .HasForeignKey(x => x.WorksheetId)
                    .OnDelete(DeleteBehavior.Cascade);

                e.Property(x => x.RequiresPhases).HasColumnType("text[]");

                e.HasIndex(x => new { x.VersionId, x.PhaseKey })
                    .IsUnique()
                    .HasDatabaseName("UX_RefWorksheetPhases_Version_PhaseKey");

                e.HasIndex(x => new { x.VersionId, x.WorksheetCode, x.Number })
                    .HasDatabaseName("IX_RefWorksheetPhases_Version_Worksheet_Number");
            });

            // ── the facts ────────────────────────────────────────────────────
            // Everything the engine replays. Identity lives here and must not be
            // reachable from a manager-facing read path.

            b.Entity<EngineMember>(e =>
            {
                e.ToTable("EngineMembers");
                e.HasKey(x => x.Id);
                e.HasOne(x => x.Profile).WithMany().HasForeignKey(x => x.ProfileId).OnDelete(DeleteBehavior.Restrict);

                // One member per token, and the engine key is how the member is named
                // everywhere downstream, so it has to be unique on its own.
                e.HasIndex(x => x.TokenHash).IsUnique().HasDatabaseName("UX_EngineMembers_TokenHash");
                e.HasIndex(x => x.EngineMemberKey).IsUnique().HasDatabaseName("UX_EngineMembers_EngineMemberKey");
            });

            b.Entity<EngineTeamMembership>(e =>
            {
                e.ToTable("EngineTeamMemberships");
                e.HasKey(x => x.Id);
                e.HasOne(x => x.Member).WithMany().HasForeignKey(x => x.MemberId).OnDelete(DeleteBehavior.Cascade);
                e.HasOne(x => x.BusinessUnit).WithMany().HasForeignKey(x => x.BusinessUnitId).OnDelete(DeleteBehavior.Restrict);

                // "Who was enrolled on this date" is the question every window asks.
                e.HasIndex(x => new { x.BusinessUnitId, x.EffectiveFrom, x.EffectiveTo })
                    .HasDatabaseName("IX_EngineTeamMemberships_Unit_Period");

                // A member holds at most one OPEN membership per team. A partial unique
                // index says so, so a double enrolment cannot inflate the denominator.
                e.HasIndex(x => new { x.MemberId, x.BusinessUnitId })
                    .IsUnique()
                    .HasFilter("\"EffectiveTo\" IS NULL")
                    .HasDatabaseName("UX_EngineTeamMemberships_Member_Unit_Open");
            });

            b.Entity<EngineTeamSettings>(e =>
            {
                e.ToTable("EngineTeamSettings");
                e.HasKey(x => x.Id);
                e.HasOne(x => x.BusinessUnit).WithMany().HasForeignKey(x => x.BusinessUnitId).OnDelete(DeleteBehavior.Restrict);

                e.HasIndex(x => x.BusinessUnitId).IsUnique().HasDatabaseName("UX_EngineTeamSettings_BusinessUnitId");
            });

            b.Entity<TeamSizeHistory>(e =>
            {
                e.ToTable("TeamSizeHistory");
                e.HasKey(x => x.Id);
                e.HasOne(x => x.BusinessUnit).WithMany().HasForeignKey(x => x.BusinessUnitId).OnDelete(DeleteBehavior.Restrict);

                // One size per team per date: a second edit the same day updates the row.
                e.HasIndex(x => new { x.BusinessUnitId, x.EffectiveFrom })
                    .IsUnique()
                    .HasDatabaseName("UX_TeamSizeHistory_Unit_EffectiveFrom");
            });

            b.Entity<EngineAnswer>(e =>
            {
                e.ToTable("EngineAnswers");
                e.HasKey(x => x.Id);
                e.HasOne(x => x.BusinessUnit).WithMany().HasForeignKey(x => x.BusinessUnitId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.Member).WithMany().HasForeignKey(x => x.MemberId).OnDelete(DeleteBehavior.Restrict);

                // Spec 4.3: a member contributes at most once per question per day. The
                // database says it rather than the application, because a retry or a double
                // tap must not become two contributions.
                e.HasIndex(x => new { x.BusinessUnitId, x.MemberId, x.Track, x.AnswerDate })
                    .IsUnique()
                    .HasDatabaseName("UX_EngineAnswers_Unit_Member_Track_Date");

                // The replay reads a team's answers in asked-day order.
                e.HasIndex(x => new { x.BusinessUnitId, x.AskedDayIndex })
                    .HasDatabaseName("IX_EngineAnswers_Unit_AskedDay");
            });

            b.Entity<EngineAnswerChip>(e =>
            {
                e.ToTable("EngineAnswerChips");
                e.HasKey(x => x.Id);
                e.HasOne(x => x.Answer).WithMany(a => a.Chips).HasForeignKey(x => x.AnswerId).OnDelete(DeleteBehavior.Cascade);

                // The same chip twice on one answer is one vote, not two, so it cannot be
                // stored twice either.
                e.HasIndex(x => new { x.AnswerId, x.ChipKey })
                    .IsUnique()
                    .HasDatabaseName("UX_EngineAnswerChips_Answer_Chip");
            });

            b.Entity<EngineConfirmation>(e =>
            {
                e.ToTable("EngineConfirmations");
                e.HasKey(x => x.Id);
                e.HasOne(x => x.BusinessUnit).WithMany().HasForeignKey(x => x.BusinessUnitId).OnDelete(DeleteBehavior.Restrict);

                // One confirmation per rung of a cycle. Tapping twice is one event.
                e.HasIndex(x => new { x.BusinessUnitId, x.CycleKey, x.Rung })
                    .IsUnique()
                    .HasDatabaseName("UX_EngineConfirmations_Unit_Cycle_Rung");
            });

            b.Entity<EngineDecline>(e =>
            {
                e.ToTable("EngineDeclines");
                e.HasKey(x => x.Id);
                e.HasOne(x => x.BusinessUnit).WithMany().HasForeignKey(x => x.BusinessUnitId).OnDelete(DeleteBehavior.Restrict);

                e.HasIndex(x => new { x.BusinessUnitId, x.CycleKey })
                    .IsUnique()
                    .HasDatabaseName("UX_EngineDeclines_Unit_Cycle");
            });

            b.Entity<EngineAcknowledgement>(e =>
            {
                e.ToTable("EngineAcknowledgements");
                e.HasKey(x => x.Id);
                e.HasOne(x => x.BusinessUnit).WithMany().HasForeignKey(x => x.BusinessUnitId).OnDelete(DeleteBehavior.Restrict);

                e.HasIndex(x => new { x.BusinessUnitId, x.CycleKey, x.Rung })
                    .IsUnique()
                    .HasDatabaseName("UX_EngineAcknowledgements_Unit_Cycle_Rung");
            });

            // ── the results ──────────────────────────────────────────────────
            // Derived by the replay, so every write is an upsert on a natural key and
            // re-running a day changes nothing.

            b.Entity<EngineStateDaily>(e =>
            {
                e.ToTable("EngineStateDaily");
                e.HasKey(x => x.Id);
                e.HasOne(x => x.BusinessUnit).WithMany().HasForeignKey(x => x.BusinessUnitId).OnDelete(DeleteBehavior.Restrict);

                e.Property(x => x.RecordJson).HasColumnType("jsonb");

                // (TeamId, Day) is the record's own natural key, and the unique index is
                // what makes a replay idempotent rather than duplicating history.
                e.HasIndex(x => new { x.BusinessUnitId, x.Day })
                    .IsUnique()
                    .HasDatabaseName("UX_EngineStateDaily_Unit_Day");
            });

            b.Entity<EngineCycle>(e =>
            {
                e.ToTable("EngineCycles");
                e.HasKey(x => x.Id);
                e.HasOne(x => x.BusinessUnit).WithMany().HasForeignKey(x => x.BusinessUnitId).OnDelete(DeleteBehavior.Restrict);

                e.HasIndex(x => new { x.BusinessUnitId, x.CycleKey })
                    .IsUnique()
                    .HasDatabaseName("UX_EngineCycles_Unit_CycleKey");

                // "What is running now", the question every manager surface asks first.
                e.HasIndex(x => new { x.BusinessUnitId, x.Outcome })
                    .HasDatabaseName("IX_EngineCycles_Unit_Outcome");
            });

            b.Entity<EngineCycleDelivery>(e =>
            {
                e.ToTable("EngineCycleDeliveries");
                e.HasKey(x => x.Id);
                e.HasOne(x => x.Cycle).WithMany(c => c.Deliveries).HasForeignKey(x => x.CycleId).OnDelete(DeleteBehavior.Cascade);

                e.Property(x => x.DeliveredPhaseRun).HasColumnType("text[]");

                // A cycle is an ordered list of deliveries, not a row with a current rung.
                e.HasIndex(x => new { x.CycleId, x.Rung })
                    .IsUnique()
                    .HasDatabaseName("UX_EngineCycleDeliveries_Cycle_Rung");
            });

            b.Entity<EngineFeedItem>(e =>
            {
                e.ToTable("EngineFeedItems");
                e.HasKey(x => x.Id);
                e.HasOne(x => x.BusinessUnit).WithMany().HasForeignKey(x => x.BusinessUnitId).OnDelete(DeleteBehavior.Restrict);

                e.Property(x => x.PayloadJson).HasColumnType("jsonb");

                e.HasIndex(x => new { x.BusinessUnitId, x.FeedKey })
                    .IsUnique()
                    .HasDatabaseName("UX_EngineFeedItems_Unit_FeedKey");

                e.HasIndex(x => new { x.BusinessUnitId, x.Day })
                    .HasDatabaseName("IX_EngineFeedItems_Unit_Day");
            });

            b.Entity<EngineLlmCall>(e =>
            {
                e.ToTable("EngineLlmCalls");
                e.HasKey(x => x.Id);
                e.HasOne(x => x.BusinessUnit).WithMany().HasForeignKey(x => x.BusinessUnitId).OnDelete(DeleteBehavior.Restrict);

                e.HasIndex(x => new { x.BusinessUnitId, x.Day })
                    .HasDatabaseName("IX_EngineLlmCalls_Unit_Day");
            });

            b.Entity<EngineTeamNote>(e =>
            {
                e.ToTable("EngineTeamNotes");
                e.HasKey(x => x.Id);
                e.HasOne(x => x.BusinessUnit).WithMany().HasForeignKey(x => x.BusinessUnitId).OnDelete(DeleteBehavior.Restrict);

                // One note per cycle per day: the replay rewrites it rather than adding a
                // second one the team would see twice.
                e.HasIndex(x => new { x.BusinessUnitId, x.CycleKey, x.Day })
                    .IsUnique()
                    .HasDatabaseName("UX_EngineTeamNotes_Unit_Cycle_Day");
            });

            b.Entity<RefCapabilityFlag>(e =>
            {
                e.ToTable("RefCapabilityFlags");
                e.HasKey(x => x.Id);
                e.HasOne(x => x.Version).WithMany().HasForeignKey(x => x.VersionId).OnDelete(DeleteBehavior.Cascade);

                e.Property(x => x.Worksheets).HasColumnType("text[]");
                e.Property(x => x.ContentPhases).HasColumnType("text[]");
                e.Property(x => x.LocusDiscoveryPhases).HasColumnType("text[]");

                e.HasIndex(x => new { x.VersionId, x.IssueFileKey })
                    .IsUnique()
                    .HasDatabaseName("UX_RefCapabilityFlags_Version_IssueFileKey");
            });
        }
    }
}
