using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CloverBackoffice.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddNotesToCompanyLicense : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ExpiryDate already exists on both tables in the live DB; the model
            // snapshot was out of sync and regenerated spurious AddColumn calls
            // for it. Stripped so this migration only adds the new Notes column.
            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "CompanyLicenses",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                schema: "public",
                table: "CompanyLicenseAuditLogs",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Notes",
                table: "CompanyLicenses");

            migrationBuilder.DropColumn(
                name: "Notes",
                schema: "public",
                table: "CompanyLicenseAuditLogs");
        }
    }
}
