using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CloverBackoffice.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyLicenseAssignedUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AssignedUserAt",
                table: "CompanyLicenses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AssignedUserId",
                table: "CompanyLicenses",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompanyLicenses_AssignedUserId",
                table: "CompanyLicenses",
                column: "AssignedUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_CompanyLicenses_Users_AssignedUserId",
                table: "CompanyLicenses",
                column: "AssignedUserId",
                principalTable: "Users",
                principalColumn: "UserId",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CompanyLicenses_Users_AssignedUserId",
                table: "CompanyLicenses");

            migrationBuilder.DropIndex(
                name: "IX_CompanyLicenses_AssignedUserId",
                table: "CompanyLicenses");

            migrationBuilder.DropColumn(
                name: "AssignedUserAt",
                table: "CompanyLicenses");

            migrationBuilder.DropColumn(
                name: "AssignedUserId",
                table: "CompanyLicenses");
        }
    }
}
