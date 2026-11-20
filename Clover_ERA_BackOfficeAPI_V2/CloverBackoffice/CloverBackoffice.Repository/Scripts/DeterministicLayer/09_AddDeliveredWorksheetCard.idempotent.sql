START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261014125445_AddDeliveredWorksheetCard') THEN
    ALTER TABLE "EngineCycleDeliveries" ADD "DeliveredWorksheetCardJson" text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261014125445_AddDeliveredWorksheetCard') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261014125445_AddDeliveredWorksheetCard', '9.0.5');
    END IF;
END $EF$;
COMMIT;

