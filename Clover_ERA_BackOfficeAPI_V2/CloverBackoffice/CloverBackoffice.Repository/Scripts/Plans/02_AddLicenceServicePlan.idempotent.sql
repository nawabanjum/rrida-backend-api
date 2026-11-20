-- =============================================================================
-- 02_AddLicenceServicePlan.idempotent.sql
--
-- Links licences and subscriptions to the plan catalogue (ServicePlans):
--
--   CompanyLicenses.ServicePlanId       the plan the licence gives its Team Manager
--   CompanySubscriptions.ServicePlanId  the plan the subscription was bought for
--
-- Both NULLABLE, no backfill: existing rows stay NULL and are read as the legacy
-- plan (Pro) by GET api/entitlements. Nothing else changes; the legacy PlanId
-- columns are untouched.
--
-- Run AFTER 01_AddServicePlanCatalog.idempotent.sql.
--
-- Exact output of
--     dotnet ef migrations script 20261111145347_AddServicePlanCatalog \
--                                 20261111165338_AddLicenceServicePlan --idempotent
-- with only this header and the summary SELECT added.
--
-- Safe to run more than once, one transaction. Run the whole file in pgAdmin (F5).
-- Rollback: ALTER TABLE "CompanyLicenses" DROP COLUMN "ServicePlanId";
--           ALTER TABLE "CompanySubscriptions" DROP COLUMN "ServicePlanId";
--           DELETE FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261111165338_AddLicenceServicePlan';
-- =============================================================================

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261111165338_AddLicenceServicePlan') THEN
    ALTER TABLE "CompanySubscriptions" ADD "ServicePlanId" integer;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261111165338_AddLicenceServicePlan') THEN
    ALTER TABLE "CompanyLicenses" ADD "ServicePlanId" integer;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261111165338_AddLicenceServicePlan') THEN
    CREATE INDEX "IX_CompanySubscriptions_ServicePlanId" ON "CompanySubscriptions" ("ServicePlanId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261111165338_AddLicenceServicePlan') THEN
    CREATE INDEX "IX_CompanyLicenses_ServicePlanId" ON "CompanyLicenses" ("ServicePlanId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261111165338_AddLicenceServicePlan') THEN
    ALTER TABLE "CompanyLicenses" ADD CONSTRAINT "FK_CompanyLicenses_ServicePlans_ServicePlanId" FOREIGN KEY ("ServicePlanId") REFERENCES public."ServicePlans" ("Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261111165338_AddLicenceServicePlan') THEN
    ALTER TABLE "CompanySubscriptions" ADD CONSTRAINT "FK_CompanySubscriptions_ServicePlans_ServicePlanId" FOREIGN KEY ("ServicePlanId") REFERENCES public."ServicePlans" ("Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261111165338_AddLicenceServicePlan') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261111165338_AddLicenceServicePlan', '9.0.5');
    END IF;
END $EF$;
COMMIT;




-- Summary
SELECT
    (SELECT COUNT(*) FROM "__EFMigrationsHistory"
      WHERE "MigrationId" = '20261111165338_AddLicenceServicePlan')         AS migration_applied,
    (SELECT COUNT(*) FROM information_schema.columns
      WHERE table_name IN ('CompanyLicenses', 'CompanySubscriptions')
        AND column_name = 'ServicePlanId')                                AS columns_added,
    (SELECT COUNT(*) FROM "CompanyLicenses" WHERE "ServicePlanId" IS NULL) AS licences_on_legacy_plan,
    (SELECT COUNT(*) FROM "CompanyLicenses" WHERE "ServicePlanId" IS NOT NULL) AS licences_with_plan;
