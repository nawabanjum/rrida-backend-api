using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CloverBackoffice.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddSystemDefaultSettings : Migration
    {
        // Migrations folder was empty when this migration was created, so EF generated
        // operations for every existing entity. The schema for those tables already
        // exists in the database (created out-of-band). Up()/Down() are trimmed to only
        // the new SystemDefaultSettings tables to avoid colliding with existing schema.
        // The Designer snapshot still records the full model — that's expected and harmless.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SystemDefaultSettings",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    UnitId = table.Column<long>(type: "bigint", nullable: false),
                    SubscriptionStartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SubscriptionEndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    GracePeriodDays = table.Column<int>(type: "integer", nullable: false),
                    IsScheduleEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    ReportGenerationDays = table.Column<int>(type: "integer", nullable: false),
                    IsConfigurable = table.Column<bool>(type: "boolean", nullable: false),
                    ConfigurableValue = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsDel = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: true),
                    UpdatedBy = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemDefaultSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SystemDefaultSettings_BusinessUnits_UnitId",
                        column: x => x.UnitId,
                        principalTable: "BusinessUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SystemDefaultSettings_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SystemDefaultSettingsAudit",
                schema: "public",
                columns: table => new
                {
                    AuditId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Id = table.Column<int>(type: "integer", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    UnitId = table.Column<long>(type: "bigint", nullable: false),
                    SubscriptionStartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SubscriptionEndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    GracePeriodDays = table.Column<int>(type: "integer", nullable: false),
                    IsScheduleEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    ReportGenerationDays = table.Column<int>(type: "integer", nullable: false),
                    IsConfigurable = table.Column<bool>(type: "boolean", nullable: false),
                    ConfigurableValue = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsDel = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: true),
                    UpdatedBy = table.Column<int>(type: "integer", nullable: true),
                    LoggedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    OperationType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemDefaultSettingsAudit", x => x.AuditId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SystemDefaultSettings_CompanyId_UnitId",
                schema: "public",
                table: "SystemDefaultSettings",
                columns: new[] { "CompanyId", "UnitId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SystemDefaultSettings_UnitId",
                schema: "public",
                table: "SystemDefaultSettings",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_SystemDefaultSettingsAudit_Id",
                schema: "public",
                table: "SystemDefaultSettingsAudit",
                column: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SystemDefaultSettings",
                schema: "public");

            migrationBuilder.DropTable(
                name: "SystemDefaultSettingsAudit",
                schema: "public");
        }
    }
}
