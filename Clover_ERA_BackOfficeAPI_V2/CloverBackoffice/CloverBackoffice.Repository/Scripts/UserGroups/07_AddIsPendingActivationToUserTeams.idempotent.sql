-- Adds UserTeams.IsPendingActivation: a Team Manager replacement waiting for the
-- invited manager to accept their invitation.
--
-- Mirrors migration 20260916090000_AddIsPendingActivationToUserTeams. Re-runnable
-- in pgAdmin (F5).
--
-- A pending row is written with IsUnAssigned = true, so the outgoing manager stays
-- the team's active manager until the invitation is accepted.

ALTER TABLE public."UserTeams"
    ADD COLUMN IF NOT EXISTS "IsPendingActivation" boolean NOT NULL DEFAULT false;

CREATE INDEX IF NOT EXISTS "IX_UserTeams_Pending_UserId"
    ON public."UserTeams" ("UserId")
    WHERE "IsPendingActivation" = true;

-- Record the migration so `dotnet ef database update` does not try it again.
INSERT INTO public."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
SELECT '20260916090000_AddIsPendingActivationToUserTeams', '9.0.1'
WHERE NOT EXISTS (
    SELECT 1 FROM public."__EFMigrationsHistory"
    WHERE "MigrationId" = '20260916090000_AddIsPendingActivationToUserTeams');

-- Summary
SELECT
    (SELECT COUNT(*) FROM information_schema.columns
      WHERE table_name = 'UserTeams' AND column_name = 'IsPendingActivation') AS column_exists,
    (SELECT COUNT(*) FROM public."UserTeams" WHERE "IsPendingActivation") AS pending_replacements,
    (SELECT COUNT(*) FROM public."__EFMigrationsHistory"
      WHERE "MigrationId" = '20260916090000_AddIsPendingActivationToUserTeams') AS migration_recorded;
