using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CloverBackoffice.Repository.Migrations
{
    /// <summary>
    /// Adds UserTeams.IsPendingActivation: a Team Manager replacement that waits
    /// for the invited manager to accept their invitation.
    ///
    /// Add User used to replace a team's manager the moment the admin confirmed,
    /// so a team could sit for an undefined time under a manager whose account
    /// was still Pending. The replacement row is now written unassigned with this
    /// flag set, the outgoing manager stays active, and accepting the invitation
    /// finalizes the swap.
    ///
    /// NOT NULL DEFAULT false: no existing row is a pending replacement. Written
    /// by hand like the other migrations here. Mirrors
    /// Scripts/UserGroups/07_AddIsPendingActivationToUserTeams.idempotent.sql.
    /// </summary>
    public partial class AddIsPendingActivationToUserTeams : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE public."UserTeams"
                    ADD COLUMN IF NOT EXISTS "IsPendingActivation" boolean NOT NULL DEFAULT false;

                CREATE INDEX IF NOT EXISTS "IX_UserTeams_Pending_UserId"
                    ON public."UserTeams" ("UserId")
                    WHERE "IsPendingActivation" = true;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS public."IX_UserTeams_Pending_UserId";
                ALTER TABLE public."UserTeams" DROP COLUMN IF EXISTS "IsPendingActivation";
                """);
        }
    }
}
