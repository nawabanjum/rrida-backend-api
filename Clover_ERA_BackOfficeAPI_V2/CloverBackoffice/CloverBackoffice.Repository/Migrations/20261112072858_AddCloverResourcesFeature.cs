using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CloverBackoffice.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddCloverResourcesFeature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing feature rows stay rows of the comparison table.
            migrationBuilder.AddColumn<bool>(
                name: "ShowInComparison",
                schema: "public",
                table: "PlanFeatures",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            // Clover Resources (worksheets with the weekly action): enforced per plan (Light no,
            // Pro yes) but shown as "*with Clover Resources" under the weekly action row, so not
            // a row of its own. Idempotent.
            migrationBuilder.Sql("""
                INSERT INTO public."PlanFeatures"
                    ("Key", "Title", "Description", "ValueType", "LimitUnit", "LimitPeriod", "Footnote",
                     "IsSystem", "ShowInComparison", "SortOrder", "IsActive", "CreatedBy", "CreatedAt", "UpdatedAt")
                VALUES
                    ('CLOVER_RESOURCES', 'Clover Resources', 'Worksheets and resources with the weekly suggested action.',
                     'INCLUDED', NULL, NULL, NULL, true, false, 12, true, 0, now(), now())
                ON CONFLICT ("Key") DO NOTHING;

                INSERT INTO public."ServicePlanFeatures"
                    ("ServicePlanId", "PlanFeatureId", "IsIncluded", "LimitValue", "DisplayText", "SubText", "UpdatedAt")
                SELECT p."Id", f."Id", c.included, NULL, NULL, NULL, now()
                FROM (VALUES ('light', false), ('pro', true)) AS c(code, included)
                JOIN public."ServicePlans" p ON p."Code" = c.code
                JOIN public."PlanFeatures" f ON f."Key" = 'CLOVER_RESOURCES'
                ON CONFLICT ("ServicePlanId", "PlanFeatureId") DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM public."ServicePlanFeatures"
                WHERE "PlanFeatureId" IN (SELECT "Id" FROM public."PlanFeatures" WHERE "Key" = 'CLOVER_RESOURCES');
                DELETE FROM public."PlanFeatures" WHERE "Key" = 'CLOVER_RESOURCES';
                """);

            migrationBuilder.DropColumn(
                name: "ShowInComparison",
                schema: "public",
                table: "PlanFeatures");
        }
    }
}
