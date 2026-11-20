using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CloverBackoffice.Repository.Migrations
{
    /// <inheritdoc />
    public partial class DropEngineTeamRotation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // D34, as the client confirmed: every team is asked on the 90-question rotation, so the
            // per-team choice added in 20261020102702 goes. Only that flag is dropped; answers and
            // results are untouched.
            migrationBuilder.DropColumn(
                name: "Rotation",
                table: "EngineTeamSettings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Rotation",
                table: "EngineTeamSettings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "production");
        }
    }
}
