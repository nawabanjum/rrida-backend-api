using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CloverBackoffice.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddLicenceServicePlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ServicePlanId",
                table: "CompanySubscriptions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ServicePlanId",
                table: "CompanyLicenses",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompanySubscriptions_ServicePlanId",
                table: "CompanySubscriptions",
                column: "ServicePlanId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyLicenses_ServicePlanId",
                table: "CompanyLicenses",
                column: "ServicePlanId");

            migrationBuilder.AddForeignKey(
                name: "FK_CompanyLicenses_ServicePlans_ServicePlanId",
                table: "CompanyLicenses",
                column: "ServicePlanId",
                principalSchema: "public",
                principalTable: "ServicePlans",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CompanySubscriptions_ServicePlans_ServicePlanId",
                table: "CompanySubscriptions",
                column: "ServicePlanId",
                principalSchema: "public",
                principalTable: "ServicePlans",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CompanyLicenses_ServicePlans_ServicePlanId",
                table: "CompanyLicenses");

            migrationBuilder.DropForeignKey(
                name: "FK_CompanySubscriptions_ServicePlans_ServicePlanId",
                table: "CompanySubscriptions");

            migrationBuilder.DropIndex(
                name: "IX_CompanySubscriptions_ServicePlanId",
                table: "CompanySubscriptions");

            migrationBuilder.DropIndex(
                name: "IX_CompanyLicenses_ServicePlanId",
                table: "CompanyLicenses");

            migrationBuilder.DropColumn(
                name: "ServicePlanId",
                table: "CompanySubscriptions");

            migrationBuilder.DropColumn(
                name: "ServicePlanId",
                table: "CompanyLicenses");
        }
    }
}
