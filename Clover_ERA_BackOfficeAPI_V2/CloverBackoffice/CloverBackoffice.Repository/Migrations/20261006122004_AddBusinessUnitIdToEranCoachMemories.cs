using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CloverBackoffice.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessUnitIdToEranCoachMemories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "BusinessUnitId",
                schema: "public",
                table: "EranCoachManagerMemories",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_EranCoachManagerMemories_ManagerUserId_BusinessUnitId",
                schema: "public",
                table: "EranCoachManagerMemories",
                columns: new[] { "ManagerUserId", "BusinessUnitId" });

            migrationBuilder.CreateIndex(
                name: "IX_EranCoachConversations_UserId_BusinessUnitId",
                schema: "public",
                table: "EranCoachConversations",
                columns: new[] { "UserId", "BusinessUnitId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EranCoachManagerMemories_ManagerUserId_BusinessUnitId",
                schema: "public",
                table: "EranCoachManagerMemories");

            migrationBuilder.DropIndex(
                name: "IX_EranCoachConversations_UserId_BusinessUnitId",
                schema: "public",
                table: "EranCoachConversations");

            migrationBuilder.DropColumn(
                name: "BusinessUnitId",
                schema: "public",
                table: "EranCoachManagerMemories");
        }
    }
}
