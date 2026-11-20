using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CloverBackoffice.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamSizeToBusinessUnit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Nullable so existing teams, created before the field existed, stay valid.
            // The API requires a value on create and treats it as optional on edit.
            migrationBuilder.AddColumn<int>(
                name: "TeamSize",
                table: "BusinessUnits",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TeamSize",
                table: "BusinessUnits");
        }
    }
}
