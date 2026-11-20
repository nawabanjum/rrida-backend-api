using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CloverBackoffice.Repository.Migrations
{
    /// <summary>
    /// Daily check-in: multi-select reasons, free-text-only answers and optional team
    /// feedback (mobile submit-daily-feedback / submit-team-feedback).
    ///
    ///   * DailyFeedbackReasons / TeamDailyFeedbackReasons hold every selected reason
    ///     of an answer. The legacy single-reason columns keep the first one, so
    ///     existing readers are unaffected. Existing answers are backfilled with their
    ///     one reason, so the reason sets are complete from day one.
    ///   * DailyFeedback.DailyReasonId becomes nullable: a member may answer with their
    ///     own free text and no predefined reason. The foreign key keeps ON DELETE
    ///     CASCADE.
    ///   * TeamFeedback stores the anonymous "Any other feedback about your team?" text.
    ///
    /// Mirrors Scripts/Mobile/02_DailyCheckInMultiReasonsAndTeamFeedback.idempotent.sql.
    /// </summary>
    public partial class DailyCheckInMultiReasonsAndTeamFeedback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<long>(
                name: "DailyReasonId",
                schema: "public",
                table: "DailyFeedback",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.CreateTable(
                name: "DailyFeedbackReasons",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DailyFeedbackId = table.Column<long>(type: "bigint", nullable: false),
                    DailyReasonId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyFeedbackReasons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailyFeedbackReasons_DailyFeedback_DailyFeedbackId",
                        column: x => x.DailyFeedbackId,
                        principalSchema: "public",
                        principalTable: "DailyFeedback",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DailyFeedbackReasons_DailyReasons_DailyReasonId",
                        column: x => x.DailyReasonId,
                        principalTable: "DailyReasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeamDailyFeedbackReasons",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TeamDailyFeedbackId = table.Column<long>(type: "bigint", nullable: false),
                    TeamDailyReasonId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamDailyFeedbackReasons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamDailyFeedbackReasons_TeamDailyFeedback_TeamDailyFeedbac~",
                        column: x => x.TeamDailyFeedbackId,
                        principalSchema: "public",
                        principalTable: "TeamDailyFeedback",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamDailyFeedbackReasons_TeamDailyReasons_TeamDailyReasonId",
                        column: x => x.TeamDailyReasonId,
                        principalTable: "TeamDailyReasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeamFeedback",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BusinessUnitId = table.Column<long>(type: "bigint", nullable: false),
                    FeedbackText = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ClientSubmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SubmissionDateTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamFeedback", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamFeedback_BusinessUnits_BusinessUnitId",
                        column: x => x.BusinessUnitId,
                        principalTable: "BusinessUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DailyFeedbackReasons_DailyFeedbackId_DailyReasonId",
                schema: "public",
                table: "DailyFeedbackReasons",
                columns: new[] { "DailyFeedbackId", "DailyReasonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DailyFeedbackReasons_DailyReasonId",
                schema: "public",
                table: "DailyFeedbackReasons",
                column: "DailyReasonId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamDailyFeedbackReasons_TeamDailyFeedbackId_TeamDailyReaso~",
                schema: "public",
                table: "TeamDailyFeedbackReasons",
                columns: new[] { "TeamDailyFeedbackId", "TeamDailyReasonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamDailyFeedbackReasons_TeamDailyReasonId",
                schema: "public",
                table: "TeamDailyFeedbackReasons",
                column: "TeamDailyReasonId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamFeedback_BusinessUnitId_SubmissionDateTime",
                schema: "public",
                table: "TeamFeedback",
                columns: new[] { "BusinessUnitId", "SubmissionDateTime" });

            // Every existing answer has exactly one reason; give it its reason set.
            migrationBuilder.Sql("""
                INSERT INTO public."DailyFeedbackReasons" ("DailyFeedbackId", "DailyReasonId")
                SELECT df."Id", df."DailyReasonId"
                FROM public."DailyFeedback" df
                WHERE df."DailyReasonId" IS NOT NULL
                ON CONFLICT ("DailyFeedbackId", "DailyReasonId") DO NOTHING;

                INSERT INTO public."TeamDailyFeedbackReasons" ("TeamDailyFeedbackId", "TeamDailyReasonId")
                SELECT tf."Id", tf."TeamDailyReasonId"
                FROM public."TeamDailyFeedback" tf
                ON CONFLICT ("TeamDailyFeedbackId", "TeamDailyReasonId") DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DailyFeedbackReasons",
                schema: "public");

            migrationBuilder.DropTable(
                name: "TeamDailyFeedbackReasons",
                schema: "public");

            migrationBuilder.DropTable(
                name: "TeamFeedback",
                schema: "public");

            migrationBuilder.AlterColumn<long>(
                name: "DailyReasonId",
                schema: "public",
                table: "DailyFeedback",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);
        }
    }
}
