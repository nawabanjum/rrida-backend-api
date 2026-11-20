using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CloverBackoffice.Repository.Migrations
{
    /// <summary>
    /// Adds UserTeams.AssignmentSeenAt: when the manager acknowledged the team
    /// assignment. Null = unseen, which login reports as a new team assignment
    /// (HasNewTeamAssignment / ManagedTeams[].IsNewAssignment) so the client can
    /// show the "you have been assigned a new team" popup.
    ///
    /// Every existing row is backfilled as seen - they are the teams managers
    /// already have. Written by hand like the other migrations here. Mirrors
    /// Scripts/UserGroups/08_AddAssignmentSeenAtToUserTeams.idempotent.sql.
    /// </summary>
    public partial class AddAssignmentSeenAtToUserTeams : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_schema = 'public' AND table_name = 'UserTeams'
                          AND column_name = 'AssignmentSeenAt'
                    ) THEN
                        ALTER TABLE public."UserTeams" ADD COLUMN "AssignmentSeenAt" timestamp with time zone NULL;
                        UPDATE public."UserTeams" SET "AssignmentSeenAt" = now();
                    END IF;
                END $$;

                CREATE INDEX IF NOT EXISTS "IX_UserTeams_Unseen_UserId"
                    ON public."UserTeams" ("UserId")
                    WHERE "AssignmentSeenAt" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS public."IX_UserTeams_Unseen_UserId";
                ALTER TABLE public."UserTeams" DROP COLUMN IF EXISTS "AssignmentSeenAt";
                """);
        }
    }
}
