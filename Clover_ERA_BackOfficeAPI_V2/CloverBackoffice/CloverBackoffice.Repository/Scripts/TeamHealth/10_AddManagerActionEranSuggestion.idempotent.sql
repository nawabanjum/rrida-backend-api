-- =============================================================================
-- 10_AddManagerActionEranSuggestion.idempotent.sql
--
-- ERAN suggestion dropdown on a manager's own (manual) action:
-- POST / PATCH api/actions "engineActionId" + "suggestionChoice".
--
--   ManagerActions.SuggestionChoice      varchar(20), NULL  SUGGESTION / NONE / NONE_OF_THESE
--   ManagerActions.EngineDeliveryId      bigint, NULL       the suggestion's id when picked
--   ManagerActions.EngineCycleKey        varchar(20), NULL  } the engine's natural key, so the
--   ManagerActions.EngineRung            integer, NULL      } link survives an engine re-run
--   ManagerActions.EngineActionKey       varchar(30), NULL  }
--   ManagerActions.EngineSuggestionText  varchar(1000), NULL ERAN's wording when picked
--   ManagerActions.EngineSuggestedOn     date, NULL         the day ERAN suggested it
--
-- Nullable columns only; existing rows stay NULL (not linked). No foreign key to
-- EngineCycleDeliveries on purpose: the engine rebuilds those rows.
--
-- Run AFTER 09 in this folder.
--
-- Exact output of
--     dotnet ef migrations script 20261118113237_AddManagerActionStartDate \
--                                 20261120133540_AddManagerActionEranSuggestion --idempotent
-- with only this header, the summary SELECT and ProductVersion 9.0.5 (as the
-- other migrations) changed.
--
-- Safe to run more than once (guarded on the migration history row), one
-- transaction. Run the whole file in pgAdmin (F5). Run BEFORE deploying: every
-- api/actions path reads these columns.
-- =============================================================================

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261120133540_AddManagerActionEranSuggestion') THEN
    ALTER TABLE public."ManagerActions" ADD "EngineActionKey" character varying(30);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261120133540_AddManagerActionEranSuggestion') THEN
    ALTER TABLE public."ManagerActions" ADD "EngineCycleKey" character varying(20);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261120133540_AddManagerActionEranSuggestion') THEN
    ALTER TABLE public."ManagerActions" ADD "EngineDeliveryId" bigint;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261120133540_AddManagerActionEranSuggestion') THEN
    ALTER TABLE public."ManagerActions" ADD "EngineRung" integer;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261120133540_AddManagerActionEranSuggestion') THEN
    ALTER TABLE public."ManagerActions" ADD "EngineSuggestedOn" date;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261120133540_AddManagerActionEranSuggestion') THEN
    ALTER TABLE public."ManagerActions" ADD "EngineSuggestionText" character varying(1000);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261120133540_AddManagerActionEranSuggestion') THEN
    ALTER TABLE public."ManagerActions" ADD "SuggestionChoice" character varying(20);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261120133540_AddManagerActionEranSuggestion') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261120133540_AddManagerActionEranSuggestion', '9.0.5');
    END IF;
END $EF$;
COMMIT;




-- Summary
SELECT
    (SELECT COUNT(*) FROM "__EFMigrationsHistory"
      WHERE "MigrationId" = '20261120133540_AddManagerActionEranSuggestion')     AS migration_applied,
    (SELECT COUNT(*) FROM information_schema.columns
      WHERE table_schema = 'public' AND table_name = 'ManagerActions'
        AND column_name IN ('SuggestionChoice', 'EngineDeliveryId', 'EngineCycleKey', 'EngineRung',
                            'EngineActionKey', 'EngineSuggestionText', 'EngineSuggestedOn')) AS new_columns_7_expected,
    (SELECT COUNT(*) FROM public."ManagerActions" WHERE "SuggestionChoice" IS NOT NULL) AS rows_with_choice;
