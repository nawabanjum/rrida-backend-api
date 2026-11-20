using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CloverBackoffice.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddStripeProductIdToCompanySubscription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // NOTE: the auto-generated migration also wanted to create
            // CompanyLicenses / AssignedCompanyLicenses / *AuditLogs tables and
            // add CardBrand/CardLast4/Discount/GracePeriod/LastWarningDaysSent
            // columns — all of which already exist in the live database (the
            // model snapshot had drifted out of sync). Those operations were
            // removed so this migration only adds the new StripeProductId
            // column and touches nothing that already exists.
            migrationBuilder.AddColumn<string>(
                name: "StripeProductId",
                table: "CompanySubscriptions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StripeProductId",
                table: "CompanySubscriptions");
        }
    }
}
