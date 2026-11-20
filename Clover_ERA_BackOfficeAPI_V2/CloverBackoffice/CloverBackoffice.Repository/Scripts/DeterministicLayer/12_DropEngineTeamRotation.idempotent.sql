START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261020133345_DropEngineTeamRotation') THEN
    ALTER TABLE "EngineTeamSettings" DROP COLUMN "Rotation";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261020133345_DropEngineTeamRotation') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261020133345_DropEngineTeamRotation', '9.0.5');
    END IF;
END $EF$;
COMMIT;

