using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CloverBackoffice.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddServicePlanCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlanFeatures",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ValueType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LimitUnit = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    LimitPeriod = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Footnote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: false),
                    UpdatedBy = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanFeatures", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ServicePlanAuditLogs",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ServicePlanId = table.Column<int>(type: "integer", nullable: true),
                    PlanFeatureId = table.Column<int>(type: "integer", nullable: true),
                    Action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ActorUserId = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Before = table.Column<string>(type: "jsonb", nullable: true),
                    After = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServicePlanAuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ServicePlans",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    InternalNotes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    PricingUnit = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    IsHighlighted = table.Column<bool>(type: "boolean", nullable: false),
                    AllowsTeamLimitExtension = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: false),
                    UpdatedBy = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServicePlans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ServicePlanFeatures",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ServicePlanId = table.Column<int>(type: "integer", nullable: false),
                    PlanFeatureId = table.Column<int>(type: "integer", nullable: false),
                    IsIncluded = table.Column<bool>(type: "boolean", nullable: false),
                    LimitValue = table.Column<int>(type: "integer", nullable: true),
                    DisplayText = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    SubText = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServicePlanFeatures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServicePlanFeatures_PlanFeatures_PlanFeatureId",
                        column: x => x.PlanFeatureId,
                        principalSchema: "public",
                        principalTable: "PlanFeatures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ServicePlanFeatures_ServicePlans_ServicePlanId",
                        column: x => x.ServicePlanId,
                        principalSchema: "public",
                        principalTable: "ServicePlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ServicePlanPriceTiers",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ServicePlanId = table.Column<int>(type: "integer", nullable: false),
                    MinManagers = table.Column<int>(type: "integer", nullable: false),
                    MaxManagers = table.Column<int>(type: "integer", nullable: true),
                    MonthlyPrice = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    AnnualPrice = table.Column<decimal>(type: "numeric(10,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServicePlanPriceTiers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServicePlanPriceTiers_ServicePlans_ServicePlanId",
                        column: x => x.ServicePlanId,
                        principalSchema: "public",
                        principalTable: "ServicePlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "UX_PlanFeatures_Key",
                schema: "public",
                table: "PlanFeatures",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServicePlanAuditLogs_Feature_CreatedAt",
                schema: "public",
                table: "ServicePlanAuditLogs",
                columns: new[] { "PlanFeatureId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicePlanAuditLogs_Plan_CreatedAt",
                schema: "public",
                table: "ServicePlanAuditLogs",
                columns: new[] { "ServicePlanId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicePlanFeatures_PlanFeatureId",
                schema: "public",
                table: "ServicePlanFeatures",
                column: "PlanFeatureId");

            migrationBuilder.CreateIndex(
                name: "UX_ServicePlanFeatures_Plan_Feature",
                schema: "public",
                table: "ServicePlanFeatures",
                columns: new[] { "ServicePlanId", "PlanFeatureId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ServicePlanPriceTiers_Plan_Min",
                schema: "public",
                table: "ServicePlanPriceTiers",
                columns: new[] { "ServicePlanId", "MinManagers" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ServicePlans_Code",
                schema: "public",
                table: "ServicePlans",
                column: "Code",
                unique: true);

            // Seed: the Light and Pro plans and the 11 comparison rows from the client's pricing
            // table. Idempotent (ON CONFLICT / NOT EXISTS); CreatedBy 0 = system.
            migrationBuilder.Sql(SeedSql);
        }

        private const string SeedSql = """
            INSERT INTO public."PlanFeatures"
                ("Key", "Title", "Description", "ValueType", "LimitUnit", "LimitPeriod", "Footnote",
                 "IsSystem", "SortOrder", "IsActive", "CreatedBy", "CreatedAt", "UpdatedAt")
            VALUES
                ('MANAGERS_AND_TEAMS', 'Managers and teams', 'How much the plan covers before you need another licence.', 'LIMIT', 'teams', 'NONE', NULL, true, 1, true, 0, now(), now()),
                ('DAILY_CHECKINS', 'Daily anonymous team check-ins', 'Anonymous feedback against our Clover Framework, with room to reflect.', 'INCLUDED', NULL, NULL, NULL, true, 2, true, 0, now(), now()),
                ('TEAM_HEALTH_DASHBOARD', 'Team Health Dashboard', 'Individual and team conditions and response rate, kept current.', 'INCLUDED', NULL, NULL, NULL, true, 3, true, 0, now(), now()),
                ('CUSTOM_QUESTIONS', 'Custom questions and action feedback loop', 'Ask custom questions and provide feedback to your team through RRIDA.', 'INCLUDED', NULL, NULL, NULL, true, 4, true, 0, now(), now()),
                ('WEEKLY_SUGGESTED_ACTION', 'Weekly suggested action', 'One practical thing to try each week.', 'TEXT', NULL, NULL, '* Clover Framework includes over 2000 micro-actions and over 100 worksheet resources, backed by our Clover Individual and Team Health framework.', true, 5, true, 0, now(), now()),
                ('TEAM_HEALTH_TIMELINE', 'Interactive Team Health Timeline', 'A living picture of what''s affecting your team, to use with your leadership.', 'INCLUDED', NULL, NULL, NULL, true, 6, true, 0, now(), now()),
                ('DAILY_NUDGES_MONTHLY_REPORTING', 'Daily quick-action nudges and monthly reporting', 'A nudge each day, and deep-insight reporting every month.', 'INCLUDED', NULL, NULL, NULL, true, 7, true, 0, now(), now()),
                ('PORTFOLIO_VIEW', 'Team-of-teams Portfolio View', 'Every team you''re responsible for, side by side in one place.', 'INCLUDED', NULL, NULL, NULL, true, 8, true, 0, now(), now()),
                ('ERAN_COACH', 'Eran, our Clover Framework Conversational Coach', 'Talk something through whenever you need to, with advice you can act on.', 'LIMIT', 'chats', 'MONTH', NULL, true, 9, true, 0, now(), now()),
                ('ERAN_CONVERSATIONS', 'Eran conversations', NULL, 'LIMIT', 'conversations', 'MONTH', NULL, true, 10, true, 0, now(), now()),
                ('LIVE_COACHING', 'Live coaching', NULL, 'TEXT', NULL, NULL, NULL, true, 11, true, 0, now(), now())
            ON CONFLICT ("Key") DO NOTHING;

            INSERT INTO public."ServicePlans"
                ("Code", "Name", "Description", "InternalNotes", "PricingUnit", "Currency", "IsHighlighted",
                 "AllowsTeamLimitExtension", "SortOrder", "IsActive", "Version", "CreatedBy", "CreatedAt", "UpdatedAt")
            VALUES
                ('light', 'Light', NULL, 'Seeded from the client pricing table (2026-11).', 'manager', 'USD', false, false, 1, true, 1, 0, now(), now()),
                ('pro', 'Pro', NULL, 'Seeded from the client pricing table (2026-11).', 'manager', 'USD', true, true, 2, true, 1, 0, now(), now())
            ON CONFLICT ("Code") DO NOTHING;

            INSERT INTO public."ServicePlanPriceTiers" ("ServicePlanId", "MinManagers", "MaxManagers", "MonthlyPrice", "AnnualPrice")
            SELECT p."Id", t.min_m, t.max_m, t.monthly, t.annual
            FROM (VALUES
                ('light', 1, NULL::integer, 97.00, 970.00),
                ('pro', 1, 4, 695.00, 6950.00),
                ('pro', 5, 9, 595.00, 5950.00),
                ('pro', 10, NULL::integer, 495.00, 4950.00)
            ) AS t(code, min_m, max_m, monthly, annual)
            JOIN public."ServicePlans" p ON p."Code" = t.code
            WHERE NOT EXISTS (SELECT 1 FROM public."ServicePlanPriceTiers" x WHERE x."ServicePlanId" = p."Id");

            INSERT INTO public."ServicePlanFeatures"
                ("ServicePlanId", "PlanFeatureId", "IsIncluded", "LimitValue", "DisplayText", "SubText", "UpdatedAt")
            SELECT p."Id", f."Id", c.included, c.limit_value, c.display_text, c.sub_text, now()
            FROM (VALUES
                ('light', 'MANAGERS_AND_TEAMS', true, 1, '1', NULL),
                ('light', 'DAILY_CHECKINS', true, NULL::integer, NULL, NULL),
                ('light', 'TEAM_HEALTH_DASHBOARD', true, NULL::integer, NULL, NULL),
                ('light', 'CUSTOM_QUESTIONS', true, NULL::integer, NULL, NULL),
                ('light', 'WEEKLY_SUGGESTED_ACTION', true, NULL::integer, 'Clover actions', NULL),
                ('light', 'TEAM_HEALTH_TIMELINE', false, NULL::integer, NULL, NULL),
                ('light', 'DAILY_NUDGES_MONTHLY_REPORTING', false, NULL::integer, NULL, NULL),
                ('light', 'PORTFOLIO_VIEW', false, NULL::integer, NULL, NULL),
                ('light', 'ERAN_COACH', true, 10, '10 chats', 'per month'),
                ('light', 'ERAN_CONVERSATIONS', true, 10, '10 conversations a month, more available', NULL),
                ('light', 'LIVE_COACHING', true, NULL::integer, 'First reading walk-through', NULL),
                ('pro', 'MANAGERS_AND_TEAMS', true, 3, 'Up to 3', NULL),
                ('pro', 'DAILY_CHECKINS', true, NULL::integer, NULL, NULL),
                ('pro', 'TEAM_HEALTH_DASHBOARD', true, NULL::integer, NULL, NULL),
                ('pro', 'CUSTOM_QUESTIONS', true, NULL::integer, NULL, NULL),
                ('pro', 'WEEKLY_SUGGESTED_ACTION', true, NULL::integer, 'Clover actions', '*with Clover Resources'),
                ('pro', 'TEAM_HEALTH_TIMELINE', true, NULL::integer, NULL, NULL),
                ('pro', 'DAILY_NUDGES_MONTHLY_REPORTING', true, NULL::integer, NULL, NULL),
                ('pro', 'PORTFOLIO_VIEW', true, NULL::integer, NULL, NULL),
                ('pro', 'ERAN_COACH', true, NULL::integer, 'Unlimited', NULL),
                ('pro', 'ERAN_CONVERSATIONS', true, NULL::integer, 'Unlimited', NULL),
                ('pro', 'LIVE_COACHING', true, NULL::integer, 'First reading plus ongoing', NULL)
            ) AS c(code, feature_key, included, limit_value, display_text, sub_text)
            JOIN public."ServicePlans" p ON p."Code" = c.code
            JOIN public."PlanFeatures" f ON f."Key" = c.feature_key
            ON CONFLICT ("ServicePlanId", "PlanFeatureId") DO NOTHING;
            """;

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServicePlanAuditLogs",
                schema: "public");

            migrationBuilder.DropTable(
                name: "ServicePlanFeatures",
                schema: "public");

            migrationBuilder.DropTable(
                name: "ServicePlanPriceTiers",
                schema: "public");

            migrationBuilder.DropTable(
                name: "PlanFeatures",
                schema: "public");

            migrationBuilder.DropTable(
                name: "ServicePlans",
                schema: "public");
        }
    }
}
