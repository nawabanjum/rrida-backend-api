using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CloverBackoffice.Repository.Migrations
{
    /// <summary>
    /// Adds UserTeams.IsNewTeam: was this assignment's team created by the very
    /// request that made the assignment?
    ///
    /// It exists because <c>post-password-landing</c> has to report it. That
    /// endpoint runs when the invited manager sets their password — days after
    /// <c>create-manager</c> ran, off nothing but the encrypted invitation link —
    /// and the answer was previously held only in create-manager's response body.
    /// Comparing the unit's creation time to the assignment's is not trustworthy
    /// enough to infer it, so it is recorded at the moment it is known.
    ///
    /// NOT NULL DEFAULT false, which is the truthful value for every existing row:
    /// they were all made against a team that already existed, either through
    /// assign-user-to-team or through an earlier manager onboarding whose team was
    /// picked from the list. Only manager onboarding ever sets it true.
    ///
    /// Written by hand rather than scaffolded, like the other migrations here, so
    /// nothing unrelated is swept in — see the note in
    /// 20260908090000_CollapseUserGroupsToTwoRoles. Mirrors
    /// Scripts/UserGroups/04_AddIsNewTeamToUserTeams.idempotent.sql.
    /// </summary>
    public partial class AddIsNewTeamToUserTeams : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE public."UserTeams"
                    ADD COLUMN IF NOT EXISTS "IsNewTeam" boolean NOT NULL DEFAULT false;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE public."UserTeams" DROP COLUMN IF EXISTS "IsNewTeam";
                """);
        }
    }
}
