using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Infrastructure.Models.PlanManagement;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Repository.Provider
{
    /// <summary>
    /// Relational configuration for the plan catalogue (api/admin/plans). Five new tables;
    /// nothing existing is altered (the legacy Plans table is untouched).
    /// </summary>
    public static class PlanManagementModelConfiguration
    {
        public static void Apply(ModelBuilder b)
        {
            b.Entity<ServicePlan>(e =>
            {
                e.HasKey(x => x.Id);

                e.HasIndex(x => x.Code).IsUnique().HasDatabaseName("UX_ServicePlans_Code");

                // Optimistic concurrency: a write carrying an older version fails.
                e.Property(x => x.Version).IsConcurrencyToken();

                e.HasMany(x => x.PriceTiers).WithOne(x => x.ServicePlan)
                    .HasForeignKey(x => x.ServicePlanId)
                    .OnDelete(DeleteBehavior.Cascade);

                e.HasMany(x => x.Features).WithOne(x => x.ServicePlan)
                    .HasForeignKey(x => x.ServicePlanId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            b.Entity<ServicePlanPriceTier>(e =>
            {
                e.HasKey(x => x.Id);

                e.HasIndex(x => new { x.ServicePlanId, x.MinManagers })
                    .IsUnique()
                    .HasDatabaseName("UX_ServicePlanPriceTiers_Plan_Min");
            });

            b.Entity<PlanFeature>(e =>
            {
                e.HasKey(x => x.Id);

                e.HasIndex(x => x.Key).IsUnique().HasDatabaseName("UX_PlanFeatures_Key");
            });

            b.Entity<ServicePlanFeature>(e =>
            {
                e.HasKey(x => x.Id);

                // One cell per plan and feature.
                e.HasIndex(x => new { x.ServicePlanId, x.PlanFeatureId })
                    .IsUnique()
                    .HasDatabaseName("UX_ServicePlanFeatures_Plan_Feature");

                e.HasOne(x => x.PlanFeature).WithMany()
                    .HasForeignKey(x => x.PlanFeatureId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // Frozen terms per plan version; one row per plan and number.
            b.Entity<ServicePlanVersion>(e =>
            {
                e.HasKey(x => x.Id);
                e.HasIndex(x => new { x.ServicePlanId, x.VersionNumber })
                    .IsUnique()
                    .HasDatabaseName("UX_ServicePlanVersions_Plan_Number");
                e.HasOne(x => x.ServicePlan).WithMany()
                    .HasForeignKey(x => x.ServicePlanId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // Licence / subscription → plan and plan version (nullable; old rows have none).
            // RESTRICT: a plan or version that licences point at can never be deleted.
            b.Entity<CompanyLicense>(e =>
            {
                e.HasOne<ServicePlan>().WithMany()
                    .HasForeignKey(x => x.ServicePlanId)
                    .OnDelete(DeleteBehavior.Restrict);
                e.HasIndex(x => x.ServicePlanId).HasDatabaseName("IX_CompanyLicenses_ServicePlanId");
                e.HasOne<ServicePlanVersion>().WithMany()
                    .HasForeignKey(x => x.ServicePlanVersionId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            b.Entity<CompanySubscription>(e =>
            {
                e.HasOne<ServicePlan>().WithMany()
                    .HasForeignKey(x => x.ServicePlanId)
                    .OnDelete(DeleteBehavior.Restrict);
                e.HasIndex(x => x.ServicePlanId).HasDatabaseName("IX_CompanySubscriptions_ServicePlanId");
                e.HasOne<ServicePlanVersion>().WithMany()
                    .HasForeignKey(x => x.ServicePlanVersionId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            b.Entity<ServicePlanAuditLog>(e =>
            {
                e.HasKey(x => x.Id);

                e.HasIndex(x => new { x.ServicePlanId, x.CreatedAt })
                    .HasDatabaseName("IX_ServicePlanAuditLogs_Plan_CreatedAt");

                e.HasIndex(x => new { x.PlanFeatureId, x.CreatedAt })
                    .HasDatabaseName("IX_ServicePlanAuditLogs_Feature_CreatedAt");
            });
        }
    }
}
