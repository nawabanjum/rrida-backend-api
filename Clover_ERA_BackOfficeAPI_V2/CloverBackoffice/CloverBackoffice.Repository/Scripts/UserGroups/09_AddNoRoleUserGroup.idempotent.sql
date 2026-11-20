-- Adds the "No Role" row (Id 0) to UserGroups.
--
-- Removing Profile Admin from a user whose only role it was leaves them with no
-- UserRoles rows. Users.UserGroupId is NOT NULL with a foreign key to UserGroups,
-- so it needs a row to point at that is not a role: Id 0 (RoleHierarchy.NoRoleGroupId).
-- Leaving it at 1 would keep the user a Profile Admin for every legacy reader.
--
-- Not offered in any role picker (filtered in code). Re-runnable (F5).
-- Must run BEFORE deploying the API change, or removing a Profile-Admin-only
-- user fails on the foreign key.

INSERT INTO public."UserGroups" ("Id", "Name", "IsAdmin", "UnitId", "OrganizationId", "Description")
VALUES (0, 'No Role', false, 0, NULL, 'Marks a user who currently holds no role. Not assignable.')
ON CONFLICT ("Id") DO UPDATE SET
    "Name" = EXCLUDED."Name",
    "IsAdmin" = EXCLUDED."IsAdmin",
    "UnitId" = EXCLUDED."UnitId",
    "Description" = EXCLUDED."Description";

-- Summary
SELECT g."Id", g."Name", g."IsAdmin",
       (SELECT COUNT(*) FROM public."Users" u WHERE u."UserGroupId" = g."Id" AND NOT u."IsDel") AS users
FROM public."UserGroups" g
ORDER BY g."Id";
