using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CloverBackoffice.Repository.Migrations
{
    /// <inheritdoc />
    public partial class RemoveScheduleDateFromTo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DateFrom",
                schema: "public",
                table: "SystemDefaultSettingsAudit");

            migrationBuilder.DropColumn(
                name: "DateTo",
                schema: "public",
                table: "SystemDefaultSettingsAudit");

            migrationBuilder.DropColumn(
                name: "DateFrom",
                schema: "public",
                table: "SystemDefaultSettings");

            migrationBuilder.DropColumn(
                name: "DateTo",
                schema: "public",
                table: "SystemDefaultSettings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DateFrom",
                schema: "public",
                table: "SystemDefaultSettingsAudit",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DateTo",
                schema: "public",
                table: "SystemDefaultSettingsAudit",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DateFrom",
                schema: "public",
                table: "SystemDefaultSettings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DateTo",
                schema: "public",
                table: "SystemDefaultSettings",
                type: "timestamp with time zone",
                nullable: true);
        }
    }
}
