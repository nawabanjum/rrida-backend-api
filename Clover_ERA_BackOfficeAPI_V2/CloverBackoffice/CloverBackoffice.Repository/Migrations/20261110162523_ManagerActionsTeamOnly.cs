using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CloverBackoffice.Repository.Migrations
{
    /// <summary>
    /// Actions are team-based only: drops the person (SubjectUserId) and owner (OwnerUserId)
    /// columns and their indexes, and makes UnitId required. Safe because nothing ever wrote
    /// a person-only row (the table was empty on Dev when this was written).
    /// </summary>
    public partial class ManagerActionsTeamOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ManagerActions_Profile_Owner",
                schema: "public",
                table: "ManagerActions");

            migrationBuilder.DropIndex(
                name: "IX_ManagerActions_Subject",
                schema: "public",
                table: "ManagerActions");

            migrationBuilder.DropColumn(
                name: "OwnerUserId",
                schema: "public",
                table: "ManagerActions");

            migrationBuilder.DropColumn(
                name: "SubjectUserId",
                schema: "public",
                table: "ManagerActions");

            migrationBuilder.AlterColumn<long>(
                name: "UnitId",
                schema: "public",
                table: "ManagerActions",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ManagerActions_Profile_CreatedBy",
                schema: "public",
                table: "ManagerActions",
                columns: new[] { "ProfileId", "CreatedBy" },
                filter: "\"IsDel\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ManagerActions_Profile_CreatedBy",
                schema: "public",
                table: "ManagerActions");

            migrationBuilder.AlterColumn<long>(
                name: "UnitId",
                schema: "public",
                table: "ManagerActions",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<int>(
                name: "OwnerUserId",
                schema: "public",
                table: "ManagerActions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SubjectUserId",
                schema: "public",
                table: "ManagerActions",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ManagerActions_Profile_Owner",
                schema: "public",
                table: "ManagerActions",
                columns: new[] { "ProfileId", "OwnerUserId" },
                filter: "\"IsDel\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_ManagerActions_Subject",
                schema: "public",
                table: "ManagerActions",
                column: "SubjectUserId",
                filter: "\"IsDel\" = false AND \"SubjectUserId\" IS NOT NULL");
        }
    }
}
