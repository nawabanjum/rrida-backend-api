using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace CloverBackoffice.Repository.Migrations
{
    /// <inheritdoc />
    public partial class RedesignTeamQuestionsToMatchLegacy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TeamDailyFeedbacks_BusinessUnits_BusinessUnitId",
                table: "TeamDailyFeedbacks");

            migrationBuilder.DropForeignKey(
                name: "FK_TeamDailyFeedbacks_TeamDailyQuestionDeliveries_TeamDailyQue~",
                table: "TeamDailyFeedbacks");

            migrationBuilder.DropForeignKey(
                name: "FK_TeamDailyFeedbacks_TeamDailyQuestions_TeamDailyQuestionId",
                table: "TeamDailyFeedbacks");

            migrationBuilder.DropForeignKey(
                name: "FK_TeamDailyFeedbacks_TeamDailyReasons_TeamDailyReasonId",
                table: "TeamDailyFeedbacks");

            migrationBuilder.DropForeignKey(
                name: "FK_TeamDailyQuestions_BusinessUnits_OwnerBusinessUnitId",
                table: "TeamDailyQuestions");

            migrationBuilder.DropForeignKey(
                name: "FK_TeamDailyQuestions_Companies_OwnerCompanyId",
                table: "TeamDailyQuestions");

            migrationBuilder.DropForeignKey(
                name: "FK_TeamDailyQuestions_TeamQuestionSubCategories_TeamQuestionSu~",
                table: "TeamDailyQuestions");

            migrationBuilder.DropForeignKey(
                name: "FK_TeamDailyQuestionSpecifications_TeamDailyQuestions_BaseTeam~",
                table: "TeamDailyQuestionSpecifications");

            migrationBuilder.DropForeignKey(
                name: "FK_TeamDailyQuestionSpecifications_TeamDailyQuestions_TeamDail~",
                table: "TeamDailyQuestionSpecifications");

            migrationBuilder.DropForeignKey(
                name: "FK_TeamDailyReasons_TeamDailyQuestions_TeamDailyQuestionId",
                table: "TeamDailyReasons");

            migrationBuilder.DropForeignKey(
                name: "FK_TeamDailyReasons_TeamQuestionAttributionTypes_TeamQuestionA~",
                table: "TeamDailyReasons");

            migrationBuilder.DropForeignKey(
                name: "FK_TeamDailyReasons_TeamStateIndicators_TeamStateIndicatorId",
                table: "TeamDailyReasons");

            migrationBuilder.DropTable(
                name: "TeamDailyQuestionResponseReceipts");

            migrationBuilder.DropTable(
                name: "TeamQuestionAttributionTypes");

            migrationBuilder.DropTable(
                name: "TeamQuestionSubCategories");

            migrationBuilder.DropTable(
                name: "TeamStateIndicators");

            migrationBuilder.DropTable(
                name: "TeamDailyQuestionDeliveries");

            migrationBuilder.DropTable(
                name: "TeamQuestionMainCategories");

            migrationBuilder.DropTable(
                name: "TeamQuestionSamplingModes");

            migrationBuilder.DropTable(
                name: "TeamCustomQuestionSchedules");

            migrationBuilder.DropTable(
                name: "TeamGoals");

            migrationBuilder.DropIndex(
                name: "IX_TeamDailyReasons_StateIndicator",
                table: "TeamDailyReasons");

            migrationBuilder.DropIndex(
                name: "IX_TeamDailyReasons_TeamQuestionAttributionTypeId",
                table: "TeamDailyReasons");

            migrationBuilder.DropIndex(
                name: "UX_TeamDailyReasons_Question_Sentiment_Position",
                table: "TeamDailyReasons");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TeamDailyReasons_Position",
                table: "TeamDailyReasons");

            migrationBuilder.DropIndex(
                name: "IX_TeamDailyQuestionSpecifications_BaseTeamDailyQuestionId",
                table: "TeamDailyQuestionSpecifications");

            migrationBuilder.DropIndex(
                name: "UX_TeamDailyQuestionSpecifications_Ask_Variant",
                table: "TeamDailyQuestionSpecifications");

            migrationBuilder.DropIndex(
                name: "UX_TeamDailyQuestionSpecifications_Question",
                table: "TeamDailyQuestionSpecifications");

            migrationBuilder.DropIndex(
                name: "UX_TeamDailyQuestionSpecifications_RotationOrder",
                table: "TeamDailyQuestionSpecifications");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TeamDailyQuestionSpecifications_Variant",
                table: "TeamDailyQuestionSpecifications");

            migrationBuilder.DropIndex(
                name: "IX_TeamDailyQuestions_OwnerBusinessUnitId",
                table: "TeamDailyQuestions");

            migrationBuilder.DropIndex(
                name: "IX_TeamDailyQuestions_OwnerCompany",
                table: "TeamDailyQuestions");

            migrationBuilder.DropIndex(
                name: "IX_TeamDailyQuestions_SubCategory_Active",
                table: "TeamDailyQuestions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TeamDailyQuestions_CustomOwnership",
                table: "TeamDailyQuestions");

            migrationBuilder.DropIndex(
                name: "UX_TeamDailyMicroActions_Reason",
                table: "TeamDailyMicroActions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_TeamDailyFeedbacks",
                table: "TeamDailyFeedbacks");

            migrationBuilder.DropIndex(
                name: "IX_TeamDailyFeedbacks_Delivery",
                table: "TeamDailyFeedbacks");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "TeamDailyReasons");

            migrationBuilder.DropColumn(
                name: "IsDel",
                table: "TeamDailyReasons");

            migrationBuilder.DropColumn(
                name: "Position",
                table: "TeamDailyReasons");

            migrationBuilder.DropColumn(
                name: "TeamQuestionAttributionTypeId",
                table: "TeamDailyReasons");

            migrationBuilder.DropColumn(
                name: "TeamStateIndicatorId",
                table: "TeamDailyReasons");

            migrationBuilder.DropColumn(
                name: "AskNumber",
                table: "TeamDailyQuestionSpecifications");

            migrationBuilder.DropColumn(
                name: "BaseTeamDailyQuestionId",
                table: "TeamDailyQuestionSpecifications");

            migrationBuilder.DropColumn(
                name: "EffectiveFromUtc",
                table: "TeamDailyQuestionSpecifications");

            migrationBuilder.DropColumn(
                name: "EffectiveToUtc",
                table: "TeamDailyQuestionSpecifications");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "TeamDailyQuestionSpecifications");

            migrationBuilder.DropColumn(
                name: "IsDel",
                table: "TeamDailyQuestionSpecifications");

            migrationBuilder.DropColumn(
                name: "RequiresGoal",
                table: "TeamDailyQuestionSpecifications");

            migrationBuilder.DropColumn(
                name: "RotationOrder",
                table: "TeamDailyQuestionSpecifications");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "TeamDailyQuestions");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "TeamDailyQuestions");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "TeamDailyQuestions");

            migrationBuilder.DropColumn(
                name: "IsDel",
                table: "TeamDailyQuestions");

            migrationBuilder.DropColumn(
                name: "OriginType",
                table: "TeamDailyQuestions");

            migrationBuilder.DropColumn(
                name: "OwnerBusinessUnitId",
                table: "TeamDailyQuestions");

            migrationBuilder.DropColumn(
                name: "OwnerCompanyId",
                table: "TeamDailyQuestions");

            migrationBuilder.DropColumn(
                name: "Body",
                table: "TeamDailyMicroActions");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "TeamDailyMicroActions");

            migrationBuilder.DropColumn(
                name: "IsDel",
                table: "TeamDailyMicroActions");

            migrationBuilder.DropColumn(
                name: "RoutesToManager",
                table: "TeamDailyMicroActions");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "TeamDailyMicroActions");

            migrationBuilder.DropColumn(
                name: "TeamDailyQuestionDeliveryId",
                table: "TeamDailyFeedbacks");

            migrationBuilder.RenameTable(
                name: "TeamDailyFeedbacks",
                newName: "TeamDailyFeedback",
                newSchema: "public");

            migrationBuilder.RenameColumn(
                name: "TeamDailyQuestionId",
                table: "TeamDailyReasons",
                newName: "SpecificationId");

            migrationBuilder.RenameColumn(
                name: "SentimentType",
                table: "TeamDailyReasons",
                newName: "TeamDailyReasonSentimentType");

            migrationBuilder.RenameColumn(
                name: "RequiresFreeText",
                table: "TeamDailyReasons",
                newName: "RequiresFreeTextFeedback");

            migrationBuilder.RenameColumn(
                name: "ReasonText",
                table: "TeamDailyReasons",
                newName: "Text");

            migrationBuilder.RenameColumn(
                name: "VariantType",
                table: "TeamDailyQuestionSpecifications",
                newName: "Variant");

            migrationBuilder.RenameColumn(
                name: "TeamQuestionSubCategoryId",
                table: "TeamDailyQuestions",
                newName: "ContentCultureId");

            migrationBuilder.RenameColumn(
                name: "QuestionText",
                table: "TeamDailyQuestions",
                newName: "Text");

            migrationBuilder.RenameIndex(
                name: "IX_TeamDailyFeedbacks_Unit_Submitted",
                schema: "public",
                table: "TeamDailyFeedback",
                newName: "IX_TeamDailyFeedback_Unit_Submitted");

            migrationBuilder.RenameIndex(
                name: "IX_TeamDailyFeedbacks_TeamDailyQuestionId",
                schema: "public",
                table: "TeamDailyFeedback",
                newName: "IX_TeamDailyFeedback_Question");

            migrationBuilder.RenameIndex(
                name: "IX_TeamDailyFeedbacks_Reason",
                schema: "public",
                table: "TeamDailyFeedback",
                newName: "IX_TeamDailyFeedback_Reason");

            migrationBuilder.AddColumn<long>(
                name: "ContentCultureId",
                table: "TeamDailyReasons",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "TeamState",
                table: "TeamDailyReasons",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ContentCultureId",
                table: "TeamDailyQuestionSpecifications",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "Ordinal",
                table: "TeamDailyQuestionSpecifications",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SubCategory",
                table: "TeamDailyQuestionSpecifications",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TeamElement",
                table: "TeamDailyQuestionSpecifications",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "ContentCultureId",
                table: "TeamDailyMicroActions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "Text",
                table: "TeamDailyMicroActions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddPrimaryKey(
                name: "PK_TeamDailyFeedback",
                schema: "public",
                table: "TeamDailyFeedback",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "CustomQuestionAudits",
                schema: "public",
                columns: table => new
                {
                    AuditId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CustomQuestionId = table.Column<long>(type: "bigint", nullable: false),
                    Text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ContentCultureId = table.Column<long>(type: "bigint", nullable: false),
                    UsageContext = table.Column<int>(type: "integer", nullable: false),
                    SentimentType = table.Column<int>(type: "integer", nullable: true),
                    CloverCategory = table.Column<int>(type: "integer", nullable: true),
                    TeamElement = table.Column<int>(type: "integer", nullable: true),
                    SubCategory = table.Column<int>(type: "integer", nullable: true),
                    Variant = table.Column<int>(type: "integer", nullable: true),
                    Ordinal = table.Column<int>(type: "integer", nullable: true),
                    EffectiveFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EffectiveToUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ChangeSummary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedBy = table.Column<int>(type: "integer", nullable: true),
                    UpdatedBy = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDel = table.Column<bool>(type: "boolean", nullable: false),
                    LoggedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    OperationType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomQuestionAudits", x => x.AuditId);
                });

            migrationBuilder.CreateTable(
                name: "CustomQuestions",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ContentCultureId = table.Column<long>(type: "bigint", nullable: false),
                    UsageContext = table.Column<int>(type: "integer", nullable: false),
                    SentimentType = table.Column<int>(type: "integer", nullable: true),
                    CloverCategory = table.Column<int>(type: "integer", nullable: true),
                    TeamElement = table.Column<int>(type: "integer", nullable: true),
                    SubCategory = table.Column<int>(type: "integer", nullable: true),
                    Variant = table.Column<int>(type: "integer", nullable: true),
                    Ordinal = table.Column<int>(type: "integer", nullable: true),
                    EffectiveFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EffectiveToUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: true),
                    UpdatedBy = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDel = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomQuestions", x => x.Id);
                    table.CheckConstraint("CK_CustomQuestions_DailyConfig", "(\"UsageContext\" IN (1,3) AND \"CloverCategory\" IS NOT NULL) OR (\"UsageContext\" = 2 AND \"CloverCategory\" IS NULL)");
                    table.CheckConstraint("CK_CustomQuestions_TeamConfig", "(\"UsageContext\" IN (2,3) AND \"TeamElement\" IS NOT NULL AND \"SubCategory\" IS NOT NULL AND \"Variant\" IS NOT NULL) OR (\"UsageContext\" = 1 AND \"TeamElement\" IS NULL AND \"SubCategory\" IS NULL AND \"Variant\" IS NULL)");
                    table.CheckConstraint("CK_CustomQuestions_Window", "\"EffectiveFromUtc\" IS NULL OR \"EffectiveToUtc\" IS NULL OR \"EffectiveToUtc\" >= \"EffectiveFromUtc\"");
                    table.ForeignKey(
                        name: "FK_CustomQuestions_ContentCultures_ContentCultureId",
                        column: x => x.ContentCultureId,
                        principalTable: "ContentCultures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TeamDailyReasons_ContentCultureId",
                table: "TeamDailyReasons",
                column: "ContentCultureId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamDailyReasons_Specification",
                table: "TeamDailyReasons",
                column: "SpecificationId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamDailyReasons_TeamState",
                table: "TeamDailyReasons",
                column: "TeamState");

            migrationBuilder.CreateIndex(
                name: "IX_TeamDailyQuestionSpecifications_ContentCultureId",
                table: "TeamDailyQuestionSpecifications",
                column: "ContentCultureId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamDailyQuestionSpecifications_TeamDailyQuestionId",
                table: "TeamDailyQuestionSpecifications",
                column: "TeamDailyQuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamDailyQuestionSpecifications_TeamElement",
                table: "TeamDailyQuestionSpecifications",
                column: "TeamElement");

            migrationBuilder.CreateIndex(
                name: "UX_TeamDailyQuestionSpecifications_SubCategory_Variant_Ordinal_Culture",
                table: "TeamDailyQuestionSpecifications",
                columns: new[] { "SubCategory", "Variant", "Ordinal", "ContentCultureId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamDailyQuestions_ContentCultureId",
                table: "TeamDailyQuestions",
                column: "ContentCultureId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamDailyMicroActions_ContentCultureId",
                table: "TeamDailyMicroActions",
                column: "ContentCultureId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamDailyMicroActions_Reason",
                table: "TeamDailyMicroActions",
                column: "TeamDailyReasonId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomQuestionAudits_Operation",
                schema: "public",
                table: "CustomQuestionAudits",
                column: "OperationType");

            migrationBuilder.CreateIndex(
                name: "IX_CustomQuestionAudits_Question_LoggedAt",
                schema: "public",
                table: "CustomQuestionAudits",
                columns: new[] { "CustomQuestionId", "LoggedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomQuestions_ContentCultureId",
                schema: "public",
                table: "CustomQuestions",
                column: "ContentCultureId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomQuestions_Status_Window",
                schema: "public",
                table: "CustomQuestions",
                columns: new[] { "Status", "EffectiveFromUtc", "EffectiveToUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomQuestions_UsageContext",
                schema: "public",
                table: "CustomQuestions",
                column: "UsageContext",
                filter: "\"IsDel\" = false");

            migrationBuilder.AddForeignKey(
                name: "FK_TeamDailyFeedback_BusinessUnits_BusinessUnitId",
                schema: "public",
                table: "TeamDailyFeedback",
                column: "BusinessUnitId",
                principalTable: "BusinessUnits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TeamDailyFeedback_TeamDailyQuestions_TeamDailyQuestionId",
                schema: "public",
                table: "TeamDailyFeedback",
                column: "TeamDailyQuestionId",
                principalTable: "TeamDailyQuestions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TeamDailyFeedback_TeamDailyReasons_TeamDailyReasonId",
                schema: "public",
                table: "TeamDailyFeedback",
                column: "TeamDailyReasonId",
                principalTable: "TeamDailyReasons",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TeamDailyMicroActions_ContentCultures_ContentCultureId",
                table: "TeamDailyMicroActions",
                column: "ContentCultureId",
                principalTable: "ContentCultures",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TeamDailyQuestions_ContentCultures_ContentCultureId",
                table: "TeamDailyQuestions",
                column: "ContentCultureId",
                principalTable: "ContentCultures",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TeamDailyQuestionSpecifications_ContentCultures_ContentCult~",
                table: "TeamDailyQuestionSpecifications",
                column: "ContentCultureId",
                principalTable: "ContentCultures",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TeamDailyQuestionSpecifications_TeamDailyQuestions_TeamDail~",
                table: "TeamDailyQuestionSpecifications",
                column: "TeamDailyQuestionId",
                principalTable: "TeamDailyQuestions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TeamDailyReasons_ContentCultures_ContentCultureId",
                table: "TeamDailyReasons",
                column: "ContentCultureId",
                principalTable: "ContentCultures",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TeamDailyReasons_TeamDailyQuestionSpecifications_Specificat~",
                table: "TeamDailyReasons",
                column: "SpecificationId",
                principalTable: "TeamDailyQuestionSpecifications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TeamDailyFeedback_BusinessUnits_BusinessUnitId",
                schema: "public",
                table: "TeamDailyFeedback");

            migrationBuilder.DropForeignKey(
                name: "FK_TeamDailyFeedback_TeamDailyQuestions_TeamDailyQuestionId",
                schema: "public",
                table: "TeamDailyFeedback");

            migrationBuilder.DropForeignKey(
                name: "FK_TeamDailyFeedback_TeamDailyReasons_TeamDailyReasonId",
                schema: "public",
                table: "TeamDailyFeedback");

            migrationBuilder.DropForeignKey(
                name: "FK_TeamDailyMicroActions_ContentCultures_ContentCultureId",
                table: "TeamDailyMicroActions");

            migrationBuilder.DropForeignKey(
                name: "FK_TeamDailyQuestions_ContentCultures_ContentCultureId",
                table: "TeamDailyQuestions");

            migrationBuilder.DropForeignKey(
                name: "FK_TeamDailyQuestionSpecifications_ContentCultures_ContentCult~",
                table: "TeamDailyQuestionSpecifications");

            migrationBuilder.DropForeignKey(
                name: "FK_TeamDailyQuestionSpecifications_TeamDailyQuestions_TeamDail~",
                table: "TeamDailyQuestionSpecifications");

            migrationBuilder.DropForeignKey(
                name: "FK_TeamDailyReasons_ContentCultures_ContentCultureId",
                table: "TeamDailyReasons");

            migrationBuilder.DropForeignKey(
                name: "FK_TeamDailyReasons_TeamDailyQuestionSpecifications_Specificat~",
                table: "TeamDailyReasons");

            migrationBuilder.DropTable(
                name: "CustomQuestionAudits",
                schema: "public");

            migrationBuilder.DropTable(
                name: "CustomQuestions",
                schema: "public");

            migrationBuilder.DropIndex(
                name: "IX_TeamDailyReasons_ContentCultureId",
                table: "TeamDailyReasons");

            migrationBuilder.DropIndex(
                name: "IX_TeamDailyReasons_Specification",
                table: "TeamDailyReasons");

            migrationBuilder.DropIndex(
                name: "IX_TeamDailyReasons_TeamState",
                table: "TeamDailyReasons");

            migrationBuilder.DropIndex(
                name: "IX_TeamDailyQuestionSpecifications_ContentCultureId",
                table: "TeamDailyQuestionSpecifications");

            migrationBuilder.DropIndex(
                name: "IX_TeamDailyQuestionSpecifications_TeamDailyQuestionId",
                table: "TeamDailyQuestionSpecifications");

            migrationBuilder.DropIndex(
                name: "IX_TeamDailyQuestionSpecifications_TeamElement",
                table: "TeamDailyQuestionSpecifications");

            migrationBuilder.DropIndex(
                name: "UX_TeamDailyQuestionSpecifications_SubCategory_Variant_Ordinal_Culture",
                table: "TeamDailyQuestionSpecifications");

            migrationBuilder.DropIndex(
                name: "IX_TeamDailyQuestions_ContentCultureId",
                table: "TeamDailyQuestions");

            migrationBuilder.DropIndex(
                name: "IX_TeamDailyMicroActions_ContentCultureId",
                table: "TeamDailyMicroActions");

            migrationBuilder.DropIndex(
                name: "IX_TeamDailyMicroActions_Reason",
                table: "TeamDailyMicroActions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_TeamDailyFeedback",
                schema: "public",
                table: "TeamDailyFeedback");

            migrationBuilder.DropColumn(
                name: "ContentCultureId",
                table: "TeamDailyReasons");

            migrationBuilder.DropColumn(
                name: "TeamState",
                table: "TeamDailyReasons");

            migrationBuilder.DropColumn(
                name: "ContentCultureId",
                table: "TeamDailyQuestionSpecifications");

            migrationBuilder.DropColumn(
                name: "Ordinal",
                table: "TeamDailyQuestionSpecifications");

            migrationBuilder.DropColumn(
                name: "SubCategory",
                table: "TeamDailyQuestionSpecifications");

            migrationBuilder.DropColumn(
                name: "TeamElement",
                table: "TeamDailyQuestionSpecifications");

            migrationBuilder.DropColumn(
                name: "ContentCultureId",
                table: "TeamDailyMicroActions");

            migrationBuilder.DropColumn(
                name: "Text",
                table: "TeamDailyMicroActions");

            migrationBuilder.RenameTable(
                name: "TeamDailyFeedback",
                schema: "public",
                newName: "TeamDailyFeedbacks");

            migrationBuilder.RenameColumn(
                name: "Text",
                table: "TeamDailyReasons",
                newName: "ReasonText");

            migrationBuilder.RenameColumn(
                name: "TeamDailyReasonSentimentType",
                table: "TeamDailyReasons",
                newName: "SentimentType");

            migrationBuilder.RenameColumn(
                name: "SpecificationId",
                table: "TeamDailyReasons",
                newName: "TeamDailyQuestionId");

            migrationBuilder.RenameColumn(
                name: "RequiresFreeTextFeedback",
                table: "TeamDailyReasons",
                newName: "RequiresFreeText");

            migrationBuilder.RenameColumn(
                name: "Variant",
                table: "TeamDailyQuestionSpecifications",
                newName: "VariantType");

            migrationBuilder.RenameColumn(
                name: "Text",
                table: "TeamDailyQuestions",
                newName: "QuestionText");

            migrationBuilder.RenameColumn(
                name: "ContentCultureId",
                table: "TeamDailyQuestions",
                newName: "TeamQuestionSubCategoryId");

            migrationBuilder.RenameIndex(
                name: "IX_TeamDailyFeedback_Unit_Submitted",
                table: "TeamDailyFeedbacks",
                newName: "IX_TeamDailyFeedbacks_Unit_Submitted");

            migrationBuilder.RenameIndex(
                name: "IX_TeamDailyFeedback_Reason",
                table: "TeamDailyFeedbacks",
                newName: "IX_TeamDailyFeedbacks_Reason");

            migrationBuilder.RenameIndex(
                name: "IX_TeamDailyFeedback_Question",
                table: "TeamDailyFeedbacks",
                newName: "IX_TeamDailyFeedbacks_TeamDailyQuestionId");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "TeamDailyReasons",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDel",
                table: "TeamDailyReasons",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<short>(
                name: "Position",
                table: "TeamDailyReasons",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<long>(
                name: "TeamQuestionAttributionTypeId",
                table: "TeamDailyReasons",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "TeamStateIndicatorId",
                table: "TeamDailyReasons",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AskNumber",
                table: "TeamDailyQuestionSpecifications",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "BaseTeamDailyQuestionId",
                table: "TeamDailyQuestionSpecifications",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EffectiveFromUtc",
                table: "TeamDailyQuestionSpecifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EffectiveToUtc",
                table: "TeamDailyQuestionSpecifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "TeamDailyQuestionSpecifications",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDel",
                table: "TeamDailyQuestionSpecifications",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresGoal",
                table: "TeamDailyQuestionSpecifications",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "RotationOrder",
                table: "TeamDailyQuestionSpecifications",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "TeamDailyQuestions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "CreatedBy",
                table: "TeamDailyQuestions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "TeamDailyQuestions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDel",
                table: "TeamDailyQuestions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "OriginType",
                table: "TeamDailyQuestions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "OwnerBusinessUnitId",
                table: "TeamDailyQuestions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "OwnerCompanyId",
                table: "TeamDailyQuestions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Body",
                table: "TeamDailyMicroActions",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "TeamDailyMicroActions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDel",
                table: "TeamDailyMicroActions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RoutesToManager",
                table: "TeamDailyMicroActions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "TeamDailyMicroActions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "TeamDailyQuestionDeliveryId",
                table: "TeamDailyFeedbacks",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddPrimaryKey(
                name: "PK_TeamDailyFeedbacks",
                table: "TeamDailyFeedbacks",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "TeamCustomQuestionSchedules",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BusinessUnitId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    RequestedByUserId = table.Column<int>(type: "integer", nullable: false),
                    TeamDailyQuestionId = table.Column<long>(type: "bigint", nullable: false),
                    AppliedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ScheduledDate = table.Column<DateOnly>(type: "date", nullable: false),
                    StatusType = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamCustomQuestionSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamCustomQuestionSchedules_BusinessUnits_BusinessUnitId",
                        column: x => x.BusinessUnitId,
                        principalTable: "BusinessUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamCustomQuestionSchedules_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TeamCustomQuestionSchedules_TeamDailyQuestions_TeamDailyQue~",
                        column: x => x.TeamDailyQuestionId,
                        principalTable: "TeamDailyQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TeamCustomQuestionSchedules_Users_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TeamGoals",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BusinessUnitId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: true),
                    DisplayOrder = table.Column<short>(type: "smallint", nullable: false),
                    GoalText = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsDel = table.Column<bool>(type: "boolean", nullable: false),
                    TargetDate = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamGoals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamGoals_BusinessUnits_BusinessUnitId",
                        column: x => x.BusinessUnitId,
                        principalTable: "BusinessUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeamQuestionAttributionTypes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    DisplayOrder = table.Column<short>(type: "smallint", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamQuestionAttributionTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TeamQuestionMainCategories",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DisplayOrder = table.Column<short>(type: "smallint", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsDel = table.Column<bool>(type: "boolean", nullable: false),
                    Name = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamQuestionMainCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TeamQuestionSamplingModes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    DisplayOrder = table.Column<short>(type: "smallint", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamQuestionSamplingModes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TeamStateIndicators",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DisplayOrder = table.Column<short>(type: "smallint", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsQuadrant = table.Column<bool>(type: "boolean", nullable: false),
                    Name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    QuadrantX = table.Column<short>(type: "smallint", nullable: true),
                    QuadrantY = table.Column<short>(type: "smallint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamStateIndicators", x => x.Id);
                    table.CheckConstraint("CK_TeamStateIndicators_Quadrant", "(\"IsQuadrant\" = false AND \"QuadrantX\" IS NULL AND \"QuadrantY\" IS NULL) OR (\"IsQuadrant\" = true  AND \"QuadrantX\" IS NOT NULL AND \"QuadrantY\" IS NOT NULL)");
                });

            migrationBuilder.CreateTable(
                name: "TeamDailyQuestionDeliveries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BusinessUnitId = table.Column<long>(type: "bigint", nullable: false),
                    TeamCustomQuestionScheduleId = table.Column<long>(type: "bigint", nullable: true),
                    TeamDailyQuestionId = table.Column<long>(type: "bigint", nullable: false),
                    TeamDailyQuestionSpecificationId = table.Column<long>(type: "bigint", nullable: true),
                    TeamGoalId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: true),
                    DeliveredQuestionText = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ScheduledDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SourceType = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamDailyQuestionDeliveries", x => x.Id);
                    table.CheckConstraint("CK_TeamDailyQuestionDeliveries_CustomSource", "(\"SourceType\" = 2 AND \"TeamCustomQuestionScheduleId\" IS NOT NULL) OR (\"SourceType\" <> 2 AND \"TeamCustomQuestionScheduleId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_TeamDailyQuestionDeliveries_BusinessUnits_BusinessUnitId",
                        column: x => x.BusinessUnitId,
                        principalTable: "BusinessUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamDailyQuestionDeliveries_TeamCustomQuestionSchedules_Tea~",
                        column: x => x.TeamCustomQuestionScheduleId,
                        principalTable: "TeamCustomQuestionSchedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TeamDailyQuestionDeliveries_TeamDailyQuestionSpecifications~",
                        column: x => x.TeamDailyQuestionSpecificationId,
                        principalTable: "TeamDailyQuestionSpecifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TeamDailyQuestionDeliveries_TeamDailyQuestions_TeamDailyQue~",
                        column: x => x.TeamDailyQuestionId,
                        principalTable: "TeamDailyQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TeamDailyQuestionDeliveries_TeamGoals_TeamGoalId",
                        column: x => x.TeamGoalId,
                        principalTable: "TeamGoals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "TeamQuestionSubCategories",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TeamQuestionMainCategoryId = table.Column<long>(type: "bigint", nullable: false),
                    TeamQuestionSamplingModeId = table.Column<long>(type: "bigint", nullable: false),
                    CadenceDays = table.Column<int>(type: "integer", nullable: true),
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DisplayOrder = table.Column<short>(type: "smallint", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsDel = table.Column<bool>(type: "boolean", nullable: false),
                    Name = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    SupportsGoalAnchor = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamQuestionSubCategories", x => x.Id);
                    table.CheckConstraint("CK_TeamQuestionSubCategories_Cadence", "(\"TeamQuestionSamplingModeId\" = 1 AND \"CadenceDays\" IS NULL) OR (\"TeamQuestionSamplingModeId\" <> 1 AND \"CadenceDays\" > 0)");
                    table.ForeignKey(
                        name: "FK_TeamQuestionSubCategories_TeamQuestionMainCategories_TeamQu~",
                        column: x => x.TeamQuestionMainCategoryId,
                        principalTable: "TeamQuestionMainCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TeamQuestionSubCategories_TeamQuestionSamplingModes_TeamQue~",
                        column: x => x.TeamQuestionSamplingModeId,
                        principalTable: "TeamQuestionSamplingModes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TeamDailyQuestionResponseReceipts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TeamDailyQuestionDeliveryId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    RespondedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamDailyQuestionResponseReceipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamDailyQuestionResponseReceipts_TeamDailyQuestionDeliveri~",
                        column: x => x.TeamDailyQuestionDeliveryId,
                        principalTable: "TeamDailyQuestionDeliveries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamDailyQuestionResponseReceipts_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "TeamQuestionAttributionTypes",
                columns: new[] { "Id", "Code", "DisplayOrder", "IsActive", "Name" },
                values: new object[,]
                {
                    { 1L, "Organisation", (short)1, true, "The organisation or the wider system" },
                    { 2L, "Manager", (short)2, true, "The manager" },
                    { 3L, "TeamOrSelf", (short)3, true, "The team or the person themselves" },
                    { 9L, "Other", (short)9, true, "Other" }
                });

            migrationBuilder.InsertData(
                table: "TeamQuestionMainCategories",
                columns: new[] { "Id", "Code", "Description", "DisplayOrder", "IsActive", "IsDel", "Name" },
                values: new object[,]
                {
                    { 1L, "DL", null, (short)1, true, false, "Direction and learning" },
                    { 2L, "DM", null, (short)2, true, false, "Design and mechanics" },
                    { 3L, "TB", null, (short)3, true, false, "What the team believes" }
                });

            migrationBuilder.InsertData(
                table: "TeamQuestionSamplingModes",
                columns: new[] { "Id", "Code", "Description", "DisplayOrder", "IsActive", "Name" },
                values: new object[,]
                {
                    { 1L, "Continuous", "Drifts quietly; sampled regularly to catch the drift.", (short)1, true, "Continuous" },
                    { 2L, "Periodic", "Tied to whatever review rhythm the team already runs.", (short)2, true, "Periodic" },
                    { 3L, "EventBased", "Asked on joiners, leavers and reorganisations, and once a quarter otherwise.", (short)3, true, "Event based" }
                });

            migrationBuilder.InsertData(
                table: "TeamStateIndicators",
                columns: new[] { "Id", "Code", "DisplayOrder", "IsActive", "IsQuadrant", "Name", "QuadrantX", "QuadrantY" },
                values: new object[,]
                {
                    { 1L, "CRUISE", (short)1, true, true, "Cruise", (short)0, (short)1 },
                    { 2L, "DRIFT", (short)2, true, true, "Drift", (short)-1, (short)0 },
                    { 3L, "HEADWINDS", (short)3, true, true, "Headwinds", (short)1, (short)0 },
                    { 4L, "STALL", (short)4, true, true, "Stall", (short)0, (short)-1 },
                    { 5L, "EXTERNAL", (short)5, true, false, "External", null, null },
                    { 6L, "NEUTRAL", (short)6, true, false, "Neutral", null, null }
                });

            migrationBuilder.InsertData(
                table: "TeamQuestionSubCategories",
                columns: new[] { "Id", "CadenceDays", "Code", "Description", "DisplayOrder", "IsActive", "IsDel", "Name", "SupportsGoalAnchor", "TeamQuestionMainCategoryId", "TeamQuestionSamplingModeId" },
                values: new object[,]
                {
                    { 1L, null, "GA", null, (short)1, true, false, "Goal Affinity", true, 1L, 1L },
                    { 2L, 30, "RS", null, (short)2, true, false, "Results", false, 1L, 2L },
                    { 3L, 30, "IL", null, (short)3, true, false, "Improvement Loop", false, 1L, 2L },
                    { 4L, 90, "ST", null, (short)4, true, false, "Structure", false, 2L, 3L },
                    { 5L, null, "CQ", null, (short)5, true, false, "Coordination Quality", false, 2L, 1L },
                    { 6L, null, "AC", null, (short)6, true, false, "Accountability", false, 3L, 1L },
                    { 7L, null, "PO", null, (short)7, true, false, "Potency", false, 3L, 1L },
                    { 8L, null, "BE", null, (short)8, true, false, "Belonging", false, 3L, 1L }
                });

            migrationBuilder.CreateIndex(
                name: "IX_TeamDailyReasons_StateIndicator",
                table: "TeamDailyReasons",
                column: "TeamStateIndicatorId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamDailyReasons_TeamQuestionAttributionTypeId",
                table: "TeamDailyReasons",
                column: "TeamQuestionAttributionTypeId");

            migrationBuilder.CreateIndex(
                name: "UX_TeamDailyReasons_Question_Sentiment_Position",
                table: "TeamDailyReasons",
                columns: new[] { "TeamDailyQuestionId", "SentimentType", "Position" },
                unique: true,
                filter: "\"IsDel\" = false");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TeamDailyReasons_Position",
                table: "TeamDailyReasons",
                sql: "\"Position\" BETWEEN 1 AND 4");

            migrationBuilder.CreateIndex(
                name: "IX_TeamDailyQuestionSpecifications_BaseTeamDailyQuestionId",
                table: "TeamDailyQuestionSpecifications",
                column: "BaseTeamDailyQuestionId");

            migrationBuilder.CreateIndex(
                name: "UX_TeamDailyQuestionSpecifications_Ask_Variant",
                table: "TeamDailyQuestionSpecifications",
                columns: new[] { "AskNumber", "VariantType" },
                unique: true,
                filter: "\"IsDel\" = false AND \"AskNumber\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_TeamDailyQuestionSpecifications_Question",
                table: "TeamDailyQuestionSpecifications",
                column: "TeamDailyQuestionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_TeamDailyQuestionSpecifications_RotationOrder",
                table: "TeamDailyQuestionSpecifications",
                column: "RotationOrder",
                unique: true,
                filter: "\"IsActive\" = true AND \"IsDel\" = false AND \"RotationOrder\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TeamDailyQuestionSpecifications_Variant",
                table: "TeamDailyQuestionSpecifications",
                sql: "(\"VariantType\" = 1 AND \"BaseTeamDailyQuestionId\" IS NULL AND \"RequiresGoal\" = false) OR (\"VariantType\" = 2 AND \"BaseTeamDailyQuestionId\" IS NOT NULL AND \"RequiresGoal\" = true)");

            migrationBuilder.CreateIndex(
                name: "IX_TeamDailyQuestions_OwnerBusinessUnitId",
                table: "TeamDailyQuestions",
                column: "OwnerBusinessUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamDailyQuestions_OwnerCompany",
                table: "TeamDailyQuestions",
                column: "OwnerCompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamDailyQuestions_SubCategory_Active",
                table: "TeamDailyQuestions",
                column: "TeamQuestionSubCategoryId",
                filter: "\"IsActive\" = true AND \"IsDel\" = false");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TeamDailyQuestions_CustomOwnership",
                table: "TeamDailyQuestions",
                sql: "(\"OriginType\" = 2 AND \"OwnerCompanyId\" IS NOT NULL) OR (\"OriginType\" <> 2 AND \"OwnerCompanyId\" IS NULL)");

            migrationBuilder.CreateIndex(
                name: "UX_TeamDailyMicroActions_Reason",
                table: "TeamDailyMicroActions",
                column: "TeamDailyReasonId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamDailyFeedbacks_Delivery",
                table: "TeamDailyFeedbacks",
                column: "TeamDailyQuestionDeliveryId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamCustomQuestionSchedules_CompanyId",
                table: "TeamCustomQuestionSchedules",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamCustomQuestionSchedules_Date_Status",
                table: "TeamCustomQuestionSchedules",
                columns: new[] { "ScheduledDate", "StatusType" });

            migrationBuilder.CreateIndex(
                name: "IX_TeamCustomQuestionSchedules_RequestedByUserId",
                table: "TeamCustomQuestionSchedules",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamCustomQuestionSchedules_TeamDailyQuestionId",
                table: "TeamCustomQuestionSchedules",
                column: "TeamDailyQuestionId");

            migrationBuilder.CreateIndex(
                name: "UX_TeamCustomQuestionSchedules_PendingPerUnitDate",
                table: "TeamCustomQuestionSchedules",
                columns: new[] { "BusinessUnitId", "ScheduledDate" },
                unique: true,
                filter: "\"StatusType\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_TeamDailyQuestionDeliveries_Date",
                table: "TeamDailyQuestionDeliveries",
                column: "ScheduledDate");

            migrationBuilder.CreateIndex(
                name: "IX_TeamDailyQuestionDeliveries_TeamCustomQuestionScheduleId",
                table: "TeamDailyQuestionDeliveries",
                column: "TeamCustomQuestionScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamDailyQuestionDeliveries_TeamDailyQuestionId",
                table: "TeamDailyQuestionDeliveries",
                column: "TeamDailyQuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamDailyQuestionDeliveries_TeamDailyQuestionSpecificationId",
                table: "TeamDailyQuestionDeliveries",
                column: "TeamDailyQuestionSpecificationId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamDailyQuestionDeliveries_TeamGoalId",
                table: "TeamDailyQuestionDeliveries",
                column: "TeamGoalId");

            migrationBuilder.CreateIndex(
                name: "UX_TeamDailyQuestionDeliveries_Unit_Date",
                table: "TeamDailyQuestionDeliveries",
                columns: new[] { "BusinessUnitId", "ScheduledDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamDailyQuestionResponseReceipts_UserId",
                table: "TeamDailyQuestionResponseReceipts",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "UX_TeamDailyQuestionResponseReceipts_Delivery_User",
                table: "TeamDailyQuestionResponseReceipts",
                columns: new[] { "TeamDailyQuestionDeliveryId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_TeamGoals_Unit_Order_Active",
                table: "TeamGoals",
                columns: new[] { "BusinessUnitId", "DisplayOrder" },
                unique: true,
                filter: "\"IsActive\" = true AND \"IsDel\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_TeamQuestionAttributionTypes_Code",
                table: "TeamQuestionAttributionTypes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamQuestionMainCategories_Code",
                table: "TeamQuestionMainCategories",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamQuestionMainCategories_Name",
                table: "TeamQuestionMainCategories",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamQuestionSamplingModes_Code",
                table: "TeamQuestionSamplingModes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamQuestionSubCategories_Code",
                table: "TeamQuestionSubCategories",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamQuestionSubCategories_Main_Order",
                table: "TeamQuestionSubCategories",
                columns: new[] { "TeamQuestionMainCategoryId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_TeamQuestionSubCategories_TeamQuestionSamplingModeId",
                table: "TeamQuestionSubCategories",
                column: "TeamQuestionSamplingModeId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamStateIndicators_Code",
                table: "TeamStateIndicators",
                column: "Code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_TeamDailyFeedbacks_BusinessUnits_BusinessUnitId",
                table: "TeamDailyFeedbacks",
                column: "BusinessUnitId",
                principalTable: "BusinessUnits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TeamDailyFeedbacks_TeamDailyQuestionDeliveries_TeamDailyQue~",
                table: "TeamDailyFeedbacks",
                column: "TeamDailyQuestionDeliveryId",
                principalTable: "TeamDailyQuestionDeliveries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TeamDailyFeedbacks_TeamDailyQuestions_TeamDailyQuestionId",
                table: "TeamDailyFeedbacks",
                column: "TeamDailyQuestionId",
                principalTable: "TeamDailyQuestions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TeamDailyFeedbacks_TeamDailyReasons_TeamDailyReasonId",
                table: "TeamDailyFeedbacks",
                column: "TeamDailyReasonId",
                principalTable: "TeamDailyReasons",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TeamDailyQuestions_BusinessUnits_OwnerBusinessUnitId",
                table: "TeamDailyQuestions",
                column: "OwnerBusinessUnitId",
                principalTable: "BusinessUnits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TeamDailyQuestions_Companies_OwnerCompanyId",
                table: "TeamDailyQuestions",
                column: "OwnerCompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TeamDailyQuestions_TeamQuestionSubCategories_TeamQuestionSu~",
                table: "TeamDailyQuestions",
                column: "TeamQuestionSubCategoryId",
                principalTable: "TeamQuestionSubCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TeamDailyQuestionSpecifications_TeamDailyQuestions_BaseTeam~",
                table: "TeamDailyQuestionSpecifications",
                column: "BaseTeamDailyQuestionId",
                principalTable: "TeamDailyQuestions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TeamDailyQuestionSpecifications_TeamDailyQuestions_TeamDail~",
                table: "TeamDailyQuestionSpecifications",
                column: "TeamDailyQuestionId",
                principalTable: "TeamDailyQuestions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TeamDailyReasons_TeamDailyQuestions_TeamDailyQuestionId",
                table: "TeamDailyReasons",
                column: "TeamDailyQuestionId",
                principalTable: "TeamDailyQuestions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TeamDailyReasons_TeamQuestionAttributionTypes_TeamQuestionA~",
                table: "TeamDailyReasons",
                column: "TeamQuestionAttributionTypeId",
                principalTable: "TeamQuestionAttributionTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TeamDailyReasons_TeamStateIndicators_TeamStateIndicatorId",
                table: "TeamDailyReasons",
                column: "TeamStateIndicatorId",
                principalTable: "TeamStateIndicators",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
