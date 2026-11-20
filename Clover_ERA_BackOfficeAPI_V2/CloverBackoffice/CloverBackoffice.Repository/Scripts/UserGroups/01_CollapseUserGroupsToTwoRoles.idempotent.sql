-- Collapses the six-role model down to the two roles the product has:
--   1 = Profile Admin, 2 = Manager
--
-- Mirrors migration 20260908090000_CollapseUserGroupsToTwoRoles. Re-runnable:
-- running it a second time is a no-op, because after the first run nothing sits
-- on 3, 4 or 5 any more and the two UserGroups rows are upserted, not inserted.
--
-- Retired groups: 2 Clover ERA Admin, 3 Customer Unit Lead, 4 Partner Admin,
-- 5 Team Manager. Company Admin keeps its id (1) and becomes Profile Admin.
--
-- Re-pointing, not deleting:
--   admin-shaped (2, 4) -> 1 Profile Admin
--   team-shaped  (3, 5) -> 2 Manager
-- There is no platform-wide administrator any more, so the ex-Clover ERA Admin
-- accounts become ordinary Profile Admins scoped to their own profile.

BEGIN;

-- Order matters: 2 and 4 must leave before Team Manager lands on 2.
UPDATE public."Users" SET "UserGroupId" = 1 WHERE "UserGroupId" IN (2, 4);
UPDATE public."Users" SET "UserGroupId" = 2 WHERE "UserGroupId" IN (3, 5);

UPDATE public."AssignUserRights" SET "UserGroupId" = 1 WHERE "UserGroupId" IN (2, 4);
UPDATE public."AssignUserRights" SET "UserGroupId" = 2 WHERE "UserGroupId" IN (3, 5);

-- No FK on this one; re-pointed so the coach's scope prompt keeps matching the
-- caller's real role.
UPDATE public."EranCoachConversations" SET "UserGroupId" = 1 WHERE "UserGroupId" IN (2, 4);
UPDATE public."EranCoachConversations" SET "UserGroupId" = 2 WHERE "UserGroupId" IN (3, 5);

DELETE FROM public."UserGroups" WHERE "Id" NOT IN (1, 2);

-- Both rows are asserted rather than assumed, so a database that never had the
-- legacy seed still ends up with exactly these two.
INSERT INTO public."UserGroups" ("Id", "Name", "IsAdmin", "UnitId", "OrganizationId", "Description")
VALUES (1, 'Profile Admin', true, 1, NULL, 'Administers one profile, and the Managers inside it.')
ON CONFLICT ("Id") DO UPDATE SET
    "Name" = EXCLUDED."Name",
    "IsAdmin" = EXCLUDED."IsAdmin",
    "UnitId" = EXCLUDED."UnitId",
    "OrganizationId" = EXCLUDED."OrganizationId",
    "Description" = EXCLUDED."Description";

INSERT INTO public."UserGroups" ("Id", "Name", "IsAdmin", "UnitId", "OrganizationId", "Description")
VALUES (2, 'Manager', false, 2, NULL, 'Runs the teams assigned to them, inside one profile.')
ON CONFLICT ("Id") DO UPDATE SET
    "Name" = EXCLUDED."Name",
    "IsAdmin" = EXCLUDED."IsAdmin",
    "UnitId" = EXCLUDED."UnitId",
    "OrganizationId" = EXCLUDED."OrganizationId",
    "Description" = EXCLUDED."Description";

COMMIT;
