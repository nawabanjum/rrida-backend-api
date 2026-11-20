using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace CloverBackoffice.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamQuestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TeamGoals",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BusinessUnitId = table.Column<long>(type: "bigint", nullable: false),
                    GoalText = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    DisplayOrder = table.Column<short>(type: "smallint", nullable: false),
                    TargetDate = table.Column<DateOnly>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsDel = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: true)
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
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    DisplayOrder = table.Column<short>(type: "smallint", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
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
                    Name = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DisplayOrder = table.Column<short>(type: "smallint", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsDel = table.Column<bool>(type: "boolean", nullable: false)
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
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    DisplayOrder = table.Column<short>(type: "smallint", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
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
                    Name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    IsQuadrant = table.Column<bool>(type: "boolean", nullable: false),
                    QuadrantX = table.Column<short>(type: "smallint", nullable: true),
                    QuadrantY = table.Column<short>(type: "smallint", nullable: true),
                    DisplayOrder = table.Column<short>(type: "smallint", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamStateIndicators", x => x.Id);
                    table.CheckConstraint("CK_TeamStateIndicators_Quadrant", "(\"IsQuadrant\" = false AND \"QuadrantX\" IS NULL AND \"QuadrantY\" IS NULL) OR (\"IsQuadrant\" = true  AND \"QuadrantX\" IS NOT NULL AND \"QuadrantY\" IS NOT NULL)");
                });

            migrationBuilder.CreateTable(
                name: "TeamQuestionSubCategories",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TeamQuestionMainCategoryId = table.Column<long>(type: "bigint", nullable: false),
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TeamQuestionSamplingModeId = table.Column<long>(type: "bigint", nullable: false),
                    CadenceDays = table.Column<int>(type: "integer", nullable: true),
                    SupportsGoalAnchor = table.Column<bool>(type: "boolean", nullable: false),
                    DisplayOrder = table.Column<short>(type: "smallint", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsDel = table.Column<bool>(type: "boolean", nullable: false)
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
                name: "TeamDailyQuestions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TeamQuestionSubCategoryId = table.Column<long>(type: "bigint", nullable: false),
                    QuestionText = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    OriginType = table.Column<int>(type: "integer", nullable: false),
                    OwnerCompanyId = table.Column<long>(type: "bigint", nullable: true),
                    OwnerBusinessUnitId = table.Column<long>(type: "bigint", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsDel = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamDailyQuestions", x => x.Id);
                    table.CheckConstraint("CK_TeamDailyQuestions_CustomOwnership", "(\"OriginType\" = 2 AND \"OwnerCompanyId\" IS NOT NULL) OR (\"OriginType\" <> 2 AND \"OwnerCompanyId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_TeamDailyQuestions_BusinessUnits_OwnerBusinessUnitId",
                        column: x => x.OwnerBusinessUnitId,
                        principalTable: "BusinessUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TeamDailyQuestions_Companies_OwnerCompanyId",
                        column: x => x.OwnerCompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TeamDailyQuestions_TeamQuestionSubCategories_TeamQuestionSu~",
                        column: x => x.TeamQuestionSubCategoryId,
                        principalTable: "TeamQuestionSubCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TeamCustomQuestionSchedules",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BusinessUnitId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    ScheduledDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TeamDailyQuestionId = table.Column<long>(type: "bigint", nullable: false),
                    StatusType = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RequestedByUserId = table.Column<int>(type: "integer", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AppliedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
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
                name: "TeamDailyQuestionSpecifications",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TeamDailyQuestionId = table.Column<long>(type: "bigint", nullable: false),
                    AskNumber = table.Column<int>(type: "integer", nullable: true),
                    VariantType = table.Column<int>(type: "integer", nullable: false),
                    BaseTeamDailyQuestionId = table.Column<long>(type: "bigint", nullable: true),
                    RequiresGoal = table.Column<bool>(type: "boolean", nullable: false),
                    RotationOrder = table.Column<int>(type: "integer", nullable: true),
                    EffectiveFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EffectiveToUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsDel = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamDailyQuestionSpecifications", x => x.Id);
                    table.CheckConstraint("CK_TeamDailyQuestionSpecifications_Variant", "(\"VariantType\" = 1 AND \"BaseTeamDailyQuestionId\" IS NULL AND \"RequiresGoal\" = false) OR (\"VariantType\" = 2 AND \"BaseTeamDailyQuestionId\" IS NOT NULL AND \"RequiresGoal\" = true)");
                    table.ForeignKey(
                        name: "FK_TeamDailyQuestionSpecifications_TeamDailyQuestions_BaseTeam~",
                        column: x => x.BaseTeamDailyQuestionId,
                        principalTable: "TeamDailyQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TeamDailyQuestionSpecifications_TeamDailyQuestions_TeamDail~",
                        column: x => x.TeamDailyQuestionId,
                        principalTable: "TeamDailyQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeamDailyReasons",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TeamDailyQuestionId = table.Column<long>(type: "bigint", nullable: false),
                    SentimentType = table.Column<int>(type: "integer", nullable: false),
                    TeamQuestionAttributionTypeId = table.Column<long>(type: "bigint", nullable: true),
                    TeamStateIndicatorId = table.Column<long>(type: "bigint", nullable: true),
                    Position = table.Column<short>(type: "smallint", nullable: false),
                    ReasonText = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    RequiresFreeText = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsDel = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamDailyReasons", x => x.Id);
                    table.CheckConstraint("CK_TeamDailyReasons_Position", "\"Position\" BETWEEN 1 AND 4");
                    table.ForeignKey(
                        name: "FK_TeamDailyReasons_TeamDailyQuestions_TeamDailyQuestionId",
                        column: x => x.TeamDailyQuestionId,
                        principalTable: "TeamDailyQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamDailyReasons_TeamQuestionAttributionTypes_TeamQuestionA~",
                        column: x => x.TeamQuestionAttributionTypeId,
                        principalTable: "TeamQuestionAttributionTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TeamDailyReasons_TeamStateIndicators_TeamStateIndicatorId",
                        column: x => x.TeamStateIndicatorId,
                        principalTable: "TeamStateIndicators",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TeamDailyQuestionDeliveries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BusinessUnitId = table.Column<long>(type: "bigint", nullable: false),
                    ScheduledDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TeamDailyQuestionId = table.Column<long>(type: "bigint", nullable: false),
                    SourceType = table.Column<int>(type: "integer", nullable: false),
                    TeamDailyQuestionSpecificationId = table.Column<long>(type: "bigint", nullable: true),
                    TeamCustomQuestionScheduleId = table.Column<long>(type: "bigint", nullable: true),
                    TeamGoalId = table.Column<long>(type: "bigint", nullable: true),
                    DeliveredQuestionText = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: true)
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
                name: "TeamDailyMicroActions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TeamDailyReasonId = table.Column<long>(type: "bigint", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    RoutesToManager = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsDel = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamDailyMicroActions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamDailyMicroActions_TeamDailyReasons_TeamDailyReasonId",
                        column: x => x.TeamDailyReasonId,
                        principalTable: "TeamDailyReasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeamDailyFeedbacks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TeamDailyQuestionDeliveryId = table.Column<long>(type: "bigint", nullable: false),
                    BusinessUnitId = table.Column<long>(type: "bigint", nullable: false),
                    TeamDailyQuestionId = table.Column<long>(type: "bigint", nullable: false),
                    TeamDailyReasonId = table.Column<long>(type: "bigint", nullable: false),
                    SentimentType = table.Column<int>(type: "integer", nullable: false),
                    FreeText = table.Column<string>(type: "character varying(896)", maxLength: 896, nullable: true),
                    SubmissionDateTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamDailyFeedbacks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamDailyFeedbacks_BusinessUnits_BusinessUnitId",
                        column: x => x.BusinessUnitId,
                        principalTable: "BusinessUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TeamDailyFeedbacks_TeamDailyQuestionDeliveries_TeamDailyQue~",
                        column: x => x.TeamDailyQuestionDeliveryId,
                        principalTable: "TeamDailyQuestionDeliveries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TeamDailyFeedbacks_TeamDailyQuestions_TeamDailyQuestionId",
                        column: x => x.TeamDailyQuestionId,
                        principalTable: "TeamDailyQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TeamDailyFeedbacks_TeamDailyReasons_TeamDailyReasonId",
                        column: x => x.TeamDailyReasonId,
                        principalTable: "TeamDailyReasons",
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
                name: "IX_TeamDailyFeedbacks_Delivery",
                table: "TeamDailyFeedbacks",
                column: "TeamDailyQuestionDeliveryId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamDailyFeedbacks_Reason",
                table: "TeamDailyFeedbacks",
                column: "TeamDailyReasonId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamDailyFeedbacks_TeamDailyQuestionId",
                table: "TeamDailyFeedbacks",
                column: "TeamDailyQuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamDailyFeedbacks_Unit_Submitted",
                table: "TeamDailyFeedbacks",
                columns: new[] { "BusinessUnitId", "SubmissionDateTime" });

            migrationBuilder.CreateIndex(
                name: "UX_TeamDailyMicroActions_Reason",
                table: "TeamDailyMicroActions",
                column: "TeamDailyReasonId",
                unique: true);

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TeamDailyFeedbacks");

            migrationBuilder.DropTable(
                name: "TeamDailyMicroActions");

            migrationBuilder.DropTable(
                name: "TeamDailyQuestionResponseReceipts");

            migrationBuilder.DropTable(
                name: "TeamDailyReasons");

            migrationBuilder.DropTable(
                name: "TeamDailyQuestionDeliveries");

            migrationBuilder.DropTable(
                name: "TeamQuestionAttributionTypes");

            migrationBuilder.DropTable(
                name: "TeamStateIndicators");

            migrationBuilder.DropTable(
                name: "TeamCustomQuestionSchedules");

            migrationBuilder.DropTable(
                name: "TeamDailyQuestionSpecifications");

            migrationBuilder.DropTable(
                name: "TeamGoals");

            migrationBuilder.DropTable(
                name: "TeamDailyQuestions");

            migrationBuilder.DropTable(
                name: "TeamQuestionSubCategories");

            migrationBuilder.DropTable(
                name: "TeamQuestionMainCategories");

            migrationBuilder.DropTable(
                name: "TeamQuestionSamplingModes");

        }
    }
}
