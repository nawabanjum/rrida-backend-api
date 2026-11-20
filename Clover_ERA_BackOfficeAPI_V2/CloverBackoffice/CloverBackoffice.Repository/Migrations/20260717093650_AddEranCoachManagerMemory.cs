using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CloverBackoffice.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddEranCoachManagerMemory : Migration
    {
        // Scoped deliberately to ONLY the EranCoach memory table. EF also detected unrelated
        // pre-existing model drift (Payments/PaymentAuditLogs columns, ComplimentaryLicenseGrants,
        // DailyReflectionLinesDtos, FeedBack, WorkAllocationResultDtos) that was never migrated by
        // the team; that is intentionally excluded here so this migration touches nothing but its
        // own table. The model snapshot still reflects those entities, so they are not re-detected.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EranCoachManagerMemories",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ManagerUserId = table.Column<int>(type: "integer", nullable: false),
                    SummaryText = table.Column<string>(type: "text", nullable: true),
                    TurnCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDel = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EranCoachManagerMemories", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EranCoachManagerMemories",
                schema: "public");
        }
    }
}
