START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261020102702_AddEngineRotationAndGuard') THEN
    ALTER TABLE "EngineTeamSettings" ADD "Rotation" character varying(20) NOT NULL DEFAULT 'production';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261020102702_AddEngineRotationAndGuard') THEN
    UPDATE public."EngineTeamSettings" s
    SET "Rotation" = 'demo'
    WHERE EXISTS (SELECT 1 FROM public."EngineAnswers" a WHERE a."BusinessUnitId" = s."BusinessUnitId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261020102702_AddEngineRotationAndGuard') THEN
    ALTER TABLE "EngineLlmCalls" ADD "GuardOutcome" character varying(30);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261020102702_AddEngineRotationAndGuard') THEN
    ALTER TABLE "EngineLlmCalls" ADD "GuardRules" character varying(500);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261020102702_AddEngineRotationAndGuard') THEN
    ALTER TABLE "EngineFeedItems" ADD "GuardOutcome" character varying(30);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261020102702_AddEngineRotationAndGuard') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261020102702_AddEngineRotationAndGuard', '9.0.5');
    END IF;
END $EF$;
COMMIT;

