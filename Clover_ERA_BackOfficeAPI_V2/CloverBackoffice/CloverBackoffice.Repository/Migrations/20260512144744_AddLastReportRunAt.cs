using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CloverBackoffice.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddLastReportRunAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastReportRunAt",
                schema: "public",
                table: "SystemDefaultSettingsAudit",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastReportRunAt",
                schema: "public",
                table: "SystemDefaultSettings",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastReportRunAt",
                schema: "public",
                table: "SystemDefaultSettingsAudit");

            migrationBuilder.DropColumn(
                name: "LastReportRunAt",
                schema: "public",
                table: "SystemDefaultSettings");
        }
    }
}
