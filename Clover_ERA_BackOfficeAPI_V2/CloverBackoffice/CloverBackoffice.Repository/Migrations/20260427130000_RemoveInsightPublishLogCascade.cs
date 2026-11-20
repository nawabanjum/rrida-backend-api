using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CloverBackoffice.Repository.Migrations
{
    /// <inheritdoc />
    public partial class RemoveInsightPublishLogCascade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InsightPublishLogs_LLMReports_ReportId",
                table: "InsightPublishLogs");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddForeignKey(
                name: "FK_InsightPublishLogs_LLMReports_ReportId",
                table: "InsightPublishLogs",
                column: "ReportId",
                principalTable: "LLMReports",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
