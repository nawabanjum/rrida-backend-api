using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CloverBackoffice.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddUnitIdToCustomQuestionDailyFeedback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "UnitId",
                schema: "public",
                table: "CustomQuestionDailyFeedbackAudits",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "UnitId",
                schema: "public",
                table: "CustomQuestionDailyFeedback",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            // Backfill before the foreign key goes on, or the rows already in the table land
            // on unit 0 and the key rejects them. The owning question is the authority: an
            // answer belongs to the unit its question belongs to, which is exactly the rule
            // the repository enforces on every new answer.
            migrationBuilder.Sql(@"
                UPDATE public.""CustomQuestionDailyFeedback"" f
                SET ""UnitId"" = q.""UnitId""
                FROM public.""CustomQuestions"" q
                WHERE q.""Id"" = f.""CustomQuestionId""
                  AND f.""UnitId"" = 0;");

            // The audit snapshots get the same treatment. No foreign key here — an audit row
            // outlives what it describes — but a snapshot reading unit 0 would be a lie.
            migrationBuilder.Sql(@"
                UPDATE public.""CustomQuestionDailyFeedbackAudits"" a
                SET ""UnitId"" = q.""UnitId""
                FROM public.""CustomQuestions"" q
                WHERE q.""Id"" = a.""CustomQuestionId""
                  AND a.""UnitId"" = 0;");

            migrationBuilder.CreateIndex(
                name: "IX_CustomQuestionDailyFeedback_Unit_CreatedAt",
                schema: "public",
                table: "CustomQuestionDailyFeedback",
                columns: new[] { "UnitId", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_CustomQuestionDailyFeedback_BusinessUnits_UnitId",
                schema: "public",
                table: "CustomQuestionDailyFeedback",
                column: "UnitId",
                principalTable: "BusinessUnits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CustomQuestionDailyFeedback_BusinessUnits_UnitId",
                schema: "public",
                table: "CustomQuestionDailyFeedback");

            migrationBuilder.DropIndex(
                name: "IX_CustomQuestionDailyFeedback_Unit_CreatedAt",
                schema: "public",
                table: "CustomQuestionDailyFeedback");

            migrationBuilder.DropColumn(
                name: "UnitId",
                schema: "public",
                table: "CustomQuestionDailyFeedbackAudits");

            migrationBuilder.DropColumn(
                name: "UnitId",
                schema: "public",
                table: "CustomQuestionDailyFeedback");
        }
    }
}
