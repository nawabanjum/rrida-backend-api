-- Takes the role model from two roles to four:
--
--     1  Profile Admin       unchanged
--     2  Team Manager        renamed from "Manager"
--     3  RRIDA Admin         new - platform super admin
--     4  Portfolio Manager   new - assignable, no permissions yet
--
-- Mirrors migration 20260908160000_AddRridaAdminAndPortfolioManagerRoles.
-- Re-runnable: rows 3 and 4 are upserted, and row 2's rename is idempotent.
--
-- RRIDA Admin is the Clover ERA Admin of the original six-role model under its new
-- name. It could not reuse the old id 2 - that id now belongs to Team Manager and
-- is held by live accounts - so the two new roles take the next free numbers. The
-- ids are therefore NOT in hierarchy order; UserGroups.cs records the hierarchy.
--
-- No user rows move. The RRIDA Admins are created by DatabaseSeeder at startup,
-- which is the only thing in the system that mints that role.

BEGIN;

UPDATE public."UserGroups"
   SET "Name" = 'Team Manager',
       "Description" = 'Runs the teams assigned to them, inside one profile.'
 WHERE "Id" = 2;

INSERT INTO public."UserGroups" ("Id", "Name", "IsAdmin", "UnitId", "OrganizationId", "Description")
VALUES (3, 'RRIDA Admin', true, 3, NULL, 'Platform super admin. Sees and manages every profile.')
ON CONFLICT ("Id") DO UPDATE SET
    "Name" = EXCLUDED."Name",
    "IsAdmin" = EXCLUDED."IsAdmin",
    "UnitId" = EXCLUDED."UnitId",
    "Description" = EXCLUDED."Description";

INSERT INTO public."UserGroups" ("Id", "Name", "IsAdmin", "UnitId", "OrganizationId", "Description")
VALUES (4, 'Portfolio Manager', false, 4, NULL, 'Additional role a Profile Admin may assign. No data access yet.')
ON CONFLICT ("Id") DO UPDATE SET
    "Name" = EXCLUDED."Name",
    "IsAdmin" = EXCLUDED."IsAdmin",
    "UnitId" = EXCLUDED."UnitId",
    "Description" = EXCLUDED."Description";

COMMIT;
