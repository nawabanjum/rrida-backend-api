using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CloverBackoffice.Repository.Migrations
{
    /// <summary>
    /// Drops BusinessUnits.IsCloveraPersonalApp.
    ///
    /// The Clovera Personal App sign-up route has been removed: the mobile
    /// registration endpoint no longer accepts a "personal" flag, and nothing reads
    /// this column any more. The three unit-list filters and the scheduled-report
    /// filter that excluded flagged units are gone with it, as is the
    /// CloveraPersonalApp:PersonalProfileId setting that briefly replaced the
    /// equivalent column on Companies.
    ///
    /// No data is lost: zero rows had the flag set when this was written.
    ///
    /// Guarded rather than a bare DropColumn so it matches the other two Profile
    /// migrations and can be re-run. Identical to
    /// Scripts/Profiles/03_RemoveIsCloveraPersonalApp.idempotent.sql.
    /// </summary>
    public partial class RemoveIsCloveraPersonalApp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE public."BusinessUnits" DROP COLUMN IF EXISTS "IsCloveraPersonalApp";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Comes back as it was: NOT NULL, defaulting to false. Every row gets
            // false, which is what every row held.
            migrationBuilder.Sql("""
                ALTER TABLE public."BusinessUnits"
                    ADD COLUMN IF NOT EXISTS "IsCloveraPersonalApp" boolean NOT NULL DEFAULT false;
                """);
        }
    }
}
