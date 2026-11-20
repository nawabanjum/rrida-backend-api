using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CloverBackoffice.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddEngineRotationAndGuard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Rotation",
                table: "EngineTeamSettings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "production");

            // D34. A team that already has answers was asked the demo's six questions, and every
            // stored chip belongs to the question its day asked: on the ninety the engine refuses
            // that history. Such a team stays on the six until its history is reset.
            migrationBuilder.Sql("""
                UPDATE public."EngineTeamSettings" s
                SET "Rotation" = 'demo'
                WHERE EXISTS (SELECT 1 FROM public."EngineAnswers" a WHERE a."BusinessUnitId" = s."BusinessUnitId");
                """);

            migrationBuilder.AddColumn<string>(
                name: "GuardOutcome",
                table: "EngineLlmCalls",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GuardRules",
                table: "EngineLlmCalls",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GuardOutcome",
                table: "EngineFeedItems",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Rotation",
                table: "EngineTeamSettings");

            migrationBuilder.DropColumn(
                name: "GuardOutcome",
                table: "EngineLlmCalls");

            migrationBuilder.DropColumn(
                name: "GuardRules",
                table: "EngineLlmCalls");

            migrationBuilder.DropColumn(
                name: "GuardOutcome",
                table: "EngineFeedItems");
        }
    }
}
