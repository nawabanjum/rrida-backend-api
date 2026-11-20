-- =============================================================================
-- 03_AddCloverResourcesFeature.idempotent.sql
--
-- Plan enforcement support:
--   PlanFeatures.ShowInComparison   false for a feature enforced per plan but shown as
--                                   part of another row (default true: existing rows stay)
--   CLOVER_RESOURCES feature        worksheets with the weekly action - Light no, Pro yes
--                                   ("*with Clover Resources"), not a comparison row
--
-- Run AFTER 01 and 02. Safe to run more than once, one transaction. Run the whole
-- file in pgAdmin (F5).
--
-- Exact output of
--     dotnet ef migrations script 20261111165338_AddLicenceServicePlan \
--                                 20261112072858_AddCloverResourcesFeature --idempotent
-- with only this header and the summary SELECT added.
-- =============================================================================

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261112072858_AddCloverResourcesFeature') THEN
    ALTER TABLE public."PlanFeatures" ADD "ShowInComparison" boolean NOT NULL DEFAULT TRUE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261112072858_AddCloverResourcesFeature') THEN
    INSERT INTO public."PlanFeatures"
        ("Key", "Title", "Description", "ValueType", "LimitUnit", "LimitPeriod", "Footnote",
         "IsSystem", "ShowInComparison", "SortOrder", "IsActive", "CreatedBy", "CreatedAt", "UpdatedAt")
    VALUES
        ('CLOVER_RESOURCES', 'Clover Resources', 'Worksheets and resources with the weekly suggested action.',
         'INCLUDED', NULL, NULL, NULL, true, false, 12, true, 0, now(), now())
    ON CONFLICT ("Key") DO NOTHING;

    INSERT INTO public."ServicePlanFeatures"
        ("ServicePlanId", "PlanFeatureId", "IsIncluded", "LimitValue", "DisplayText", "SubText", "UpdatedAt")
    SELECT p."Id", f."Id", c.included, NULL, NULL, NULL, now()
    FROM (VALUES ('light', false), ('pro', true)) AS c(code, included)
    JOIN public."ServicePlans" p ON p."Code" = c.code
    JOIN public."PlanFeatures" f ON f."Key" = 'CLOVER_RESOURCES'
    ON CONFLICT ("ServicePlanId", "PlanFeatureId") DO NOTHING;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261112072858_AddCloverResourcesFeature') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261112072858_AddCloverResourcesFeature', '9.0.5');
    END IF;
END $EF$;
COMMIT;




-- Summary
SELECT
    (SELECT COUNT(*) FROM "__EFMigrationsHistory"
      WHERE "MigrationId" = '20261112072858_AddCloverResourcesFeature')    AS migration_applied,
    (SELECT COUNT(*) FROM public."PlanFeatures")                          AS features,
    (SELECT COUNT(*) FROM public."PlanFeatures" WHERE "ShowInComparison") AS comparison_rows;

SELECT p."Code", f."Key", c."IsIncluded"
FROM public."ServicePlanFeatures" c
JOIN public."ServicePlans" p ON p."Id" = c."ServicePlanId"
JOIN public."PlanFeatures" f ON f."Id" = c."PlanFeatureId"
WHERE f."Key" = 'CLOVER_RESOURCES'
ORDER BY p."SortOrder";
