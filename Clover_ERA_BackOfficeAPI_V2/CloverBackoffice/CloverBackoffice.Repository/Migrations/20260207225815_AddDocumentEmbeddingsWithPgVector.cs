using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Pgvector;

#nullable disable

namespace CloverBackoffice.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentEmbeddingsWithPgVector : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "public");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "ActionHubs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UnitId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    ReportId = table.Column<long>(type: "bigint", nullable: true),
                    ActionTitle = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: false),
                    IsDel = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActionHubs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    LogId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: true),
                    Endpoint = table.Column<string>(type: "text", nullable: false),
                    HttpMethod = table.Column<string>(type: "text", nullable: false),
                    RequestData = table.Column<string>(type: "text", nullable: false),
                    ResponseData = table.Column<string>(type: "text", nullable: false),
                    ActionType = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.LogId);
                });

            migrationBuilder.CreateTable(
                name: "BatchJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OpenAIJobId = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    RequestType = table.Column<string>(type: "text", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    InputFileUrl = table.Column<string>(type: "text", nullable: true),
                    OutputFileUrl = table.Column<string>(type: "text", nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    MetadataJson = table.Column<string>(type: "text", nullable: true),
                    BusinessUnitId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BatchJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CompanyTeams",
                columns: table => new
                {
                    TeamId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TeamName = table.Column<string>(type: "text", nullable: false),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    UnitId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: false),
                    ModifiedBy = table.Column<int>(type: "integer", nullable: true),
                    OrderBy = table.Column<int>(type: "integer", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    ParentId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyTeams", x => x.TeamId);
                });

            migrationBuilder.CreateTable(
                name: "Contacts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmailAddress = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    FirstName = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    LastName = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contacts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContentCultures",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CultureName = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentCultures", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CurrentRelationshipStatuses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CurrentRelationshipStatuses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DailyFeedbackReportRowDtos",
                columns: table => new
                {
                    FeedbackId = table.Column<long>(type: "bigint", nullable: false),
                    SubmissionDateTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SentimentType = table.Column<int>(type: "integer", nullable: false),
                    FreeText = table.Column<string>(type: "text", nullable: true),
                    QuestionId = table.Column<long>(type: "bigint", nullable: true),
                    QuestionText = table.Column<string>(type: "text", nullable: true),
                    ReasonId = table.Column<long>(type: "bigint", nullable: true),
                    ReasonText = table.Column<string>(type: "text", nullable: true),
                    RequiresFreeText = table.Column<bool>(type: "boolean", nullable: true),
                    SpecificationId = table.Column<long>(type: "bigint", nullable: true),
                    SpecificationOrdinal = table.Column<int>(type: "integer", nullable: true),
                    SpecificationGroupId = table.Column<long>(type: "bigint", nullable: true),
                    SpecificationCloverCategory = table.Column<int>(type: "integer", nullable: true),
                    BusinessUnitId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                });

            migrationBuilder.CreateTable(
                name: "DailyQuestionSetGroups",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DailyQuestionSetType = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyQuestionSetGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DocumentEmbeddings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    Embedding = table.Column<Vector>(type: "vector(1536)", nullable: false),
                    SourceType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentEmbeddings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FeedbackAnalysisResults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AnalysisDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SubmissionDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CategoryName = table.Column<string>(type: "text", nullable: false),
                    SadPercentage = table.Column<double>(type: "double precision", nullable: false),
                    HappyPercentage = table.Column<double>(type: "double precision", nullable: false),
                    IndifferentPercentage = table.Column<double>(type: "double precision", nullable: false),
                    AngryPercentage = table.Column<double>(type: "double precision", nullable: false),
                    BusinessUnitId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedbackAnalysisResults", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FeedbackDataResultDtos",
                columns: table => new
                {
                    FeedbackId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SubmissionDateTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BusinessUnitId = table.Column<int>(type: "integer", nullable: false),
                    BusinessUnitName = table.Column<string>(type: "text", nullable: false),
                    CompanyName = table.Column<string>(type: "text", nullable: false),
                    QuestionId = table.Column<int>(type: "integer", nullable: true),
                    QuestionText = table.Column<string>(type: "text", nullable: false),
                    CategoryId = table.Column<int>(type: "integer", nullable: true),
                    Category = table.Column<string>(type: "text", nullable: false),
                    ReasonId = table.Column<int>(type: "integer", nullable: true),
                    ReasonText = table.Column<string>(type: "text", nullable: false),
                    SentimentTypeId = table.Column<int>(type: "integer", nullable: false),
                    SentimentType = table.Column<string>(type: "text", nullable: false),
                    FreeText = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedbackDataResultDtos", x => x.FeedbackId);
                });

            migrationBuilder.CreateTable(
                name: "Industries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Industries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LLMLogs",
                columns: table => new
                {
                    LogId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UnitId = table.Column<int>(type: "integer", nullable: false),
                    FromDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ToDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PromptTokens = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Endpoint = table.Column<string>(type: "text", nullable: false),
                    HttpMethod = table.Column<string>(type: "text", nullable: false),
                    TokenCount = table.Column<int>(type: "integer", nullable: false),
                    RequestData = table.Column<string>(type: "text", nullable: false),
                    ResponseData = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LLMLogs", x => x.LogId);
                });

            migrationBuilder.CreateTable(
                name: "LLMPrompts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    InitialPrompt = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LLMPrompts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LLMReports",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FromDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ToDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    WorksheetFile = table.Column<string>(type: "text", nullable: true),
                    UnitId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LLMReports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    NotificationId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UnitId = table.Column<int>(type: "integer", nullable: false),
                    BussinessUnitId = table.Column<int>(type: "integer", nullable: false),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.NotificationId);
                });

            migrationBuilder.CreateTable(
                name: "NumberOfEmployees",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NumberOfEmployees", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Opportunities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompanyName = table.Column<string>(type: "text", nullable: false),
                    CompanyWebsite = table.Column<string>(type: "text", nullable: false),
                    PrimaryContactName = table.Column<string>(type: "text", nullable: false),
                    PrimaryContactTitle = table.Column<string>(type: "text", nullable: false),
                    PrimaryContactEmail = table.Column<string>(type: "text", nullable: false),
                    PrimaryContactPhone = table.Column<string>(type: "text", nullable: false),
                    Industryid = table.Column<int>(type: "integer", nullable: false),
                    NumberOfEmployeesid = table.Column<int>(type: "integer", nullable: false),
                    CompanyLocation = table.Column<string>(type: "text", nullable: false),
                    stageid = table.Column<int>(type: "integer", nullable: false),
                    ExpectedCloseDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EstimatedDealValue = table.Column<decimal>(type: "numeric", nullable: false),
                    EstimatedRevenue = table.Column<decimal>(type: "numeric", nullable: false),
                    EstimatedTeams = table.Column<int>(type: "integer", nullable: false),
                    BusinessPainPoints = table.Column<string>(type: "text", nullable: false),
                    CurrentRelationshipStatusid = table.Column<int>(type: "integer", nullable: false),
                    AdditionalNotes = table.Column<string>(type: "text", nullable: false),
                    AcceptTerms1 = table.Column<bool>(type: "boolean", nullable: false),
                    AcceptTerms2 = table.Column<bool>(type: "boolean", nullable: false),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    IsDel = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: false),
                    UpdatedBy = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Opportunities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OpportunityTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpportunityTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PromptTypes",
                columns: table => new
                {
                    PromptTypeId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PromptTypeTitle = table.Column<string>(type: "text", nullable: false),
                    UnitId = table.Column<int>(type: "integer", nullable: false),
                    OrganizationId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromptTypes", x => x.PromptTypeId);
                });

            migrationBuilder.CreateTable(
                name: "ResetPasswordLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    ResetLink = table.Column<string>(type: "text", nullable: false),
                    IsLinkUsed = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiryDateTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDel = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResetPasswordLinks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemModules",
                columns: table => new
                {
                    ModuleID = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ModuleName = table.Column<string>(type: "text", nullable: false),
                    FriendlyName = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemModules", x => x.ModuleID);
                });

            migrationBuilder.CreateTable(
                name: "TopActions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UnitId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    ReportId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TopActions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TopRecommendations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UnitId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Recommendation = table.Column<string>(type: "text", nullable: false),
                    ReportId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TopRecommendations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TopThemes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UnitId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Theme = table.Column<string>(type: "text", nullable: false),
                    ReportId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TopThemes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    IsAdmin = table.Column<bool>(type: "boolean", nullable: false),
                    UnitId = table.Column<int>(type: "integer", nullable: false),
                    OrganizationId = table.Column<int>(type: "integer", nullable: true),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserTeams",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    UnitId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsUnAssigned = table.Column<bool>(type: "boolean", nullable: false),
                    IsReadOnly = table.Column<bool>(type: "boolean", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserTeams", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserTokens",
                columns: table => new
                {
                    AuthId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Token = table.Column<string>(type: "text", nullable: false),
                    ExpiryDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserTokens", x => x.AuthId);
                });

            migrationBuilder.CreateTable(
                name: "ActionEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DateAddedToAction = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ScheduledEventActivity = table.Column<string>(type: "text", nullable: false),
                    DateImplemented = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Feedback = table.Column<string>(type: "text", nullable: false),
                    Attachment = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: false),
                    IsDel = table.Column<bool>(type: "boolean", nullable: false),
                    ActionHubId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActionEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActionEvents_ActionHubs_ActionHubId",
                        column: x => x.ActionHubId,
                        principalTable: "ActionHubs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EngagementInsights",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BusinessUnitId = table.Column<long>(type: "bigint", nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PeriodStart = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PeriodEnd = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OverallScore = table.Column<decimal>(type: "numeric", nullable: true),
                    CategoryScoresJson = table.Column<string>(type: "text", nullable: false),
                    TopThemesJson = table.Column<string>(type: "text", nullable: false),
                    RecommendationsJson = table.Column<string>(type: "text", nullable: false),
                    StatisticalSummaryJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    BatchJobsId = table.Column<Guid>(type: "uuid", nullable: false),
                    TopActionsJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EngagementInsights", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EngagementInsights_BatchJobs_BatchJobsId",
                        column: x => x.BatchJobsId,
                        principalTable: "BatchJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Companies",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyCode = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    CompanyShortCode = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    CountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    DomainName = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    Name = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    NameInvariant = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    RegistrationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ContactId = table.Column<int>(type: "integer", nullable: false),
                    IsPartner = table.Column<bool>(type: "boolean", nullable: false),
                    ReferredBy = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Companies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Companies_Contacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DailyQuestions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ContentCultureId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailyQuestions_ContentCultures_ContentCultureId",
                        column: x => x.ContentCultureId,
                        principalTable: "ContentCultures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DailyQuestionSets",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    VersionNumber = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    ContentCultureId = table.Column<long>(type: "bigint", nullable: false),
                    DailyQuestionSetGroupId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyQuestionSets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailyQuestionSets_ContentCultures_ContentCultureId",
                        column: x => x.ContentCultureId,
                        principalTable: "ContentCultures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DailyQuestionSets_DailyQuestionSetGroups_DailyQuestionSetGr~",
                        column: x => x.DailyQuestionSetGroupId,
                        principalTable: "DailyQuestionSetGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssignUserRights",
                columns: table => new
                {
                    AssignRightId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserGroupId = table.Column<int>(type: "integer", nullable: false),
                    SystemModuleId = table.Column<int>(type: "integer", nullable: false),
                    PromptTypeId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssignUserRights", x => x.AssignRightId);
                    table.ForeignKey(
                        name: "FK_AssignUserRights_PromptTypes_PromptTypeId",
                        column: x => x.PromptTypeId,
                        principalTable: "PromptTypes",
                        principalColumn: "PromptTypeId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssignUserRights_SystemModules_SystemModuleId",
                        column: x => x.SystemModuleId,
                        principalTable: "SystemModules",
                        principalColumn: "ModuleID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssignUserRights_UserGroups_UserGroupId",
                        column: x => x.UserGroupId,
                        principalTable: "UserGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    Username = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    CreatedDate = table.Column<string>(type: "text", nullable: true),
                    ModifiedDate = table.Column<string>(type: "text", nullable: true),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    ModifiedBy = table.Column<string>(type: "text", nullable: true),
                    IsDel = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    UserGroupId = table.Column<int>(type: "integer", nullable: false),
                    LastLoginDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Password = table.Column<string>(type: "text", nullable: true),
                    IsVerified = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    IsEntraId = table.Column<bool>(type: "boolean", nullable: false),
                    IsFirstTimePasswordChange = table.Column<bool>(type: "boolean", nullable: true),
                    CompanyId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_Users_UserGroups_UserGroupId",
                        column: x => x.UserGroupId,
                        principalTable: "UserGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BusinessUnits",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BusinessUnitCode = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    BusinessUnitShortCode = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Name = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    NameInvariant = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    RegistrationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    ContactId = table.Column<int>(type: "integer", nullable: false),
                    ParentId = table.Column<int>(type: "integer", nullable: true),
                    IsDel = table.Column<bool>(type: "boolean", nullable: true),
                    ActiveDailyQuestionSetGroupId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessUnits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BusinessUnits_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BusinessUnits_Contacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BusinessUnits_DailyQuestionSetGroups_ActiveDailyQuestionSet~",
                        column: x => x.ActiveDailyQuestionSetGroupId,
                        principalTable: "DailyQuestionSetGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DailyQuestionSpecifications",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DailyQuestionSetGroupId = table.Column<long>(type: "bigint", nullable: false),
                    DailyQuestionSetId = table.Column<long>(type: "bigint", nullable: false),
                    CloverCategory = table.Column<int>(type: "integer", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    ContentCultureId = table.Column<long>(type: "bigint", nullable: false),
                    QuestionId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyQuestionSpecifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailyQuestionSpecifications_ContentCultures_ContentCultureId",
                        column: x => x.ContentCultureId,
                        principalTable: "ContentCultures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DailyQuestionSpecifications_DailyQuestionSetGroups_DailyQue~",
                        column: x => x.DailyQuestionSetGroupId,
                        principalTable: "DailyQuestionSetGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DailyQuestionSpecifications_DailyQuestionSets_DailyQuestion~",
                        column: x => x.DailyQuestionSetId,
                        principalSchema: "public",
                        principalTable: "DailyQuestionSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DailyQuestionSpecifications_DailyQuestions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "DailyQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CompanyLinks",
                columns: table => new
                {
                    CompanyLinkId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    ParentId = table.Column<int>(type: "integer", nullable: true),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    UnitId = table.Column<int>(type: "integer", nullable: true),
                    ExpiryDate = table.Column<string>(type: "text", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    BussinessUnitId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyLinks", x => x.CompanyLinkId);
                    table.ForeignKey(
                        name: "FK_CompanyLinks_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DailyReasons",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DailyReasonSentimentType = table.Column<int>(type: "integer", nullable: false),
                    Text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    RequiresFreeTextFeedback = table.Column<bool>(type: "boolean", nullable: false),
                    SpecificationId = table.Column<long>(type: "bigint", nullable: false),
                    ContentCultureId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyReasons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailyReasons_ContentCultures_ContentCultureId",
                        column: x => x.ContentCultureId,
                        principalTable: "ContentCultures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DailyReasons_DailyQuestionSpecifications_SpecificationId",
                        column: x => x.SpecificationId,
                        principalTable: "DailyQuestionSpecifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DailyFeedback",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SubmissionDateTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BusinessUnitId = table.Column<long>(type: "bigint", nullable: false),
                    SentimentType = table.Column<int>(type: "integer", nullable: false),
                    DailyQuestionId = table.Column<long>(type: "bigint", nullable: false),
                    DailyReasonId = table.Column<long>(type: "bigint", nullable: false),
                    FreeText = table.Column<string>(type: "character varying(896)", maxLength: 896, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyFeedback", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailyFeedback_BusinessUnits_BusinessUnitId",
                        column: x => x.BusinessUnitId,
                        principalTable: "BusinessUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DailyFeedback_DailyQuestions_DailyQuestionId",
                        column: x => x.DailyQuestionId,
                        principalTable: "DailyQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DailyFeedback_DailyReasons_DailyReasonId",
                        column: x => x.DailyReasonId,
                        principalTable: "DailyReasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DailyMicroActions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ContentCultureId = table.Column<long>(type: "bigint", nullable: false),
                    ReasonId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyMicroActions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailyMicroActions_ContentCultures_ContentCultureId",
                        column: x => x.ContentCultureId,
                        principalTable: "ContentCultures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DailyMicroActions_DailyReasons_ReasonId",
                        column: x => x.ReasonId,
                        principalTable: "DailyReasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActionEvents_ActionHubId",
                table: "ActionEvents",
                column: "ActionHubId");

            migrationBuilder.CreateIndex(
                name: "IX_AssignUserRights_PromptTypeId",
                table: "AssignUserRights",
                column: "PromptTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_AssignUserRights_SystemModuleId",
                table: "AssignUserRights",
                column: "SystemModuleId");

            migrationBuilder.CreateIndex(
                name: "IX_AssignUserRights_UserGroupId",
                table: "AssignUserRights",
                column: "UserGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessUnits_ActiveDailyQuestionSetGroupId",
                table: "BusinessUnits",
                column: "ActiveDailyQuestionSetGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessUnits_CompanyId",
                table: "BusinessUnits",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessUnits_ContactId",
                table: "BusinessUnits",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_Companies_ContactId",
                table: "Companies",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyLinks_UserId",
                table: "CompanyLinks",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyFeedback_BusinessUnitId",
                schema: "public",
                table: "DailyFeedback",
                column: "BusinessUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyFeedback_DailyQuestionId",
                schema: "public",
                table: "DailyFeedback",
                column: "DailyQuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyFeedback_DailyReasonId",
                schema: "public",
                table: "DailyFeedback",
                column: "DailyReasonId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyMicroActions_ContentCultureId",
                table: "DailyMicroActions",
                column: "ContentCultureId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyMicroActions_ReasonId",
                table: "DailyMicroActions",
                column: "ReasonId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyQuestions_ContentCultureId",
                table: "DailyQuestions",
                column: "ContentCultureId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyQuestionSets_ContentCultureId",
                schema: "public",
                table: "DailyQuestionSets",
                column: "ContentCultureId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyQuestionSets_DailyQuestionSetGroupId",
                schema: "public",
                table: "DailyQuestionSets",
                column: "DailyQuestionSetGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyQuestionSpecifications_ContentCultureId",
                table: "DailyQuestionSpecifications",
                column: "ContentCultureId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyQuestionSpecifications_DailyQuestionSetGroupId",
                table: "DailyQuestionSpecifications",
                column: "DailyQuestionSetGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyQuestionSpecifications_DailyQuestionSetId",
                table: "DailyQuestionSpecifications",
                column: "DailyQuestionSetId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyQuestionSpecifications_QuestionId",
                table: "DailyQuestionSpecifications",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyReasons_ContentCultureId",
                table: "DailyReasons",
                column: "ContentCultureId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyReasons_SpecificationId",
                table: "DailyReasons",
                column: "SpecificationId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentEmbeddings_Embedding",
                table: "DocumentEmbeddings",
                column: "Embedding")
                .Annotation("Npgsql:IndexMethod", "hnsw")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_EngagementInsights_BatchJobsId",
                table: "EngagementInsights",
                column: "BatchJobsId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_UserGroupId",
                table: "Users",
                column: "UserGroupId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActionEvents");

            migrationBuilder.DropTable(
                name: "AssignUserRights");

            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "CompanyLinks");

            migrationBuilder.DropTable(
                name: "CompanyTeams");

            migrationBuilder.DropTable(
                name: "CurrentRelationshipStatuses");

            migrationBuilder.DropTable(
                name: "DailyFeedback",
                schema: "public");

            migrationBuilder.DropTable(
                name: "DailyFeedbackReportRowDtos");

            migrationBuilder.DropTable(
                name: "DailyMicroActions");

            migrationBuilder.DropTable(
                name: "DocumentEmbeddings");

            migrationBuilder.DropTable(
                name: "EngagementInsights");

            migrationBuilder.DropTable(
                name: "FeedbackAnalysisResults");

            migrationBuilder.DropTable(
                name: "FeedbackDataResultDtos");

            migrationBuilder.DropTable(
                name: "Industries");

            migrationBuilder.DropTable(
                name: "LLMLogs");

            migrationBuilder.DropTable(
                name: "LLMPrompts");

            migrationBuilder.DropTable(
                name: "LLMReports");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "NumberOfEmployees");

            migrationBuilder.DropTable(
                name: "Opportunities");

            migrationBuilder.DropTable(
                name: "OpportunityTypes");

            migrationBuilder.DropTable(
                name: "ResetPasswordLinks");

            migrationBuilder.DropTable(
                name: "TopActions");

            migrationBuilder.DropTable(
                name: "TopRecommendations");

            migrationBuilder.DropTable(
                name: "TopThemes");

            migrationBuilder.DropTable(
                name: "UserTeams");

            migrationBuilder.DropTable(
                name: "UserTokens");

            migrationBuilder.DropTable(
                name: "ActionHubs");

            migrationBuilder.DropTable(
                name: "PromptTypes");

            migrationBuilder.DropTable(
                name: "SystemModules");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "BusinessUnits");

            migrationBuilder.DropTable(
                name: "DailyReasons");

            migrationBuilder.DropTable(
                name: "BatchJobs");

            migrationBuilder.DropTable(
                name: "UserGroups");

            migrationBuilder.DropTable(
                name: "Companies");

            migrationBuilder.DropTable(
                name: "DailyQuestionSpecifications");

            migrationBuilder.DropTable(
                name: "Contacts");

            migrationBuilder.DropTable(
                name: "DailyQuestionSets",
                schema: "public");

            migrationBuilder.DropTable(
                name: "DailyQuestions");

            migrationBuilder.DropTable(
                name: "DailyQuestionSetGroups");

            migrationBuilder.DropTable(
                name: "ContentCultures");
        }
    }
}
