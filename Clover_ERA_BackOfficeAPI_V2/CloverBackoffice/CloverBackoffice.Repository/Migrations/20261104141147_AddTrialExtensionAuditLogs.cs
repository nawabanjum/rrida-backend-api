using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CloverBackoffice.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddTrialExtensionAuditLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TrialExtensionAuditLogs",
                schema: "public",
                columns: table => new
                {
                    AuditId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProfileId = table.Column<long>(type: "bigint", nullable: false),
                    SubscriptionId = table.Column<int>(type: "integer", nullable: true),
                    UnitId = table.Column<long>(type: "bigint", nullable: true),
                    Scope = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DaysAdded = table.Column<int>(type: "integer", nullable: true),
                    PreviousTrialEndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NewTrialEndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PreviousGracePeriod = table.Column<int>(type: "integer", nullable: true),
                    NewGracePeriod = table.Column<int>(type: "integer", nullable: true),
                    LicensesUpdated = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ExtendedByUserId = table.Column<int>(type: "integer", nullable: false),
                    LoggedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrialExtensionAuditLogs", x => x.AuditId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrialExtensionAuditLogs",
                schema: "public");
        }
    }
}
