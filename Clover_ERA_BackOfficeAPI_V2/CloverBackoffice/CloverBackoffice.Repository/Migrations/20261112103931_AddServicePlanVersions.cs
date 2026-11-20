using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CloverBackoffice.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddServicePlanVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ServicePlanVersionId",
                table: "CompanySubscriptions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ServicePlanVersionId",
                table: "CompanyLicenses",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ServicePlanVersions",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ServicePlanId = table.Column<int>(type: "integer", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    Terms = table.Column<string>(type: "jsonb", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedBy = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServicePlanVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServicePlanVersions_ServicePlans_ServicePlanId",
                        column: x => x.ServicePlanId,
                        principalSchema: "public",
                        principalTable: "ServicePlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompanySubscriptions_ServicePlanVersionId",
                table: "CompanySubscriptions",
                column: "ServicePlanVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyLicenses_ServicePlanVersionId",
                table: "CompanyLicenses",
                column: "ServicePlanVersionId");

            migrationBuilder.CreateIndex(
                name: "UX_ServicePlanVersions_Plan_Number",
                schema: "public",
                table: "ServicePlanVersions",
                columns: new[] { "ServicePlanId", "VersionNumber" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CompanyLicenses_ServicePlanVersions_ServicePlanVersionId",
                table: "CompanyLicenses",
                column: "ServicePlanVersionId",
                principalSchema: "public",
                principalTable: "ServicePlanVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CompanySubscriptions_ServicePlanVersions_ServicePlanVersion~",
                table: "CompanySubscriptions",
                column: "ServicePlanVersionId",
                principalSchema: "public",
                principalTable: "ServicePlanVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Version 1 of every existing plan: its terms as they are now, frozen. Licences with
            // no version (all existing ones, including pre-plan licences = Pro) read version 1, so
            // later plan edits don't change them. JSON matches PlanTerms (PascalCase). Idempotent.
            migrationBuilder.Sql("""
                INSERT INTO public."ServicePlanVersions" ("ServicePlanId", "VersionNumber", "Terms", "Reason", "CreatedBy", "CreatedAt")
                SELECT p."Id", 1,
                       jsonb_build_object(
                           'Currency', p."Currency",
                           'PricingUnit', p."PricingUnit",
                           'AllowsTeamLimitExtension', p."AllowsTeamLimitExtension",
                           'PriceTiers', COALESCE((
                               SELECT jsonb_agg(jsonb_build_object(
                                          'MinManagers', t."MinManagers", 'MaxManagers', t."MaxManagers",
                                          'MonthlyPrice', t."MonthlyPrice", 'AnnualPrice', t."AnnualPrice")
                                      ORDER BY t."MinManagers")
                               FROM public."ServicePlanPriceTiers" t WHERE t."ServicePlanId" = p."Id"), '[]'::jsonb),
                           'Features', COALESCE((
                               SELECT jsonb_agg(jsonb_build_object(
                                          'FeatureId', c."PlanFeatureId", 'Key', f."Key", 'IsIncluded', c."IsIncluded",
                                          'LimitValue', c."LimitValue", 'DisplayText', c."DisplayText", 'SubText', c."SubText")
                                      ORDER BY c."PlanFeatureId")
                               FROM public."ServicePlanFeatures" c
                               JOIN public."PlanFeatures" f ON f."Id" = c."PlanFeatureId"
                               WHERE c."ServicePlanId" = p."Id"), '[]'::jsonb)),
                       'Initial version', 0, now()
                FROM public."ServicePlans" p
                WHERE NOT EXISTS (SELECT 1 FROM public."ServicePlanVersions" v WHERE v."ServicePlanId" = p."Id");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CompanyLicenses_ServicePlanVersions_ServicePlanVersionId",
                table: "CompanyLicenses");

            migrationBuilder.DropForeignKey(
                name: "FK_CompanySubscriptions_ServicePlanVersions_ServicePlanVersion~",
                table: "CompanySubscriptions");

            migrationBuilder.DropTable(
                name: "ServicePlanVersions",
                schema: "public");

            migrationBuilder.DropIndex(
                name: "IX_CompanySubscriptions_ServicePlanVersionId",
                table: "CompanySubscriptions");

            migrationBuilder.DropIndex(
                name: "IX_CompanyLicenses_ServicePlanVersionId",
                table: "CompanyLicenses");

            migrationBuilder.DropColumn(
                name: "ServicePlanVersionId",
                table: "CompanySubscriptions");

            migrationBuilder.DropColumn(
                name: "ServicePlanVersionId",
                table: "CompanyLicenses");
        }
    }
}
