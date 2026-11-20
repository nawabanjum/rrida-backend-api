using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CloverBackoffice.Repository.Migrations
{
    /// <inheritdoc />
    public partial class ReshapeCustomQuestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CustomQuestions_Status_Window",
                schema: "public",
                table: "CustomQuestions");

            migrationBuilder.DropIndex(
                name: "IX_CustomQuestions_UsageContext",
                schema: "public",
                table: "CustomQuestions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CustomQuestions_DailyConfig",
                schema: "public",
                table: "CustomQuestions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CustomQuestions_TeamConfig",
                schema: "public",
                table: "CustomQuestions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CustomQuestions_Window",
                schema: "public",
                table: "CustomQuestions");

            migrationBuilder.DropColumn(
                name: "CloverCategory",
                schema: "public",
                table: "CustomQuestions");

            migrationBuilder.DropColumn(
                name: "EffectiveFromUtc",
                schema: "public",
                table: "CustomQuestions");

            migrationBuilder.DropColumn(
                name: "Ordinal",
                schema: "public",
                table: "CustomQuestions");

            migrationBuilder.DropColumn(
                name: "SentimentType",
                schema: "public",
                table: "CustomQuestions");

            migrationBuilder.DropColumn(
                name: "SubCategory",
                schema: "public",
                table: "CustomQuestions");

            migrationBuilder.DropColumn(
                name: "TeamElement",
                schema: "public",
                table: "CustomQuestions");

            migrationBuilder.DropColumn(
                name: "UsageContext",
                schema: "public",
                table: "CustomQuestions");

            migrationBuilder.DropColumn(
                name: "Variant",
                schema: "public",
                table: "CustomQuestions");

            migrationBuilder.DropColumn(
                name: "CloverCategory",
                schema: "public",
                table: "CustomQuestionAudits");

            migrationBuilder.DropColumn(
                name: "EffectiveFromUtc",
                schema: "public",
                table: "CustomQuestionAudits");

            migrationBuilder.DropColumn(
                name: "Ordinal",
                schema: "public",
                table: "CustomQuestionAudits");

            migrationBuilder.DropColumn(
                name: "SentimentType",
                schema: "public",
                table: "CustomQuestionAudits");

            migrationBuilder.DropColumn(
                name: "SubCategory",
                schema: "public",
                table: "CustomQuestionAudits");

            migrationBuilder.DropColumn(
                name: "TeamElement",
                schema: "public",
                table: "CustomQuestionAudits");

            migrationBuilder.DropColumn(
                name: "UsageContext",
                schema: "public",
                table: "CustomQuestionAudits");

            migrationBuilder.DropColumn(
                name: "Variant",
                schema: "public",
                table: "CustomQuestionAudits");

            migrationBuilder.RenameColumn(
                name: "EffectiveToUtc",
                schema: "public",
                table: "CustomQuestions",
                newName: "ScheduledAt");

            migrationBuilder.RenameColumn(
                name: "EffectiveToUtc",
                schema: "public",
                table: "CustomQuestionAudits",
                newName: "ScheduledAt");

            migrationBuilder.AddColumn<long>(
                name: "CompanyId",
                schema: "public",
                table: "CustomQuestions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "UnitId",
                schema: "public",
                table: "CustomQuestions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "CompanyId",
                schema: "public",
                table: "CustomQuestionAudits",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "UnitId",
                schema: "public",
                table: "CustomQuestionAudits",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "CustomQuestionDailyFeedback",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CustomQuestionId = table.Column<long>(type: "bigint", nullable: false),
                    FreeText = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CreatedBy = table.Column<int>(type: "integer", nullable: true),
                    UpdatedBy = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomQuestionDailyFeedback", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomQuestionDailyFeedback_CustomQuestions_CustomQuestionId",
                        column: x => x.CustomQuestionId,
                        principalSchema: "public",
                        principalTable: "CustomQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomQuestionDailyFeedbackAudits",
                schema: "public",
                columns: table => new
                {
                    AuditId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CustomQuestionDailyFeedbackId = table.Column<long>(type: "bigint", nullable: false),
                    CustomQuestionId = table.Column<long>(type: "bigint", nullable: false),
                    FreeText = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ChangeSummary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedBy = table.Column<int>(type: "integer", nullable: true),
                    UpdatedBy = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LoggedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    OperationType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomQuestionDailyFeedbackAudits", x => x.AuditId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomQuestions_Company_Unit",
                schema: "public",
                table: "CustomQuestions",
                columns: new[] { "CompanyId", "UnitId" },
                filter: "\"IsDel\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_CustomQuestions_Status_ScheduledAt",
                schema: "public",
                table: "CustomQuestions",
                columns: new[] { "Status", "ScheduledAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomQuestions_UnitId",
                schema: "public",
                table: "CustomQuestions",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomQuestionDailyFeedback_CreatedAt",
                schema: "public",
                table: "CustomQuestionDailyFeedback",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_CustomQuestionDailyFeedback_Question",
                schema: "public",
                table: "CustomQuestionDailyFeedback",
                column: "CustomQuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomQuestionDailyFeedbackAudits_Feedback_Version",
                schema: "public",
                table: "CustomQuestionDailyFeedbackAudits",
                columns: new[] { "CustomQuestionDailyFeedbackId", "VersionNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomQuestionDailyFeedbackAudits_Operation",
                schema: "public",
                table: "CustomQuestionDailyFeedbackAudits",
                column: "OperationType");

            migrationBuilder.AddForeignKey(
                name: "FK_CustomQuestions_BusinessUnits_UnitId",
                schema: "public",
                table: "CustomQuestions",
                column: "UnitId",
                principalTable: "BusinessUnits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CustomQuestions_Companies_CompanyId",
                schema: "public",
                table: "CustomQuestions",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CustomQuestions_BusinessUnits_UnitId",
                schema: "public",
                table: "CustomQuestions");

            migrationBuilder.DropForeignKey(
                name: "FK_CustomQuestions_Companies_CompanyId",
                schema: "public",
                table: "CustomQuestions");

            migrationBuilder.DropTable(
                name: "CustomQuestionDailyFeedback",
                schema: "public");

            migrationBuilder.DropTable(
                name: "CustomQuestionDailyFeedbackAudits",
                schema: "public");

            migrationBuilder.DropIndex(
                name: "IX_CustomQuestions_Company_Unit",
                schema: "public",
                table: "CustomQuestions");

            migrationBuilder.DropIndex(
                name: "IX_CustomQuestions_Status_ScheduledAt",
                schema: "public",
                table: "CustomQuestions");

            migrationBuilder.DropIndex(
                name: "IX_CustomQuestions_UnitId",
                schema: "public",
                table: "CustomQuestions");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                schema: "public",
                table: "CustomQuestions");

            migrationBuilder.DropColumn(
                name: "UnitId",
                schema: "public",
                table: "CustomQuestions");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                schema: "public",
                table: "CustomQuestionAudits");

            migrationBuilder.DropColumn(
                name: "UnitId",
                schema: "public",
                table: "CustomQuestionAudits");

            migrationBuilder.RenameColumn(
                name: "ScheduledAt",
                schema: "public",
                table: "CustomQuestions",
                newName: "EffectiveToUtc");

            migrationBuilder.RenameColumn(
                name: "ScheduledAt",
                schema: "public",
                table: "CustomQuestionAudits",
                newName: "EffectiveToUtc");

            migrationBuilder.AddColumn<int>(
                name: "CloverCategory",
                schema: "public",
                table: "CustomQuestions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EffectiveFromUtc",
                schema: "public",
                table: "CustomQuestions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Ordinal",
                schema: "public",
                table: "CustomQuestions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SentimentType",
                schema: "public",
                table: "CustomQuestions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SubCategory",
                schema: "public",
                table: "CustomQuestions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TeamElement",
                schema: "public",
                table: "CustomQuestions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UsageContext",
                schema: "public",
                table: "CustomQuestions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Variant",
                schema: "public",
                table: "CustomQuestions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CloverCategory",
                schema: "public",
                table: "CustomQuestionAudits",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EffectiveFromUtc",
                schema: "public",
                table: "CustomQuestionAudits",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Ordinal",
                schema: "public",
                table: "CustomQuestionAudits",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SentimentType",
                schema: "public",
                table: "CustomQuestionAudits",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SubCategory",
                schema: "public",
                table: "CustomQuestionAudits",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TeamElement",
                schema: "public",
                table: "CustomQuestionAudits",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UsageContext",
                schema: "public",
                table: "CustomQuestionAudits",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Variant",
                schema: "public",
                table: "CustomQuestionAudits",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomQuestions_Status_Window",
                schema: "public",
                table: "CustomQuestions",
                columns: new[] { "Status", "EffectiveFromUtc", "EffectiveToUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomQuestions_UsageContext",
                schema: "public",
                table: "CustomQuestions",
                column: "UsageContext",
                filter: "\"IsDel\" = false");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CustomQuestions_DailyConfig",
                schema: "public",
                table: "CustomQuestions",
                sql: "(\"UsageContext\" IN (1,3) AND \"CloverCategory\" IS NOT NULL) OR (\"UsageContext\" = 2 AND \"CloverCategory\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CustomQuestions_TeamConfig",
                schema: "public",
                table: "CustomQuestions",
                sql: "(\"UsageContext\" IN (2,3) AND \"TeamElement\" IS NOT NULL AND \"SubCategory\" IS NOT NULL AND \"Variant\" IS NOT NULL) OR (\"UsageContext\" = 1 AND \"TeamElement\" IS NULL AND \"SubCategory\" IS NULL AND \"Variant\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CustomQuestions_Window",
                schema: "public",
                table: "CustomQuestions",
                sql: "\"EffectiveFromUtc\" IS NULL OR \"EffectiveToUtc\" IS NULL OR \"EffectiveToUtc\" >= \"EffectiveFromUtc\"");
        }
    }
}
