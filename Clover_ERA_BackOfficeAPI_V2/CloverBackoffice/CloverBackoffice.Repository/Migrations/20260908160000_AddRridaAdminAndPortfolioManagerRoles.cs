using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CloverBackoffice.Repository.Migrations
{
    /// <summary>
    /// Takes the role model from two roles to four:
    ///
    ///     1  Profile Admin       unchanged
    ///     2  Team Manager        renamed from "Manager"
    ///     3  RRIDA Admin         new - platform super admin
    ///     4  Portfolio Manager   new - assignable, no permissions yet
    ///
    /// RRIDA Admin is the Clover ERA Admin of the original six-role model, brought
    /// back under its new name. It could NOT reuse the old id 2: that id now belongs
    /// to Team Manager and is held by live accounts, so the two new roles take the
    /// next free numbers instead. The ids are therefore not in hierarchy order —
    /// UserGroups.cs is the place that records the hierarchy.
    ///
    /// No user rows move. Every existing account is a Profile Admin (1) or a Team
    /// Manager (2) and stays exactly where it is; the RRIDA Admins are created by
    /// DatabaseSeeder at startup, which is the only thing that mints that role.
    ///
    /// No schema changes — data only, and not scaffolded: the EF model does not
    /// change, so `migrations add` would have produced an empty Up() plus unrelated
    /// drift. Mirrors
    /// Scripts/UserGroups/03_AddRridaAdminAndPortfolioManagerRoles.idempotent.sql.
    /// </summary>
    public partial class AddRridaAdminAndPortfolioManagerRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
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
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Anyone holding one of the two new roles is moved to the nearest role
            // that still exists BEFORE the rows go, or the foreign key would refuse
            // the delete. A RRIDA Admin becomes a Profile Admin — a demotion, which
            // is the safe direction; a Portfolio Manager becomes a Team Manager,
            // which is the role it was carved out of.
            migrationBuilder.Sql("""
                UPDATE public."Users"                  SET "UserGroupId" = 1 WHERE "UserGroupId" = 3;
                UPDATE public."AssignUserRights"       SET "UserGroupId" = 1 WHERE "UserGroupId" = 3;
                UPDATE public."EranCoachConversations" SET "UserGroupId" = 1 WHERE "UserGroupId" = 3;

                UPDATE public."Users"                  SET "UserGroupId" = 2 WHERE "UserGroupId" = 4;
                UPDATE public."AssignUserRights"       SET "UserGroupId" = 2 WHERE "UserGroupId" = 4;
                UPDATE public."EranCoachConversations" SET "UserGroupId" = 2 WHERE "UserGroupId" = 4;

                DELETE FROM public."UserGroups" WHERE "Id" IN (3, 4);

                UPDATE public."UserGroups"
                   SET "Name" = 'Manager',
                       "Description" = 'Runs the teams assigned to them, inside one profile.'
                 WHERE "Id" = 2;
                """);
        }
    }
}
