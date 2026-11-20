using CloverBackoffice.BusinessModel;
using CloverBackoffice.Infrastructure.BusinessModel;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Pgvector.EntityFrameworkCore;
using TQ = CloverBackoffice.Infrastructure.Models.TeamQuestions;
using TS = CloverBackoffice.Infrastructure.Models.TeamSetups;
using MB = CloverBackoffice.Infrastructure.Models.Mobile;
using DL = CloverBackoffice.Infrastructure.Models.DeterministicLayer;
using TH = CloverBackoffice.Infrastructure.Models.TeamHealth;
using PM = CloverBackoffice.Infrastructure.Models.PlanManagement;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CloverBackoffice.Repository.Provider
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
        }

        // Phase 1 of the move to UserRoles. Many paths still create a user by
        // setting Users.UserGroupId alone — profile registration, trial sign-up,
        // the seeder, partner onboarding, manager onboarding. Rather than touch
        // each of them, a NEW user arriving with a group and no role rows is given
        // the matching UserRoles row here, in the same SaveChanges, so no user can
        // exist without one. Removed with the column in phase 2.
        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            AddRoleRowsForNewUsers();
            RecordTeamSizeChangesAsync(sync: true, CancellationToken.None).GetAwaiter().GetResult();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            AddRoleRowsForNewUsers();
            await RecordTeamSizeChangesAsync(sync: false, cancellationToken);
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        // The team size is the headcount for the response rate and the engine's floor, and
        // the engine is replayed from day one, so every change is kept dated in
        // TeamSizeHistory. Done here, in the same SaveChanges, so every path that creates or
        // edits a team (team setup, Teams, the workbook import, onboarding) records it
        // without being touched. Only a save carrying a BusinessUnit whose TeamSize was set
        // or edited does any work; a value equal to the size already in force writes nothing.
        private async Task RecordTeamSizeChangesAsync(bool sync, CancellationToken ct)
        {
            var units = ChangeTracker.Entries<BusinessUnit>()
                .Where(e => e.State == EntityState.Added
                            || (e.State == EntityState.Modified && e.Property(u => u.TeamSize).IsModified))
                .ToList();
            if (units.Count == 0) return;

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var now = DateTime.UtcNow;

            foreach (var entry in units)
            {
                var unit = entry.Entity;
                var size = unit.TeamSize is > 0 ? unit.TeamSize : null;

                if (entry.State == EntityState.Added)
                {
                    // A new team with no size has nothing to record: enrolled members count.
                    if (size is null) continue;
                    TeamSizeHistory.Add(new DL.TeamSizeHistory
                    {
                        BusinessUnit = unit, TeamSize = size, EffectiveFrom = today, CreatedAt = now
                    });
                    continue;
                }

                // The row in force today, whether already saved or added earlier in this save.
                var unitId = unit.Id;
                var inForce = TeamSizeHistory.Local
                    .Where(h => h.BusinessUnitId == unitId && h.EffectiveFrom <= today)
                    .OrderByDescending(h => h.EffectiveFrom)
                    .FirstOrDefault();
                if (inForce is null)
                {
                    var query = TeamSizeHistory
                        .Where(h => h.BusinessUnitId == unitId && h.EffectiveFrom <= today)
                        .OrderByDescending(h => h.EffectiveFrom);
                    inForce = sync ? query.FirstOrDefault() : await query.FirstOrDefaultAsync(ct);
                }

                // Unchanged, or no size before and none now: nothing to record.
                if (inForce is null ? size is null : inForce.TeamSize == size) continue;

                if (inForce is not null && inForce.EffectiveFrom == today)
                {
                    inForce.TeamSize = size;
                    inForce.UpdatedAt = now;
                }
                else
                {
                    TeamSizeHistory.Add(new DL.TeamSizeHistory
                    {
                        BusinessUnitId = unitId, TeamSize = size, EffectiveFrom = today, CreatedAt = now
                    });
                }
            }
        }

        private void AddRoleRowsForNewUsers()
        {
            var newUsers = ChangeTracker.Entries<User>()
                .Where(e => e.State == EntityState.Added)
                .Select(e => e.Entity)
                .ToList();

            foreach (var user in newUsers)
            {
                if (user.UserGroupId <= 0 || user.Roles.Any())
                    continue;

                user.Roles.Add(new UserRole
                {
                    UserGroupId = user.UserGroupId,
                    CreatedDate = DateTime.UtcNow
                });
            }
        }

        public DbSet<User> Users { get; set; }
        public DbSet<PromptTypes> PromptTypes { get; set; }
        public DbSet<UserGroup> UserGroups { get; set; }
        public DbSet<SystemModule> SystemModules { get; set; }
        public DbSet<AssignUserRight> AssignUserRights { get; set; }
        public DbSet<Profile> Profiles { get; set; }
        public DbSet<BusinessUnit> BusinessUnits { get; set; }
        public DbSet<CompanyLink> CompanyLinks { get; set; }
        public DbSet<DailyQuestionSetGroup> DailyQuestionSetGroups { get; set; }
        public DbSet<DailyQuestionSpecification> DailyQuestionSpecifications { get; set; }
        public DbSet<Notification> Notifications { get; set; }
        public DbSet<AuditLogs> AuditLogs { get; set; }
        public DbSet<DailyFeedback> DailyFeedbacks { get; set; }

        // Daily check-in: every selected reason per answer, and the optional
        // wrap-up team feedback. See DailyCheckInEntities.cs.
        public DbSet<DailyFeedbackReason> DailyFeedbackReasons { get; set; }
        public DbSet<TeamDailyFeedbackReason> TeamDailyFeedbackReasons { get; set; }
        public DbSet<TeamFeedback> TeamFeedback { get; set; }
        public DbSet<Feedback> Feedbacks { get; set; }
        public DbSet<DailyFeedbackReportRowDto> DailyFeedbackReportRowDtos { get; set; }
        public DbSet<FeedbackDataResultDto> FeedbackDataResultDtos { get; set; }
        public DbSet<WorkAllocationResultDto> WorkAllocationResultDtos { get; set; }
        public DbSet<DailyReflectionLinesDto> DailyReflectionLinesDtos { get; set; }
        public DbSet<FeedbackAnalysisResult> FeedbackAnalysisResults { get; set; }
        public DbSet<TopTheme> TopThemes { get; set; }
        public DbSet<TopRecommendation> TopRecommendations { get; set; }
        public DbSet<CompanyTeam> CompanyTeams { get; set; }
        public DbSet<UserTeam> UserTeams { get; set; }
        public DbSet<UserRole> UserRoles { get; set; }
        public DbSet<PortfolioManagerAssignment> PortfolioManagerAssignments { get; set; }
        public DbSet<LLMPrompt> LLMPrompts { get; set; }
        public DbSet<TopAction> TopActions { get; set; }
        public DbSet<ActionHub> ActionHubs { get; set; }
        public DbSet<ActionEvent> ActionEvents { get; set; }
        public DbSet<UserToken> UserTokens { get; set; }
        public DbSet<LLMReport> LLMReports { get; set; }
        public DbSet<EngagementInsights> EngagementInsights { get; set; }
        public DbSet<EranCoachManagerMemory> EranCoachManagerMemories { get; set; }
        public DbSet<EranCoachConversation> EranCoachConversations { get; set; }
        public DbSet<EranCoachMessage> EranCoachMessages { get; set; }
        public DbSet<BatchJob> BatchJobs { get; set; }
        public DbSet<DailyQuestion> DailyQuestions { get; set; }
        public DbSet<DailyReason> DailyReasons { get; set; }
        public DbSet<ContentCulture> ContentCultures { get; set; }
        public DbSet<LLMLog> LLMLogs { get; set; }
        public DbSet<Opportunity> Opportunities { get; set; }
        public DbSet<ResetPasswordLinks> ResetPasswordLinks { get; set; }
        public DbSet<PushNotificationPreference> PushNotificationPreferences { get; set; }
        public DbSet<UserSystemDetails> UserSystemDetails { get; set; }


        //Look up Tables
        public DbSet<Industry> Industries { get; set; }
        public DbSet<OpportunityType> OpportunityTypes { get; set; }
        public DbSet<CurrentRelationshipStatus> CurrentRelationshipStatuses { get; set; }
        public DbSet<NumberOfEmployees> NumberOfEmployees { get; set; }
        public DbSet<DailyMicroAction> DailyMicroActions { get; set; }
        public DbSet<DailyQuestionSet> DailyQuestionSets { get; set; }
        public DbSet<DocumentEmbedding> DocumentEmbeddings { get; set; }
        public DbSet<DocumentEmbeddingAuditLog> DocumentEmbeddingAuditLogs { get; set; }
        public DbSet<PromptConfiguration> PromptConfigurations { get; set; }
        public DbSet<ModelConfiguration> ModelConfigurations { get; set; }
        public DbSet<ModelConfigurationType> ModelConfigurationTypes { get; set; }

        // PromptOps Integration
        public DbSet<PromptTemplate> PromptTemplates { get; set; }
        public DbSet<AIModelConfig> AIModelConfigs { get; set; }
        public DbSet<AIRequestLog> AIRequestLogs { get; set; }

        // Test Cases (Dataset Tab)
        public DbSet<TestCase> TestCases { get; set; }
        public DbSet<TestCaseAssertion> TestCaseAssertions { get; set; }

        // Workbench Background Runs
        public DbSet<WorkbenchRunStatus> WorkbenchRunStatuses { get; set; }

        // Clovera Personal App — per-unit Snapshot Reports
        public DbSet<CloveraPersonalSnapshot> CloveraPersonalSnapshots { get; set; }

        public DbSet<InsightPublishLog> InsightPublishLogs { get; set; }


        public DbSet<SystemNotification> SystemNotifications { get; set; }
        public DbSet<DailyFeedbackAuditLog> DailyFeedbackAuditLogs { get; set; }
        public DbSet<PartnerCustomerAuditLog> PartnerCustomerAuditLogs { get; set; }

        // Subscription & Payment
        public DbSet<Plan> Plans { get; set; }
        public DbSet<CompanySubscription> CompanySubscriptions { get; set; }
        public DbSet<Payment> Payments { get; set; }
        public DbSet<PromoCode> PromoCodes { get; set; }
        public DbSet<PromoCodeUsage> PromoCodeUsages { get; set; }

        // Payment & Subscription Audit Logs
        public DbSet<PaymentAuditLog> PaymentAuditLogs { get; set; }
        public DbSet<CompanySubscriptionAuditLog> CompanySubscriptionAuditLogs { get; set; }

        // Who extended which profile's trial, and by how much (POST /api/subscription/apply).
        public DbSet<TrialExtensionAuditLog> TrialExtensionAuditLogs { get; set; }
        public DbSet<TeamLimitExtension> TeamLimitExtensions { get; set; }
        public DbSet<PromoCodeAuditLog> PromoCodeAuditLogs { get; set; }
        public DbSet<PromoCodeUsageAuditLog> PromoCodeUsageAuditLogs { get; set; }

        // Company Licensing
        public DbSet<CompanyLicense> CompanyLicenses { get; set; }
        public DbSet<AssignedCompanyLicense> AssignedCompanyLicenses { get; set; }
        public DbSet<CompanyLicenseAuditLog> CompanyLicenseAuditLogs { get; set; }
        public DbSet<AssignedCompanyLicenseAuditLog> AssignedCompanyLicenseAuditLogs { get; set; }
        public DbSet<ComplimentaryLicenseGrant> ComplimentaryLicenseGrants { get; set; }
        public DbSet<ComplimentaryLicenseGrantAuditLog> ComplimentaryLicenseGrantAuditLogs { get; set; }

        // Azure Marketplace
        public DbSet<AzureMarketplaceSubscription> AzureMarketplaceSubscriptions { get; set; }
        public DbSet<AzureMarketplaceWebhookEvent> AzureMarketplaceWebhookEvents { get; set; }

        // LLM Report Schedule (per Company + BusinessUnit) — drives the scheduled
        // report generation job. Each row stores one unit's cadence configuration.
        public DbSet<LLMReportSchedule> LLMReportSchedules { get; set; }
        public DbSet<LLMReportScheduleAudit> LLMReportScheduleAudits { get; set; }

        // ── RRIDA Team Questions ─────────────────────────────────────────────
        // Mirrors the legacy CLOVER chain column for column; the legacy tables above
        // are untouched. Configuration is in TeamQuestionModelConfiguration.
        // Optional grouping for team questions (industry, sector, segment). Id 0 is the
        // seeded "General" category that uncategorised questions point at.
        public DbSet<TQ.TeamQuestionCategory> TeamQuestionCategories { get; set; }
        public DbSet<TQ.TeamDailyQuestion> TeamDailyQuestions { get; set; }
        public DbSet<TQ.TeamDailyQuestionSpecification> TeamDailyQuestionSpecifications { get; set; }
        public DbSet<TQ.TeamDailyReason> TeamDailyReasons { get; set; }
        public DbSet<TQ.TeamDailyMicroAction> TeamDailyMicroActions { get; set; }
        public DbSet<TQ.TeamDailyFeedback> TeamDailyFeedback { get; set; }

        // Snapshot audit history for TeamDailyFeedback. Mirrors DailyFeedbackAuditLogs.
        public DbSet<TQ.TeamDailyFeedbackAuditLog> TeamDailyFeedbackAuditLogs { get; set; }

        // Custom Questions — a question owned by one company and one of its units,
        // the free-text answers to it, and the audit history of both.
        public DbSet<TQ.CustomQuestion> CustomQuestions { get; set; }
        public DbSet<TQ.CustomQuestionDailyFeedback> CustomQuestionDailyFeedback { get; set; }
        public DbSet<TQ.CustomQuestionAudit> CustomQuestionAudits { get; set; }
        public DbSet<TQ.CustomQuestionDailyFeedbackAudit> CustomQuestionDailyFeedbackAudits { get; set; }

        // ── RRIDA Team Setup ─────────────────────────────────────────────────
        // What the manager tells us about a team beyond the name and size the unit
        // already carries: the team type, the manager's context, and the team's
        // goals with their full history. Configuration is in
        // TeamSetupModelConfiguration. BusinessUnits is untouched.
        public DbSet<TS.TeamSetup> TeamSetups { get; set; }
        public DbSet<TS.TeamGoal> TeamGoals { get; set; }
        public DbSet<TS.TeamGoalAudit> TeamGoalAudits { get; set; }

        // ── RRIDA Team Health ────────────────────────────────────────────────
        // Milestones pinned to the Team Health timeline. Annotation only: nothing
        // that calculates a reading reads this table. See TeamHealthModelConfiguration.
        public DbSet<TH.TeamMilestone> TeamMilestones { get; set; }

        // Weekly Flag per team per week (what came up + suggested action). Read by
        // the Team Health API; written by the flag producer.
        public DbSet<TH.TeamWeeklyFlag> TeamWeeklyFlags { get; set; }

        // The manager's record of an engine action (edit, close/reopen, comments),
        // kept beside the engine's derived rows so a re-run never loses it.
        public DbSet<TH.TeamActionState> TeamActionStates { get; set; }
        public DbSet<TH.TeamActionComment> TeamActionComments { get; set; }
        public DbSet<TH.TeamActionAudit> TeamActionAudits { get; set; }

        // Actions a manager created themselves, independent of reports and the engine
        // (POST api/actions). Eran is told about a team's open ones.
        public DbSet<TH.ManagerAction> ManagerActions { get; set; }
        public DbSet<TH.ManagerActionComment> ManagerActionComments { get; set; }

        // Plan catalogue managed by a RRIDA Admin (api/admin/plans): plans, their per-manager
        // price tiers, the feature rows and each plan's value for them. Separate from the
        // legacy Plans table above. See PlanManagementModelConfiguration.
        public DbSet<PM.ServicePlan> ServicePlans { get; set; }
        public DbSet<PM.ServicePlanPriceTier> ServicePlanPriceTiers { get; set; }
        public DbSet<PM.PlanFeature> PlanFeatures { get; set; }
        public DbSet<PM.ServicePlanFeature> ServicePlanFeatures { get; set; }
        public DbSet<PM.ServicePlanAuditLog> ServicePlanAuditLogs { get; set; }
        public DbSet<PM.ServicePlanVersion> ServicePlanVersions { get; set; }

        // Mobile app tables. All four already exist in the database; see
        // MobileModelConfiguration for the two columns that deviate from the
        // house convention.
        public DbSet<MB.DailyReflection> DailyReflections { get; set; }
        public DbSet<MB.TimeAllocation> TimeAllocations { get; set; }
        public DbSet<MB.FeedBack> FeedBack { get; set; }
        public DbSet<Subscription> Subscriptions { get; set; }

        // Deterministic layer reference data, loaded from the handover pack's JSON.
        // Authored content: every row belongs to a version and is never updated in
        // place, so a delivery made last month still resolves to what it delivered.
        // Configuration is in DeterministicLayerModelConfiguration.
        public DbSet<DL.RefDataVersion> RefDataVersions { get; set; }
        public DbSet<DL.RefQuestion> RefQuestions { get; set; }
        public DbSet<DL.RefChip> RefChips { get; set; }
        public DbSet<DL.RefIssueFile> RefIssueFiles { get; set; }
        public DbSet<DL.RefAction> RefActions { get; set; }
        public DbSet<DL.RefWorksheet> RefWorksheets { get; set; }
        public DbSet<DL.RefWorksheetPhase> RefWorksheetPhases { get; set; }
        public DbSet<DL.RefCapabilityFlag> RefCapabilityFlags { get; set; }

        // Deterministic layer facts: who answers, when they were enrolled, and what they
        // said. The engine's state is replayed from these; identity lives here and stays
        // out of every manager-facing read path.
        public DbSet<DL.EngineMember> EngineMembers { get; set; }
        public DbSet<DL.EngineTeamMembership> EngineTeamMemberships { get; set; }
        public DbSet<DL.EngineTeamSettings> EngineTeamSettings { get; set; }

        // BusinessUnits.TeamSize, dated: the headcount for the response rate and the
        // engine's floor. Written in SaveChanges below, never by a service directly.
        public DbSet<DL.TeamSizeHistory> TeamSizeHistory { get; set; }
        public DbSet<DL.EngineAnswer> EngineAnswers { get; set; }
        public DbSet<DL.EngineAnswerChip> EngineAnswerChips { get; set; }

        // Deterministic layer results, rebuilt by the replay. Every write is an upsert on
        // a natural key: (unit, day) for state, (unit, cycle key) for a cycle.
        // What the manager did. Facts, not results: no replay can derive them.
        public DbSet<DL.EngineConfirmation> EngineConfirmations { get; set; }
        public DbSet<DL.EngineDecline> EngineDeclines { get; set; }
        public DbSet<DL.EngineAcknowledgement> EngineAcknowledgements { get; set; }

        public DbSet<DL.EngineStateDaily> EngineStateDaily { get; set; }
        public DbSet<DL.EngineCycle> EngineCycles { get; set; }
        public DbSet<DL.EngineCycleDelivery> EngineCycleDeliveries { get; set; }
        public DbSet<DL.EngineFeedItem> EngineFeedItems { get; set; }
        public DbSet<DL.EngineTeamNote> EngineTeamNotes { get; set; }

        // Every call to Eran, kept in full: its output is shown to managers and teams.
        public DbSet<DL.EngineLlmCall> EngineLlmCalls { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // RRIDA Team Questions. Entirely new tables; the legacy chain is untouched.
            TeamQuestionModelConfiguration.Apply(modelBuilder);

            // RRIDA Team Setup. Three new tables; BusinessUnits is not altered.
            TeamSetupModelConfiguration.Apply(modelBuilder);

            // RRIDA Team Health milestones. One new table.
            TeamHealthModelConfiguration.Apply(modelBuilder);
            // Plan catalogue (api/admin/plans). Five new tables.
            PlanManagementModelConfiguration.Apply(modelBuilder);
            MobileModelConfiguration.Apply(modelBuilder);

            // Deterministic layer reference data. Eight new tables; nothing existing
            // is altered.
            DeterministicLayerModelConfiguration.Apply(modelBuilder);

            modelBuilder.HasPostgresExtension("vector");

            modelBuilder.Entity<DocumentEmbedding>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Embedding).HasColumnType("vector(1536)");
                entity.HasIndex(e => e.Embedding)
                      .HasMethod("hnsw")
                      .HasOperators("vector_cosine_ops");
            });

            // ERAN Coach on Team Health is team-specific: conversations are listed per team, and
            // the coach's rolling memory is kept per manager per team.
            modelBuilder.Entity<EranCoachConversation>()
                .HasIndex(c => new { c.UserId, c.BusinessUnitId })
                .HasDatabaseName("IX_EranCoachConversations_UserId_BusinessUnitId");
            modelBuilder.Entity<EranCoachManagerMemory>()
                .HasIndex(m => new { m.ManagerUserId, m.BusinessUnitId })
                .HasDatabaseName("IX_EranCoachManagerMemories_ManagerUserId_BusinessUnitId");

            // Explicit table mappings for configuration entities to match existing schema
            modelBuilder.Entity<PromptConfiguration>().ToTable("PromptConfiguration");
            modelBuilder.Entity<ModelConfiguration>().ToTable("ModelConfiguration");

            modelBuilder.Entity<SystemNotification>().ToTable("SystemNotification");

            modelBuilder.Entity<DailyFeedbackReportRowDto>().HasNoKey();

            // Plan unique slug index
            modelBuilder.Entity<Plan>()
                .HasIndex(p => p.Slug)
                .IsUnique();

            // CompanySubscription → Company
            modelBuilder.Entity<CompanySubscription>()
                .HasOne(s => s.Profile)
                .WithMany()
                .HasForeignKey(s => s.ProfileId)
                .OnDelete(DeleteBehavior.Restrict);

            // CompanySubscription → Plan
            modelBuilder.Entity<CompanySubscription>()
                .HasOne(s => s.Plan)
                .WithMany()
                .HasForeignKey(s => s.PlanId)
                .OnDelete(DeleteBehavior.Restrict);

            // CompanySubscription → PromoCode
            modelBuilder.Entity<CompanySubscription>()
                .HasOne(s => s.PromoCode)
                .WithMany()
                .HasForeignKey(s => s.PromoCodeId)
                .OnDelete(DeleteBehavior.SetNull);

            // PromoCode unique code index
            modelBuilder.Entity<PromoCode>()
                .HasIndex(p => p.Code)
                .IsUnique();

            // PromoCode → Company (optional)
            modelBuilder.Entity<PromoCode>()
                .HasOne(p => p.Profile)
                .WithMany()
                .HasForeignKey(p => p.ProfileId)
                .OnDelete(DeleteBehavior.Restrict);

            // PromoCodeUsage → PromoCode
            modelBuilder.Entity<PromoCodeUsage>()
                .HasOne(u => u.PromoCode)
                .WithMany(p => p.Usages)
                .HasForeignKey(u => u.PromoCodeId)
                .OnDelete(DeleteBehavior.Cascade);

            // PromoCodeUsage → Company
            modelBuilder.Entity<PromoCodeUsage>()
                .HasOne(u => u.Profile)
                .WithMany()
                .HasForeignKey(u => u.ProfileId)
                .OnDelete(DeleteBehavior.Restrict);

            // CompanyLicense
            modelBuilder.Entity<CompanyLicense>()
                .HasIndex(l => l.LicenseKey)
                .IsUnique();

            modelBuilder.Entity<CompanyLicense>()
                .HasOne(l => l.Profile)
                .WithMany()
                .HasForeignKey(l => l.ProfileId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CompanyLicense>()
                .HasOne(l => l.CompanySubscription)
                .WithMany()
                .HasForeignKey(l => l.CompanySubscriptionId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<CompanyLicense>()
                .HasOne(l => l.Plan)
                .WithMany()
                .HasForeignKey(l => l.PlanId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<CompanyLicense>()
                .HasOne<User>()
                .WithMany()
                .HasForeignKey(l => l.AssignedUserId)
                .OnDelete(DeleteBehavior.SetNull);

            // AssignedCompanyLicense
            modelBuilder.Entity<AssignedCompanyLicense>()
                .HasOne(a => a.CompanyLicense)
                .WithMany(l => l.Assignments)
                .HasForeignKey(a => a.CompanyLicenseId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<AssignedCompanyLicense>()
                .HasOne(a => a.BusinessUnit)
                .WithMany()
                .HasForeignKey(a => a.BusinessUnitId)
                .OnDelete(DeleteBehavior.Restrict);

            // Only one active assignment per license at any time.
            modelBuilder.Entity<AssignedCompanyLicense>()
                .HasIndex(a => new { a.CompanyLicenseId, a.IsActive })
                .HasFilter("\"IsActive\" = true")
                .IsUnique();

            // ComplimentaryLicenseGrant — a purchased license can be shared with
            // additional (same-manager) teams, up to LicenseTeamLimits.MaxTeamsPerLicense
            // teams in total. The limit is enforced in CompanyLicenseRepository.
            modelBuilder.Entity<ComplimentaryLicenseGrant>()
                .HasOne(g => g.CompanyLicense)
                .WithMany()
                .HasForeignKey(g => g.CompanyLicenseId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ComplimentaryLicenseGrant>()
                .HasOne(g => g.PrimaryBusinessUnit)
                .WithMany()
                .HasForeignKey(g => g.PrimaryBusinessUnitId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ComplimentaryLicenseGrant>()
                .HasOne(g => g.ComplimentaryBusinessUnit)
                .WithMany()
                .HasForeignKey(g => g.ComplimentaryBusinessUnitId)
                .OnDelete(DeleteBehavior.Restrict);

            // Active grants per license (lookup only; several are allowed).
            modelBuilder.Entity<ComplimentaryLicenseGrant>()
                .HasIndex(g => new { g.CompanyLicenseId, g.IsActive })
                .HasFilter("\"IsActive\" = true");

            // A unit can be the complimentary beneficiary of at most one active
            // grant at a time.
            modelBuilder.Entity<ComplimentaryLicenseGrant>()
                .HasIndex(g => new { g.ComplimentaryBusinessUnitId, g.IsActive })
                .HasFilter("\"IsActive\" = true")
                .IsUnique();

            // TeamLimitExtensions — summed per profile / per licence on every
            // team limit check (ManagerTeamLimitQueries).
            modelBuilder.Entity<TeamLimitExtension>()
                .HasIndex(e => e.ProfileId);
            modelBuilder.Entity<TeamLimitExtension>()
                .HasIndex(e => e.CompanyLicenseId);

            // Payment → Company
            modelBuilder.Entity<Payment>()
                .HasOne(p => p.Profile)
                .WithMany()
                .HasForeignKey(p => p.ProfileId)
                .OnDelete(DeleteBehavior.Restrict);

            // Payment → CompanySubscription
            modelBuilder.Entity<Payment>()
                .HasOne(p => p.CompanySubscription)
                .WithMany(s => s.Payments)
                .HasForeignKey(p => p.CompanySubscriptionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<User>()
                .HasOne(u => u.UserGroup)
                .WithMany(g => g.Users)
                .HasForeignKey(u => u.UserGroupId)
                .OnDelete(DeleteBehavior.Restrict);

            // A user's roles. Mirrors Scripts/UserGroups/05_AddUserRolesAndPortfolioManagerAssignments.
            modelBuilder.Entity<UserRole>(entity =>
            {
                entity.ToTable("UserRoles");

                entity.HasOne(r => r.User)
                      .WithMany(u => u.Roles)
                      .HasForeignKey(r => r.UserId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(r => r.UserGroup)
                      .WithMany()
                      .HasForeignKey(r => r.UserGroupId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(r => new { r.UserId, r.UserGroupId })
                      .IsUnique()
                      .HasDatabaseName("UX_UserRoles_UserId_UserGroupId");

                // "Every Team Manager in this profile" filters on the role first.
                entity.HasIndex(r => r.UserGroupId)
                      .HasDatabaseName("IX_UserRoles_UserGroupId");
            });

            modelBuilder.Entity<PortfolioManagerAssignment>(entity =>
            {
                entity.ToTable("PortfolioManagerAssignments",
                    t => t.HasCheckConstraint("CK_PortfolioManagerAssignments_NotSelf",
                        "\"PortfolioManagerUserId\" <> \"ManagerUserId\""));

                entity.HasOne<User>()
                      .WithMany()
                      .HasForeignKey(a => a.PortfolioManagerUserId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne<User>()
                      .WithMany()
                      .HasForeignKey(a => a.ManagerUserId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne<Profile>()
                      .WithMany()
                      .HasForeignKey(a => a.ProfileId)
                      .OnDelete(DeleteBehavior.Restrict);

                // One live Portfolio Manager per manager; history rows are free.
                entity.HasIndex(a => a.ManagerUserId)
                      .IsUnique()
                      .HasFilter("\"IsUnAssigned\" = false")
                      .HasDatabaseName("UX_PortfolioManagerAssignments_ManagerUserId_Active");

                entity.HasIndex(a => new { a.PortfolioManagerUserId, a.IsUnAssigned })
                      .HasDatabaseName("IX_PortfolioManagerAssignments_PortfolioManager_IsUnAssigned");

                entity.HasIndex(a => new { a.ProfileId, a.IsUnAssigned })
                      .HasDatabaseName("IX_PortfolioManagerAssignments_ProfileId_IsUnAssigned");
            });

            // Normalize DateTime Kind for User timestamptz columns.
            // PostgreSQL 'timestamp with time zone' stores a UTC instant; Npgsql refuses
            // to write a DateTime whose Kind is Unspecified. Some UAT/imported rows have
            // Kind=Unspecified after read, which breaks any subsequent SaveChanges on the
            // entity. This converter tags reads as Utc (value-preserving) and coerces
            // writes to Utc as a safety net. Scope: User.LastLoginDate / LastFailedLoginAt only.
            var utcNullableDateTimeConverter = new ValueConverter<DateTime?, DateTime?>(
                v => v.HasValue
                    ? (v.Value.Kind == DateTimeKind.Local
                        ? v.Value.ToUniversalTime()
                        : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc))
                    : v,
                v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

            modelBuilder.Entity<User>()
                .Property(u => u.LastLoginDate)
                .HasConversion(utcNullableDateTimeConverter);

            modelBuilder.Entity<User>()
                .Property(u => u.LastFailedLoginAt)
                .HasConversion(utcNullableDateTimeConverter);


            modelBuilder.Entity<AssignUserRight>()
                 .HasOne(ar => ar.UserGroup)
                 .WithMany(ug => ug.AssignUserRights)
                 .HasForeignKey(ar => ar.UserGroupId)
                 .OnDelete(DeleteBehavior.Cascade); // Optional

            modelBuilder.Entity<AssignUserRight>()
                .HasOne(ar => ar.SystemModule)
                .WithMany()
                .HasForeignKey(ar => ar.SystemModuleId);

            modelBuilder.Entity<AssignUserRight>()
                .HasOne(ar => ar.PromptType)
                .WithMany()
                .HasForeignKey(ar => ar.PromptTypeId);

            modelBuilder.Entity<BusinessUnit>()
                  .HasOne(bu => bu.Profile)
                  .WithMany(c => c.BusinessUnits)
                  .HasForeignKey(bu => bu.ProfileId)
                  .OnDelete(DeleteBehavior.Cascade);

            //Add for the Gpt model

            modelBuilder.Entity<DailyFeedback>()
                    .HasOne(df => df.Question)
                    .WithMany()
                    .HasForeignKey(df => df.DailyQuestionId);

            modelBuilder.Entity<DailyFeedback>()
                    .HasOne(df => df.Reason)
                    .WithMany()
                    .HasForeignKey(df => df.DailyReasonId)
                    .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<DailyReason>()
                    .HasOne(dr => dr.Specification)
                    .WithMany()
                    .HasForeignKey(dr => dr.SpecificationId);
            modelBuilder.Entity<UserGroup>(entity =>
            {
                entity.HasKey(e => e.Id);

                // Disable identity/auto-generation
                entity.Property(e => e.Id)
                      .ValueGeneratedNever();
            });

            modelBuilder.Entity<ActionEvent>()
            .HasOne(ae => ae.ActionHub)
            .WithMany(ah => ah.ActionEvents)
            .HasForeignKey(ae => ae.ActionHubId)
            .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<UserToken>(entity =>
            {
                entity.HasKey(e => e.AuthId);
                entity.Property(e => e.AuthId)
                      .ValueGeneratedOnAdd(); // Auto-increment PK

            });

            modelBuilder.Entity<DailyFeedback>()
           .ToTable("DailyFeedback", "public"); // exact table name

            modelBuilder.Entity<DailyFeedback>()
            .Property(f => f.Id)
            .ValueGeneratedOnAdd();

            // A reason is stored once per answer.
            modelBuilder.Entity<DailyFeedbackReason>()
                .HasIndex(r => new { r.DailyFeedbackId, r.DailyReasonId })
                .IsUnique();

            modelBuilder.Entity<TeamDailyFeedbackReason>()
                .HasIndex(r => new { r.TeamDailyFeedbackId, r.TeamDailyReasonId })
                .IsUnique();

            modelBuilder.Entity<TeamFeedback>()
                .HasIndex(f => new { f.BusinessUnitId, f.SubmissionDateTime });

            modelBuilder.Entity<DailyReason>()
            .HasOne(r => r.Specification)
            .WithMany(s => s.Reasons)
            .HasForeignKey(r => r.SpecificationId);

            modelBuilder.Entity<DailyQuestionSet>()
            .ToTable("DailyQuestionSets", "public");

            modelBuilder.Entity<EngagementInsights>()
            .Property(e => e.StatisticalSummaryJson)
            .HasColumnType("jsonb");

            modelBuilder.Entity<EngagementInsights>()
            .Property(e => e.BatchJobsId)
            .HasColumnName("BatchJobsId");

            modelBuilder.Entity<EngagementInsights>()
            .HasOne(ei => ei.BatchJob)
            .WithMany(b => b.EngagementInsights)
            .HasForeignKey(ei => ei.BatchJobsId);

            modelBuilder.Entity<Profile>()
                .HasOne(c => c.Partner)
                .WithMany(c => c.Customers)
                .HasForeignKey(c => c.PartnerId)
                .OnDelete(DeleteBehavior.Restrict);

            // Profile hierarchy: a profile may hang off a parent profile in the same
            // table. Restrict, not Cascade — deleting a parent must not silently take
            // its children (and everything hanging off them) with it.
            modelBuilder.Entity<Profile>()
                .HasOne(p => p.Parent)
                .WithMany(p => p.Children)
                .HasForeignKey(p => p.ParentId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Profile>()
                .HasIndex(p => p.ParentId);

            // Azure Marketplace mappings
            modelBuilder.Entity<AzureMarketplaceSubscription>(entity =>
            {
                entity.ToTable("AzureMarketplaceSubscription");
                entity.HasIndex(e => e.MarketplaceSubscriptionId).IsUnique();
                entity.HasIndex(e => e.ProfileId);
                entity.HasOne(e => e.Profile)
                      .WithMany()
                      .HasForeignKey(e => e.ProfileId)
                      .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<AzureMarketplaceWebhookEvent>(entity =>
            {
                entity.ToTable("AzureMarketplaceWebhookEvent");
                entity.HasIndex(e => e.OperationId).IsUnique();
                entity.HasIndex(e => e.MarketplaceSubscriptionId);
            });

            // LLMReportSchedule → Company (Restrict, soft delete via IsDel)
            modelBuilder.Entity<LLMReportSchedule>()
                .HasOne(s => s.Profile)
                .WithMany()
                .HasForeignKey(s => s.ProfileId)
                .OnDelete(DeleteBehavior.Restrict);

            // LLMReportSchedule → BusinessUnit
            modelBuilder.Entity<LLMReportSchedule>()
                .HasOne(s => s.BusinessUnit)
                .WithMany()
                .HasForeignKey(s => s.UnitId)
                .OnDelete(DeleteBehavior.Restrict);

            // One schedule row per (Company, Unit)
            modelBuilder.Entity<LLMReportSchedule>()
                .HasIndex(s => new { s.ProfileId, s.UnitId })
                .IsUnique();

            // Audit rows are looked up by the schedule they describe.
            modelBuilder.Entity<LLMReportScheduleAudit>()
                .HasIndex(a => a.ScheduleId);
        }
    }
}
