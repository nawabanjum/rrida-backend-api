-- =============================================================================
-- 04_AddSubscriptionPlanUnitPrice.idempotent.sql
--
--   CompanySubscriptions.ServicePlanUnitPrice  price per manager a plan checkout
--                                              charged (tier for the quantity, for the
--                                              billing cycle). NULLABLE, no backfill:
--                                              older subscriptions show today's tier price.
--
-- Run AFTER 01, 02 and 03. Safe to run more than once, one transaction. Run the
-- whole file in pgAdmin (F5).
--
-- Exact output of
--     dotnet ef migrations script 20261112072858_AddCloverResourcesFeature \
--                                 20261112074844_AddSubscriptionPlanUnitPrice --idempotent
-- with only this header and the summary SELECT added.
-- =============================================================================

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261112074844_AddSubscriptionPlanUnitPrice') THEN
    ALTER TABLE "CompanySubscriptions" ADD "ServicePlanUnitPrice" numeric(10,2);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261112074844_AddSubscriptionPlanUnitPrice') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261112074844_AddSubscriptionPlanUnitPrice', '9.0.5');
    END IF;
END $EF$;
COMMIT;




-- Summary
SELECT
    (SELECT COUNT(*) FROM "__EFMigrationsHistory"
      WHERE "MigrationId" = '20261112074844_AddSubscriptionPlanUnitPrice')  AS migration_applied,
    (SELECT COUNT(*) FROM information_schema.columns
      WHERE table_name = 'CompanySubscriptions' AND column_name = 'ServicePlanUnitPrice') AS column_added;
