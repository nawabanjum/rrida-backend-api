using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CloverBackoffice.Repository.Migrations
{
    /// <inheritdoc />
    public partial class RenameToLLMReportSchedules : Migration
    {
        // Renames SystemDefaultSettings → LLMReportSchedules, drops unused columns
        // (SubscriptionStartDate, SubscriptionEndDate, GracePeriodDays,
        //  LastReportRunAt, IsConfigurable, ConfigurableValue), renames the audit
        // table SystemDefaultSettingsAudit → LLMReportScheduleAudits, and renames
        // the audit's `Id` column to `ScheduleId` so it reads naturally as an FK
        // to the schedule row it describes.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── 1. Drop indexes that reference the old table names ───────────
            migrationBuilder.DropIndex(
                name: "IX_SystemDefaultSettings_CompanyId_UnitId",
                schema: "public",
                table: "SystemDefaultSettings");

            migrationBuilder.DropIndex(
                name: "IX_SystemDefaultSettings_UnitId",
                schema: "public",
                table: "SystemDefaultSettings");

            migrationBuilder.DropIndex(
                name: "IX_SystemDefaultSettingsAudit_Id",
                schema: "public",
                table: "SystemDefaultSettingsAudit");

            // ── 2. Drop unused columns from main table ───────────────────────
            migrationBuilder.DropColumn(name: "SubscriptionStartDate", schema: "public", table: "SystemDefaultSettings");
            migrationBuilder.DropColumn(name: "SubscriptionEndDate",   schema: "public", table: "SystemDefaultSettings");
            migrationBuilder.DropColumn(name: "GracePeriodDays",       schema: "public", table: "SystemDefaultSettings");
            migrationBuilder.DropColumn(name: "LastReportRunAt",       schema: "public", table: "SystemDefaultSettings");
            migrationBuilder.DropColumn(name: "IsConfigurable",        schema: "public", table: "SystemDefaultSettings");
            migrationBuilder.DropColumn(name: "ConfigurableValue",     schema: "public", table: "SystemDefaultSettings");

            // ── 3. Drop matching columns from the audit table ────────────────
            migrationBuilder.DropColumn(name: "SubscriptionStartDate", schema: "public", table: "SystemDefaultSettingsAudit");
            migrationBuilder.DropColumn(name: "SubscriptionEndDate",   schema: "public", table: "SystemDefaultSettingsAudit");
            migrationBuilder.DropColumn(name: "GracePeriodDays",       schema: "public", table: "SystemDefaultSettingsAudit");
            migrationBuilder.DropColumn(name: "LastReportRunAt",       schema: "public", table: "SystemDefaultSettingsAudit");
            migrationBuilder.DropColumn(name: "IsConfigurable",        schema: "public", table: "SystemDefaultSettingsAudit");
            migrationBuilder.DropColumn(name: "ConfigurableValue",     schema: "public", table: "SystemDefaultSettingsAudit");

            // ── 4. Rename the audit's `Id` column to `ScheduleId` ────────────
            migrationBuilder.RenameColumn(
                name: "Id",
                schema: "public",
                table: "SystemDefaultSettingsAudit",
                newName: "ScheduleId");

            // ── 5. Rename tables ─────────────────────────────────────────────
            migrationBuilder.RenameTable(
                name: "SystemDefaultSettings",
                schema: "public",
                newName: "LLMReportSchedules",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "SystemDefaultSettingsAudit",
                schema: "public",
                newName: "LLMReportScheduleAudits",
                newSchema: "public");

            // ── 6. Recreate indexes under the new names ──────────────────────
            migrationBuilder.CreateIndex(
                name: "IX_LLMReportSchedules_CompanyId_UnitId",
                schema: "public",
                table: "LLMReportSchedules",
                columns: new[] { "CompanyId", "UnitId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LLMReportSchedules_UnitId",
                schema: "public",
                table: "LLMReportSchedules",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_LLMReportScheduleAudits_ScheduleId",
                schema: "public",
                table: "LLMReportScheduleAudits",
                column: "ScheduleId");

            // ── 7. Rename PK + FK constraints to match new table names ───────
            // Postgres-friendly raw SQL — keeps the FKs intact across the rename.
            migrationBuilder.Sql(@"
                ALTER TABLE ""public"".""LLMReportSchedules""
                    RENAME CONSTRAINT ""PK_SystemDefaultSettings"" TO ""PK_LLMReportSchedules"";
                ALTER TABLE ""public"".""LLMReportSchedules""
                    RENAME CONSTRAINT ""FK_SystemDefaultSettings_BusinessUnits_UnitId"" TO ""FK_LLMReportSchedules_BusinessUnits_UnitId"";
                ALTER TABLE ""public"".""LLMReportSchedules""
                    RENAME CONSTRAINT ""FK_SystemDefaultSettings_Companies_CompanyId"" TO ""FK_LLMReportSchedules_Companies_CompanyId"";

                ALTER TABLE ""public"".""LLMReportScheduleAudits""
                    RENAME CONSTRAINT ""PK_SystemDefaultSettingsAudit"" TO ""PK_LLMReportScheduleAudits"";
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse the constraint renames first.
            migrationBuilder.Sql(@"
                ALTER TABLE ""public"".""LLMReportScheduleAudits""
                    RENAME CONSTRAINT ""PK_LLMReportScheduleAudits"" TO ""PK_SystemDefaultSettingsAudit"";

                ALTER TABLE ""public"".""LLMReportSchedules""
                    RENAME CONSTRAINT ""FK_LLMReportSchedules_Companies_CompanyId"" TO ""FK_SystemDefaultSettings_Companies_CompanyId"";
                ALTER TABLE ""public"".""LLMReportSchedules""
                    RENAME CONSTRAINT ""FK_LLMReportSchedules_BusinessUnits_UnitId"" TO ""FK_SystemDefaultSettings_BusinessUnits_UnitId"";
                ALTER TABLE ""public"".""LLMReportSchedules""
                    RENAME CONSTRAINT ""PK_LLMReportSchedules"" TO ""PK_SystemDefaultSettings"";
            ");

            migrationBuilder.DropIndex(
                name: "IX_LLMReportScheduleAudits_ScheduleId",
                schema: "public",
                table: "LLMReportScheduleAudits");

            migrationBuilder.DropIndex(
                name: "IX_LLMReportSchedules_UnitId",
                schema: "public",
                table: "LLMReportSchedules");

            migrationBuilder.DropIndex(
                name: "IX_LLMReportSchedules_CompanyId_UnitId",
                schema: "public",
                table: "LLMReportSchedules");

            migrationBuilder.RenameTable(
                name: "LLMReportScheduleAudits",
                schema: "public",
                newName: "SystemDefaultSettingsAudit",
                newSchema: "public");

            migrationBuilder.RenameTable(
                name: "LLMReportSchedules",
                schema: "public",
                newName: "SystemDefaultSettings",
                newSchema: "public");

            migrationBuilder.RenameColumn(
                name: "ScheduleId",
                schema: "public",
                table: "SystemDefaultSettingsAudit",
                newName: "Id");

            migrationBuilder.AddColumn<string>(name: "ConfigurableValue",     schema: "public", table: "SystemDefaultSettingsAudit", type: "character varying(500)", maxLength: 500, nullable: true);
            migrationBuilder.AddColumn<bool>  (name: "IsConfigurable",        schema: "public", table: "SystemDefaultSettingsAudit", type: "boolean", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<DateTime>(name: "LastReportRunAt",     schema: "public", table: "SystemDefaultSettingsAudit", type: "timestamp with time zone", nullable: true);
            migrationBuilder.AddColumn<int>   (name: "GracePeriodDays",       schema: "public", table: "SystemDefaultSettingsAudit", type: "integer", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<DateTime>(name: "SubscriptionEndDate", schema: "public", table: "SystemDefaultSettingsAudit", type: "timestamp with time zone", nullable: true);
            migrationBuilder.AddColumn<DateTime>(name: "SubscriptionStartDate", schema: "public", table: "SystemDefaultSettingsAudit", type: "timestamp with time zone", nullable: true);

            migrationBuilder.AddColumn<string>(name: "ConfigurableValue",     schema: "public", table: "SystemDefaultSettings", type: "character varying(500)", maxLength: 500, nullable: true);
            migrationBuilder.AddColumn<bool>  (name: "IsConfigurable",        schema: "public", table: "SystemDefaultSettings", type: "boolean", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<DateTime>(name: "LastReportRunAt",     schema: "public", table: "SystemDefaultSettings", type: "timestamp with time zone", nullable: true);
            migrationBuilder.AddColumn<int>   (name: "GracePeriodDays",       schema: "public", table: "SystemDefaultSettings", type: "integer", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<DateTime>(name: "SubscriptionEndDate", schema: "public", table: "SystemDefaultSettings", type: "timestamp with time zone", nullable: true);
            migrationBuilder.AddColumn<DateTime>(name: "SubscriptionStartDate", schema: "public", table: "SystemDefaultSettings", type: "timestamp with time zone", nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SystemDefaultSettingsAudit_Id",
                schema: "public",
                table: "SystemDefaultSettingsAudit",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_SystemDefaultSettings_UnitId",
                schema: "public",
                table: "SystemDefaultSettings",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_SystemDefaultSettings_CompanyId_UnitId",
                schema: "public",
                table: "SystemDefaultSettings",
                columns: new[] { "CompanyId", "UnitId" },
                unique: true);
        }
    }
}
