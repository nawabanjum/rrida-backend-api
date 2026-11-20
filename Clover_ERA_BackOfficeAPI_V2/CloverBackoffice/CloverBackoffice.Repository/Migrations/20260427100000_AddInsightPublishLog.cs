using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CloverBackoffice.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddInsightPublishLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InsightPublishLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReportId = table.Column<long>(type: "bigint", nullable: false),
                    RunId = table.Column<int>(type: "integer", nullable: false),
                    TopThemes = table.Column<string>(type: "text", nullable: false, defaultValue: "[]"),
                    TopActions = table.Column<string>(type: "text", nullable: false, defaultValue: "[]"),
                    TopRecommendations = table.Column<string>(type: "text", nullable: false, defaultValue: "[]"),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InsightPublishLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InsightPublishLogs_LLMReports_ReportId",
                        column: x => x.ReportId,
                        principalTable: "LLMReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InsightPublishLogs_ReportId",
                table: "InsightPublishLogs",
                column: "ReportId");

            migrationBuilder.CreateIndex(
                name: "IX_InsightPublishLogs_RunId",
                table: "InsightPublishLogs",
                column: "RunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "InsightPublishLogs");
        }
    }
}
