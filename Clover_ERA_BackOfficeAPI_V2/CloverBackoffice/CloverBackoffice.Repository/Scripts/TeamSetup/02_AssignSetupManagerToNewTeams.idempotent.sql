-- =============================================================================
-- 02_AssignSetupManagerToNewTeams.idempotent.sql
--
-- One-off data fix for teams created through POST api/team-setup/upsert-team-setup
-- (unitId 0) BEFORE the fix that makes the Team Manager who set the team up its
-- manager. Those teams have no UserTeams row, so assignable-teams returns
-- hasManager: false and login returns no managedTeams.
--
-- A team qualifies only when ALL hold:
--   * the unit and its setup row are live;
--   * the setup row was written in the same request that created the unit
--     (TeamSetups.CreatedAt within 60 seconds of BusinessUnits.RegistrationDate);
--   * the setup author is a live Team Manager of the same profile;
--   * the unit has NEVER had a UserTeams row (assigned or unassigned) — a team
--     whose manager was later changed or removed is left alone.
--
-- Re-runnable: a fixed team gains a UserTeams row and no longer qualifies.
-- Note: the onboarding job may send the manager one "team assigned" email for a
-- row inserted here if it runs within its catch-up window.
-- =============================================================================

BEGIN;

WITH candidates AS (
    SELECT ts."UnitId", ts."CreatedBy" AS "UserId"
    FROM "TeamSetups" ts
    JOIN "BusinessUnits" bu
      ON bu."Id" = ts."UnitId"
     AND COALESCE(bu."IsDel", false) = false
    JOIN "Users" u
      ON u."UserId" = ts."CreatedBy"
     AND u."ProfileId" = bu."ProfileId"
     AND u."IsDel" = false
    WHERE ts."IsDel" = false
      AND abs(extract(epoch FROM (ts."CreatedAt" - bu."RegistrationDate"))) <= 60
      AND (
            EXISTS (SELECT 1 FROM "UserRoles" r
                    WHERE r."UserId" = u."UserId" AND r."UserGroupId" = 2)
         OR (NOT EXISTS (SELECT 1 FROM "UserRoles" r WHERE r."UserId" = u."UserId")
             AND u."UserGroupId" = 2)
          )
      AND NOT EXISTS (SELECT 1 FROM "UserTeams" ut WHERE ut."UnitId" = ts."UnitId")
)
INSERT INTO "UserTeams" ("UserId", "UnitId", "CreatedBy", "CreatedDate", "IsUnAssigned", "IsReadOnly", "IsNewTeam")
SELECT c."UserId", c."UnitId", c."UserId", now(), false, false, false
FROM candidates c;

COMMIT;

-- Summary: every team set up by its creator, with its live manager (if any).
SELECT bu."Id"            AS "UnitId",
       bu."Name"          AS "TeamName",
       bu."ProfileId",
       ts."CreatedBy"     AS "SetupBy",
       ut."UserId"        AS "ManagerUserId",
       u."Name"           AS "ManagerName",
       u."Username"       AS "ManagerEmail",
       ut."CreatedDate"   AS "AssignedAt"
FROM "TeamSetups" ts
JOIN "BusinessUnits" bu ON bu."Id" = ts."UnitId" AND COALESCE(bu."IsDel", false) = false
LEFT JOIN "UserTeams" ut ON ut."UnitId" = bu."Id" AND ut."IsUnAssigned" = false
LEFT JOIN "Users" u ON u."UserId" = ut."UserId"
WHERE ts."IsDel" = false
ORDER BY bu."Id" DESC;
