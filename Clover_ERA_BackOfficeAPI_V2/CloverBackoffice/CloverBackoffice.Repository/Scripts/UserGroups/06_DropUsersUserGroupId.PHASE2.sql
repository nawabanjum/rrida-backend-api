-- =============================================================================
-- 06  PHASE 2 - drop the single-role column Users.UserGroupId
-- =============================================================================
--
-- DO NOT RUN YET.
--
-- Script 05 made UserRoles the source of truth and left Users.UserGroupId in place
-- as a derived "highest role" column, because ~35 files still read it: the JWT
-- claim, TenantAccessService, UnitAccessService, the permission matrix at login,
-- SystemNotificationRepository, subscriptions, dashboards, EranCoach, partner
-- screens. Dropping the column before those readers move to UserRoles breaks
-- login and tenant isolation.
--
-- Run only when ALL of these are true:
--   [ ] `grep -r "UserGroupId" --include=*.cs` finds no read of User.UserGroupId
--       outside Migrations/ (AssignUserRights.UserGroupId and
--       EranCoachConversations.UserGroupId are different columns and stay).
--   [ ] User.UserGroupId / User.UserGroup removed from the model, the
--       ApplicationDbContext SaveChanges role hook removed, and a matching EF
--       migration generated.
--   [ ] Frontend no longer depends on login `userGroup` / get-users `userGroupId`
--       (or the API computes them from UserRoles).
--   [ ] Script 05 section 9c returns 0 rows.
-- =============================================================================

-- PRE-FLIGHT (read-only): must return 0.
SELECT COUNT(*) AS users_without_any_role
  FROM public."Users" u
 WHERE NOT EXISTS (SELECT 1 FROM public."UserRoles" r WHERE r."UserId" = u."UserId");


BEGIN;

DO $$
DECLARE
    missing integer;
BEGIN
    SELECT COUNT(*) INTO missing
      FROM public."Users" u
     WHERE NOT EXISTS (SELECT 1 FROM public."UserRoles" r WHERE r."UserId" = u."UserId");
    IF missing > 0 THEN
        RAISE EXCEPTION '% user(s) have no UserRoles row. Refusing to drop Users.UserGroupId.', missing;
    END IF;
END $$;

ALTER TABLE public."Users" DROP CONSTRAINT IF EXISTS "FK_Users_UserGroups_UserGroupId";
DROP INDEX IF EXISTS public."IX_Users_UserGroupId";
ALTER TABLE public."Users" DROP COLUMN IF EXISTS "UserGroupId";

COMMIT;


-- =============================================================================
-- ROLLBACK (manual): restore the column from UserRoles, highest role wins.
-- =============================================================================
-- BEGIN;
-- ALTER TABLE public."Users" ADD COLUMN IF NOT EXISTS "UserGroupId" integer;
-- UPDATE public."Users" u
--    SET "UserGroupId" = r."PrimaryGroupId"
--   FROM (SELECT "UserId",
--                (array_agg("UserGroupId" ORDER BY
--                     CASE "UserGroupId" WHEN 3 THEN 0 WHEN 1 THEN 1
--                                        WHEN 2 THEN 2 WHEN 4 THEN 3 ELSE 99 END))[1] AS "PrimaryGroupId"
--           FROM public."UserRoles" GROUP BY "UserId") r
--  WHERE u."UserId" = r."UserId";
-- ALTER TABLE public."Users" ALTER COLUMN "UserGroupId" SET NOT NULL;
-- CREATE INDEX IF NOT EXISTS "IX_Users_UserGroupId" ON public."Users" ("UserGroupId");
-- ALTER TABLE public."Users" ADD CONSTRAINT "FK_Users_UserGroups_UserGroupId"
--     FOREIGN KEY ("UserGroupId") REFERENCES public."UserGroups" ("Id") ON DELETE RESTRICT;
-- COMMIT;
