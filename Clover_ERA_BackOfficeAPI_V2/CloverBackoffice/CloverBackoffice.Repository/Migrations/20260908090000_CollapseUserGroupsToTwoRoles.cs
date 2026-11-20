using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CloverBackoffice.Repository.Migrations
{
    /// <summary>
    /// Collapses the six-role model down to the two roles the product has:
    /// Profile Admin (Id 1) and Manager (Id 2).
    ///
    /// The retired groups are Clover ERA Admin (2), Customer Unit Lead (3),
    /// Partner Admin (4) and Team Manager (5). Company Admin (1) keeps its id and
    /// becomes Profile Admin, so every existing Company Admin stays valid; Team
    /// Manager moves from 5 down onto 2, which is why the old Clover ERA Admin
    /// rows have to be re-pointed at 1 first.
    ///
    /// Existing rows are re-pointed rather than deleted:
    ///   admin-shaped (2 Clover ERA Admin, 4 Partner Admin) -> 1 Profile Admin
    ///   team-shaped  (3 Customer Unit Lead, 5 Team Manager) -> 2 Manager
    /// There is deliberately no platform-wide administrator any more, so the
    /// ex-Clover ERA Admin accounts become ordinary Profile Admins scoped to their
    /// own profile.
    ///
    /// Two tables carry a group id: Users (FK to UserGroups) and
    /// AssignUserRights (FK, empty at the time of writing). EranCoachConversations
    /// stores one as a plain column with no FK and is re-pointed too so the coach's
    /// scope prompt keeps matching the caller's real role.
    ///
    /// No schema changes — data only. This is not a scaffolded migration: the
    /// model is unchanged, so `migrations add` would have produced an empty Up()
    /// plus unrelated drift. Mirrors
    /// Scripts/UserGroups/01_CollapseUserGroupsToTwoRoles.idempotent.sql.
    /// </summary>
    public partial class CollapseUserGroupsToTwoRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                -- Order matters: 2 and 4 must leave before Team Manager lands on 2.
                UPDATE public."Users" SET "UserGroupId" = 1 WHERE "UserGroupId" IN (2, 4);
                UPDATE public."Users" SET "UserGroupId" = 2 WHERE "UserGroupId" IN (3, 5);

                UPDATE public."AssignUserRights" SET "UserGroupId" = 1 WHERE "UserGroupId" IN (2, 4);
                UPDATE public."AssignUserRights" SET "UserGroupId" = 2 WHERE "UserGroupId" IN (3, 5);

                UPDATE public."EranCoachConversations" SET "UserGroupId" = 1 WHERE "UserGroupId" IN (2, 4);
                UPDATE public."EranCoachConversations" SET "UserGroupId" = 2 WHERE "UserGroupId" IN (3, 5);

                -- Nothing references them now.
                DELETE FROM public."UserGroups" WHERE "Id" NOT IN (1, 2);

                -- Both rows are asserted rather than assumed: a database that never
                -- had the legacy seed still ends up with exactly these two.
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
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The six groups come back, but the merge is not reversible per row:
            // which of 2/4 an account started as, and which of 3/5, is recorded
            // nowhere. Each pair is therefore rolled back to the LESS privileged of
            // its two members — id 1 stays Company Admin (never CloverERA Admin),
            // and everything on id 2 is moved to Team Manager BEFORE 2 is renamed
            // back, so no Manager is silently handed the platform-admin role.
            migrationBuilder.Sql("""
                INSERT INTO public."UserGroups" ("Id", "Name", "IsAdmin", "UnitId", "OrganizationId", "Description") VALUES
                    (3, 'Customer Unit Lead', false, 3, 5, NULL),
                    (4, 'Partner Admin',      false, 4, 6, NULL),
                    (5, 'Team Manager',       false, 5, 2, NULL)
                ON CONFLICT ("Id") DO NOTHING;

                UPDATE public."Users"                  SET "UserGroupId" = 5 WHERE "UserGroupId" = 2;
                UPDATE public."AssignUserRights"       SET "UserGroupId" = 5 WHERE "UserGroupId" = 2;
                UPDATE public."EranCoachConversations" SET "UserGroupId" = 5 WHERE "UserGroupId" = 2;

                UPDATE public."UserGroups"
                   SET "Name" = 'Company Admin', "IsAdmin" = true, "UnitId" = 2, "OrganizationId" = 4, "Description" = NULL
                 WHERE "Id" = 1;

                UPDATE public."UserGroups"
                   SET "Name" = 'CloverERA Admin', "IsAdmin" = true, "UnitId" = 1, "OrganizationId" = 3, "Description" = NULL
                 WHERE "Id" = 2;
                """);
        }
    }
}
