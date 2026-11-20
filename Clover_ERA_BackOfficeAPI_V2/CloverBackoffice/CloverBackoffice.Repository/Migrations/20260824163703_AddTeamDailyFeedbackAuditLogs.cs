using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CloverBackoffice.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamDailyFeedbackAuditLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TeamDailyFeedbackAuditLogs",
                schema: "public",
                columns: table => new
                {
                    AuditId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    SubmissionDateTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BusinessUnitId = table.Column<long>(type: "bigint", nullable: false),
                    SentimentType = table.Column<int>(type: "integer", nullable: false),
                    TeamDailyQuestionId = table.Column<long>(type: "bigint", nullable: false),
                    TeamDailyReasonId = table.Column<long>(type: "bigint", nullable: false),
                    FreeText = table.Column<string>(type: "character varying(896)", maxLength: 896, nullable: true),
                    LoggedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    OperationType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RecordCountSnapshot = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamDailyFeedbackAuditLogs", x => x.AuditId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TeamDailyFeedbackAuditLogs_Id_Version",
                schema: "public",
                table: "TeamDailyFeedbackAuditLogs",
                columns: new[] { "Id", "VersionNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_TeamDailyFeedbackAuditLogs_Operation",
                schema: "public",
                table: "TeamDailyFeedbackAuditLogs",
                column: "OperationType");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TeamDailyFeedbackAuditLogs",
                schema: "public");
        }
    }
}
