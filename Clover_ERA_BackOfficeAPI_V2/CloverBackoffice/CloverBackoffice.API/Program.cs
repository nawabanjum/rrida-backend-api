using Azure;
using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.Search.Documents;
using CloverBackoffice.API.Middlewares;
using CloverBackoffice.Components;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.BusinessModel;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Logging;
using CloverBackoffice.Repository;
using CloverBackoffice.Repository.Implementation;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Implementation;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json.Linq;
using System.Text;
using System.Threading.RateLimiting;
using Pgvector.EntityFrameworkCore;
using static CloverBackoffice.Infrastructure.DTO.AzureConfigs;

var builder = WebApplication.CreateBuilder(args);

//key Vault changes
// Held so the secret-missing message below can say whether the vault itself failed
// to load, which is the usual cause.
Exception? keyVaultLoadError = null;
var keyVaultUrl = builder.Configuration["AzureKeyVault:VaultUrl"];
if (!string.IsNullOrEmpty(keyVaultUrl))
{
    TokenCredential keyVaultCredential;

    var kvTenantId = builder.Configuration["AzureKeyVault:TenantId"];
    var kvClientId = builder.Configuration["AzureKeyVault:ClientId"];
    var kvClientSecret = Environment.GetEnvironmentVariable("AzureAd__ClientSecret");

    var managedIdentityClientId = builder.Configuration["AzureKeyVault:ManagedIdentityClientId"];

    if (!string.IsNullOrWhiteSpace(kvTenantId) &&
        !string.IsNullOrWhiteSpace(kvClientId) &&
        !string.IsNullOrWhiteSpace(kvClientSecret))
    {
        // Use explicit Service Principal credentials from configuration
        keyVaultCredential = new ClientSecretCredential(kvTenantId, kvClientId, kvClientSecret);
    }
    else if (!string.IsNullOrWhiteSpace(managedIdentityClientId))
    {
        // Use User-Assigned Managed Identity if a ClientId is provided
        keyVaultCredential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ManagedIdentityClientId = managedIdentityClientId
        });
    }
    else
    {
        // Fall back to DefaultAzureCredential (System-Assigned Managed Identity in App Service)
        keyVaultCredential = new DefaultAzureCredential();
    }

    try
    {
        builder.Configuration.AddAzureKeyVault(new Uri(keyVaultUrl), keyVaultCredential);
    }
    catch (Exception ex)
    {
        // Startup deliberately continues: every secret can also be supplied through
        // environment variables or user-secrets, which is how local development runs.
        //
        // But the failure must be visible. Swallowing it silently turns a credential
        // problem here into an ArgumentNullException far below, where the missing
        // secret is finally dereferenced, and the real cause is invisible.
        keyVaultLoadError = ex;
        Console.Error.WriteLine(
            $"[Startup][KeyVault] Could not load secrets from {keyVaultUrl}: " +
            $"{ex.GetBaseException().Message}");
        Console.Error.WriteLine(
            "[Startup][KeyVault] Continuing without vault secrets. Any secret not supplied " +
            "via environment variables or user-secrets will be missing.");
    }
}


builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
        httpContext => RateLimitPartition.GetFixedWindowLimiter(
            // Partition on the token's userId claim, NOT User.Identity.Name.
            //
            // The JWT issues email / userId / userGroupId and no NameClaimType is
            // configured, so Identity.Name is always null — every authenticated
            // caller fell through to the Host header and shared ONE 100-per-minute
            // bucket for the whole API, letting ordinary traffic 429 itself.
            //
            // Anonymous requests still bucket by Host: there is no ForwardedHeaders
            // setup here, so behind App Service the remote IP is the proxy's and
            // would be no better.
            partitionKey: httpContext.User?.FindFirst("userId")?.Value
                          ?? httpContext.Request.Headers.Host.ToString(),
            factory: partition => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1)
            }));
});

// CORS Configuration — origins loaded from appsettings "Cors:AllowedOrigins"
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? Array.Empty<string>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var azureAdSettings = builder.Configuration.GetSection("AzureAd");
// Two-step lookup used for every secret in this app: appsettings holds the NAME of a
// Key Vault secret, and that name is then used as a configuration key to read the VALUE.
// The value can come from the vault, an environment variable, or user-secrets.
var jwtSettingsKeySecretName = builder.Configuration["AzureKeyVault:SecretNames:JwtSettingsKey"];

var jwtSettingsKey = string.IsNullOrWhiteSpace(jwtSettingsKeySecretName)
    ? null
    : builder.Configuration[jwtSettingsKeySecretName];

if (string.IsNullOrWhiteSpace(jwtSettingsKey))
{
    // Without this guard the app failed with "Value cannot be null. (Parameter 's')"
    // from Encoding.GetBytes, which says nothing about which secret is missing.
    var secretLabel = string.IsNullOrWhiteSpace(jwtSettingsKeySecretName)
        ? "(AzureKeyVault:SecretNames:JwtSettingsKey is not set in appsettings)"
        : $"'{jwtSettingsKeySecretName}'";

    throw new InvalidOperationException(
        $"The JWT signing key is missing. Expected configuration key {secretLabel}.\n" +
        (keyVaultLoadError is not null
            ? $"Azure Key Vault failed to load, which is the likely cause: {keyVaultLoadError.GetBaseException().Message}\n"
            : "Azure Key Vault loaded without error, so the secret is probably absent from the vault.\n") +
        "For local development supply it directly, for example:\n" +
        $"    dotnet user-secrets set \"{jwtSettingsKeySecretName ?? "JwtSettingsKry"}\" \"<32+ character key>\"\n" +
        $"or set the environment variable {jwtSettingsKeySecretName ?? "JwtSettingsKry"}.\n" +
        "Note the secret name is spelled exactly as configured, including any typo.");
}

var key = Encoding.UTF8.GetBytes(jwtSettingsKey);

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer("CustomJwt", options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidAudience = jwtSettings["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(key)
    };
})
.AddMicrosoftIdentityWebApi(azureAdSettings, JwtBearerDefaults.AuthenticationScheme);

builder.Services.AddAuthorization(options =>
{
    options.DefaultPolicy = new AuthorizationPolicyBuilder()
        .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme, "CustomJwt")
        .RequireAuthenticatedUser()
        .Build();
});


// 3️ Apply globally to all controllers (optional but recommended)
builder.Services.AddControllers(options =>
{
    var defaultPolicy = new AuthorizationPolicyBuilder(
        JwtBearerDefaults.AuthenticationScheme,
        "CustomJwt"
    )
    .RequireAuthenticatedUser()
    .Build();

    options.Filters.Add(new Microsoft.AspNetCore.Mvc.Authorization.AuthorizeFilter(defaultPolicy));

    // Multi-tenant isolation. AuthorizeFilter above only proves the caller is
    // authenticated; this one proves the company id they sent is one they are
    // allowed to touch. Global on purpose — a per-controller check is exactly
    // what was missing, and any new endpoint taking a profileId is covered the
    // moment it is written. Opt out with [SkipTenantGuard] where a company id
    // legitimately does not name the caller's own tenant.
    options.Filters.Add<CloverBackoffice.API.Filters.CompanyTenantGuardFilter>();
})
.AddJsonOptions(opts =>
{
    // Accept trailing commas and JS-style comments in request bodies.
    opts.JsonSerializerOptions.AllowTrailingCommas = true;
    opts.JsonSerializerOptions.ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip;

    // MVC defaults to 32, and System.Text.Json spends one level on the array and
    // one on the object at every step. Self-nesting trees (the team hierarchy's
    // SubUnit chain) therefore cost 2 per level, so 32 caps get-teams-structure
    // at ~13 nested teams before it throws "A possible object cycle was
    // detected". 128 leaves room for ~60 levels of team nesting.
    opts.JsonSerializerOptions.MaxDepth = 128;
});



builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
{
    options.TokenValidationParameters.ValidateAudience = true;
    options.TokenValidationParameters.ValidAudiences = new[]
{
    builder.Configuration["Jwt:Audience"],
    "https://localhost:7195", // Swagger's token audience (if applicable)
};
    options.Events = new JwtBearerEvents
    {
        OnAuthenticationFailed = context =>
        {
            return Task.CompletedTask;
        }
    };
});

//Key Vault chnage
var environmentName = builder.Configuration["Environment:Name"];
var connectionStringSecretName = environmentName == "PreProd"
    ? builder.Configuration[builder.Configuration["AzureKeyVault:SecretNames:PreProdConnectionString"]]
    : environmentName == "UAT"
    ? builder.Configuration[builder.Configuration["AzureKeyVault:SecretNames:UatConnectionString"]]
    : environmentName == "LIVE"
        ? builder.Configuration[builder.Configuration["AzureKeyVault:SecretNames:LiveConnectionString"]]
    : environmentName == "LIVE2"
        ? builder.Configuration[builder.Configuration["AzureKeyVault:SecretNames:Live2ConnectionString"]]
        : builder.Configuration[builder.Configuration["AzureKeyVault:SecretNames:DevConnectionString"]];


// Configure SendGrid/Resend settings from Key Vault (PostConfigure so it overrides section binding)
builder.Services.PostConfigure<SendGridSettings>(options =>
{
    options.ApiKey = builder.Configuration[builder.Configuration["AzureKeyVault:SecretNames:SendGridApiKey"]];
    options.ResendApiKey = builder.Configuration[builder.Configuration["AzureKeyVault:SecretNames:ResendApiKey"]];
});

// Stripe keys + webhook secret are per-environment and ALWAYS loaded from
// Azure Key Vault — never from appsettings.json. The environment is chosen by
// "Environment:Name" (set per deployment in Azure App Settings). Each
// environment has its OWN secrets in the SAME vault, addressed by the names
// under "AzureKeyVault:StripeSecretNames:<Env>". The rest of the codebase only
// reads SecretKey/PublishableKey/WebhookSecret and stays oblivious to which
// environment they came from.
var stripeEnvKey = (builder.Configuration["Environment:Name"] ?? string.Empty)
    .Trim().ToUpperInvariant() switch
{
    "PREPROD" or "PRE-PROD" => "PreProd",
    "UAT" => "UAT",
    "LIVE" or "LIVE2" or "PROD" or "PRODUCTION" => "Prod",
    _ => "Dev" // Development / local / anything unset
};

// Resolve the Key Vault secret NAMES for the current environment, then read
// each secret VALUE from the vault-backed configuration. Empty name → empty
// value (never pass a null key to the indexer).
string ReadKvSecret(string? secretName)
    => string.IsNullOrWhiteSpace(secretName)
        ? string.Empty
        : (builder.Configuration[secretName] ?? string.Empty);

var stripeSecretKeyName      = builder.Configuration[$"AzureKeyVault:StripeSecretNames:{stripeEnvKey}:SecretKey"];
var stripePublishableKeyName = builder.Configuration[$"AzureKeyVault:StripeSecretNames:{stripeEnvKey}:PublishableKey"];
var stripeWebhookSecretName  = builder.Configuration[$"AzureKeyVault:StripeSecretNames:{stripeEnvKey}:WebhookSecret"];

builder.Services.PostConfigure<CloverBackoffice.Infrastructure.DTO.StripeSettings>(options =>
{
    options.SecretKey      = ReadKvSecret(stripeSecretKeyName);
    options.PublishableKey = ReadKvSecret(stripePublishableKeyName);
    options.WebhookSecret  = ReadKvSecret(stripeWebhookSecretName);

    if (string.IsNullOrWhiteSpace(options.SecretKey) || string.IsNullOrWhiteSpace(options.WebhookSecret))
    {
        Console.WriteLine(
            $"[Startup][Stripe] WARNING: environment '{stripeEnvKey}' — one or more Stripe secrets are EMPTY. " +
            $"Expected Key Vault secrets: '{stripeSecretKeyName}', '{stripePublishableKeyName}', '{stripeWebhookSecretName}'. " +
            "Confirm the vault holds them and the app's identity has GET access.");
    }
});

var azureSearchKeySecretName = builder.Configuration["AzureKeyVault:SecretNames:AzureSearchKey"];
var azureSearchKey = builder.Configuration[azureSearchKeySecretName];

builder.Services.Configure<AzureSearchConfig>(options =>
{
    options.ApiKey = builder.Configuration[builder.Configuration["AzureKeyVault:SecretNames:AzureSearchKey"]];
});

builder.Services.Configure<AzureSearchConfig>(builder.Configuration.GetSection("AzureSearch"));

builder.Services.AddHttpClient("azureopenai");

var searchCfg = builder.Configuration.GetSection("AzureSearch").Get<AzureSearchConfig>();
builder.Services.AddSingleton(_ =>
    new SearchClient(new Uri(searchCfg.Endpoint), searchCfg.IndexName, new AzureKeyCredential(azureSearchKey)));
//closed rag
// ========== LITELLM REGISTRATION ==========
builder.Services.AddHttpContextAccessor();

builder.Services.Configure<LiteLLMOptions>(
    builder.Configuration.GetSection(LiteLLMOptions.SectionName));

builder.Services.AddHttpClient<ILiteLLMService, LiteLLMService>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<LiteLLMOptions>>().Value;

    // CRITICAL: Set BaseAddress
    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);

    // Add Authorization header
    client.DefaultRequestHeaders.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", options.MasterKey);

    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.DefaultRequestHeaders.Add("User-Agent", "CloverEra-API/1.0");
});
// =====================================================

//EF Core
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionStringSecretName, o => o.UseVector()));

// Job classes. Each one is driven by its own hosted BackgroundService below,
// on the cadence configured under "JobSchedules" in appsettings.json.
builder.Services.AddScoped<GuardShortageJob>();
builder.Services.AddScoped<SubscriptionLifecycleJob>();
builder.Services.AddScoped<ComplimentaryLicenseReconciliationJob>();


builder.Services.AddScoped<DatabaseSeeder>();
// DI Registrations
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IUserAccountLockService, UserAccountLockService>();
builder.Services.AddScoped<IUserManualActivationService, UserManualActivationService>();
builder.Services.AddScoped<ISystemSettingRepository, SystemSettingRepository>();
builder.Services.AddScoped<ISystemSettingService, SystemSettingService>();
builder.Services.AddScoped<IPermissionRepository, PermissionRepository>();
builder.Services.AddScoped<IPermissionService, PermissionService>();
builder.Services.AddScoped<IAccountRegistrationRepository, AccountRegistrationRepository>();
builder.Services.AddScoped<IAccountRegistrationService, AccountRegistrationService>();
builder.Services.AddScoped<IAuditLogsService, AuditLogsService>();
builder.Services.AddScoped<IAuditLogsRepository, AuditLogsRepository>();
builder.Services.AddScoped<JwtTokenGenerator>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITokenReaderService, TokenReaderService>();
builder.Services.AddScoped<ITenantAccessService, TenantAccessService>();
builder.Services.AddScoped<IFeedbackAnalysisService, FeedbackAnalysisService>();
builder.Services.AddScoped<IFeedbackRepository, FeedbackRepository>();
builder.Services.AddScoped<IFeedbackEntryRepository, FeedbackEntryRepository>();
builder.Services.AddScoped<IFeedbackListService, FeedbackListService>();
builder.Services.AddScoped<ICompanyTeamService, CompanyTeamService>();
builder.Services.AddScoped<ITeamStructureService, TeamStructureService>();
builder.Services.AddScoped<ITeamHealthAccessService, TeamHealthAccessService>();
builder.Services.AddScoped<ITeamMilestoneService, TeamMilestoneService>();
builder.Services.AddScoped<ITeamHealthTimelineService, TeamHealthTimelineService>();
builder.Services.AddScoped<ITeamCustomQuestionService, TeamCustomQuestionService>();
builder.Services.AddScoped<ITeamActionService, TeamActionService>();
// Manager-created actions (api/actions), told to Eran in daily messages, chat and suggestions.
builder.Services.AddScoped<IManagerActionService, ManagerActionService>();
// Plan catalogue for the Admin side (api/admin/plans, api/admin/plan-features), RRIDA Admin only.
builder.Services.AddScoped<IPlanManagementService, PlanManagementService>();
// Customer plan reads + price quotes (api/plans) and plan entitlements (api/entitlements).
builder.Services.AddScoped<IPlanCatalogService, PlanCatalogService>();
builder.Services.AddScoped<IPlanEntitlementService, PlanEntitlementService>();
// Backend enforcement of the plan (Timeline, Portfolio, Eran monthly limits, nudges/report, Clover Resources).
builder.Services.AddScoped<IPlanFeatureGate, PlanFeatureGate>();
builder.Services.AddScoped<ITeamHealthUserTeamsService, TeamHealthUserTeamsService>();
builder.Services.AddScoped<ITeamWeeklyFlagService, TeamWeeklyFlagService>();
builder.Services.AddScoped<ITeamCalendarInviteService, TeamCalendarInviteService>();
builder.Services.AddScoped<ITeamStateService, TeamStateService>();
builder.Services.AddScoped<IPortfolioViewService, PortfolioViewService>();
builder.Services.AddScoped<IManagerOnboardingService, ManagerOnboardingService>();
builder.Services.AddScoped<IUserRoleAssignmentService, UserRoleAssignmentService>();
builder.Services.AddScoped<ITeamManagerHandoverService, TeamManagerHandoverService>();
builder.Services.AddScoped<ICompanyTeamRepository, CompanyTeamRepository>();
builder.Services.AddScoped<ISystemNotificationService, SystemNotificationService>();
builder.Services.AddScoped<ISystemNotificationRepository, SystemNotificationRepository>();
builder.Services.AddScoped<ICompanyLicenseRepository, CompanyLicenseRepository>();
builder.Services.AddScoped<ICompanyLicenseService, CompanyLicenseService>();
builder.Services.AddScoped<ILicensedPeriodService, LicensedPeriodService>();
builder.Services.AddScoped<ILegacyTrialBackfillService, LegacyTrialBackfillService>();
builder.Services.AddScoped<IActionHubService, ActionHubService>();
builder.Services.AddScoped<IActionHubRepository, ActionHubRepository>();
builder.Services.AddScoped<ICloveraCompanyAdminService, CloveraCompanyAdminService>();
builder.Services.AddScoped<ICloveraCompanyAdminRepository, CloveraCompanyAdminRepository>();
builder.Services.AddScoped<IEmbeddingRepository, EmbeddingRepository>();
builder.Services.AddScoped<ISearchRepository, SearchRepository>();
builder.Services.AddScoped<IRagService, RagService>();
builder.Services.AddScoped<IOpportunityService, OpportunityService>();
builder.Services.AddScoped<IOpportunityRepository, OpportunityRepository>();
builder.Services.AddScoped<IPartnerCustomerService, PartnerCustomerService>();
builder.Services.AddScoped<IPartnerCustomerRepository, PartnerCustomerRepository>();
builder.Services.AddScoped<IQuestionService, QuestionService>();
builder.Services.AddScoped<IQuestionRepository, QuestionRepository>();
// RRIDA Team Daily Questions — a separate bank from the legacy CLOVER chain above.
builder.Services.AddScoped<ITeamQuestionService, TeamQuestionService>();
builder.Services.AddScoped<ITeamQuestionRepository, TeamQuestionRepository>();
// Custom questions — their own table, usable by either question system.
builder.Services.AddScoped<ICustomQuestionService, CustomQuestionService>();
builder.Services.AddScoped<ICustomQuestionRepository, CustomQuestionRepository>();
// Team setup — the unit, its team type and context, and its goals, written as one.
builder.Services.AddScoped<ITeamSetupService, TeamSetupService>();
builder.Services.AddScoped<ITeamSetupRepository, TeamSetupRepository>();

// Mobile app endpoints (api/v1/*). One service and repository behind every
// endpoint the Clover ERA app calls - see Controllers/Mobile.
builder.Services.AddScoped<IMobileApiService, MobileApiService>();
builder.Services.AddScoped<IMobileApiRepository, MobileApiRepository>();
builder.Services.AddScoped<IAggregateDashboardService, AggregateDashboardService>();
builder.Services.AddScoped<IAggregateDashboardRepository, AggregateDashboardRepository>();
builder.Services.AddScoped<IPushNotificationService, PushNotificationService>();
builder.Services.AddScoped<IPushNotificationRepository, PushNotificationRepository>();
// "Report is ready" email + push. Shared by the manual create-report endpoint
// (FeedbackAnalysisService) and the scheduler (GuardShortageJob).
builder.Services.AddScoped<IReportNotificationService, ReportNotificationService>();
// Admin review gate between report generation and the manager notification.
// GuardShortageJob parks generated reports as PendingReview and notifies the
// configured reviewers; ReportApprovalService owns the only release path.
builder.Services.AddScoped<IReportApprovalRepository, ReportApprovalRepository>();
builder.Services.AddScoped<IReportApprovalService, ReportApprovalService>();
builder.Services.AddSingleton<IRateLimiterService>(new RateLimiterService(20));
builder.Services.AddScoped<IDocumentBatchesService, DocumentBatchesService>();
builder.Services.AddScoped<IDocumentBatchesRepository, DocumentBatchesRepository>();


builder.Services.AddAuthorization();
var sendGridSettings = builder.Configuration.GetSection("SendGrid").Get<SendGridSettings>();
sendGridSettings.ApiKey = builder.Configuration[builder.Configuration["AzureKeyVault:SecretNames:SendGridApiKey"]];
sendGridSettings.ResendApiKey = builder.Configuration[builder.Configuration["AzureKeyVault:SecretNames:ResendApiKey"]];

// App Settings
builder.Services.Configure<AppSettings>(builder.Configuration.GetSection("AppSettings"));
builder.Services.Configure<SendGridSettings>(builder.Configuration.GetSection("SendGrid"));
builder.Services.AddSingleton(res => res.GetRequiredService<IOptions<SendGridSettings>>().Value);
builder.Services.Configure<FeedbackScoreThresholdsOptions>(builder.Configuration.GetSection("FeedbackScoreThresholds"));
// Static body / link label for the mailed daily question calendar invite.
builder.Services.Configure<DailyQuestionInviteEmailSettings>(builder.Configuration.GetSection("DailyQuestion:InviteEmail"));
builder.Services.AddSingleton<ILoggerProvider, FileLoggerProvider>();

// Insights Automation DI
builder.Services.AddScoped<IInsightsRepository, InsightsRepository>();
builder.Services.AddScoped<IBatchProcessingService, BatchProcessingService>();
builder.Services.AddScoped<IEmbeddingService, EmbeddingService>();
builder.Services.AddScoped<IDocumentEmbeddingRepository, DocumentEmbeddingRepository>();
builder.Services.AddScoped<IDocumentEmbeddingAuditLogRepository, DocumentEmbeddingAuditLogRepository>();
builder.Services.AddScoped<IDocumentEmbeddingGeneratorService, DocumentEmbeddingGeneratorService>();
builder.Services.AddScoped<IDocumentEmbeddingService, DocumentEmbeddingService>();
builder.Services.AddScoped<IDocumentParserService, DocumentParserService>();
builder.Services.AddScoped<IInsightsGenerationService, InsightsGenerationService>();
// Per-job named schedules under "JobSchedules" in appsettings.json. Every
// hosted background service in the app reads its cadence through
// IOptionsMonitor<JobScheduleOptions>.Get(JobName), so the same DTO drives
// all of them. Default per section = Mode "Daily" at 00:00 + 12:00 UTC;
// switch to Mode "Interval" with IntervalMinutes for fast local testing.
builder.Services.Configure<CloverBackoffice.Infrastructure.DTO.JobScheduleOptions>(
    CloverBackoffice.Infrastructure.Helpers.JobScheduleHelper.JobNames.SubscriptionLifecycle,
    builder.Configuration.GetSection("JobSchedules:SubscriptionLifecycle"));
builder.Services.Configure<CloverBackoffice.Infrastructure.DTO.JobScheduleOptions>(
    CloverBackoffice.Infrastructure.Helpers.JobScheduleHelper.JobNames.EngineDailyRun,
    builder.Configuration.GetSection("JobSchedules:EngineDailyRun"));
builder.Services.Configure<CloverBackoffice.Infrastructure.DTO.JobScheduleOptions>(
    CloverBackoffice.Infrastructure.Helpers.JobScheduleHelper.JobNames.BatchProcessing,
    builder.Configuration.GetSection("JobSchedules:BatchProcessing"));
builder.Services.Configure<CloverBackoffice.Infrastructure.DTO.JobScheduleOptions>(
    CloverBackoffice.Infrastructure.Helpers.JobScheduleHelper.JobNames.BatchPolling,
    builder.Configuration.GetSection("JobSchedules:BatchPolling"));
builder.Services.Configure<CloverBackoffice.Infrastructure.DTO.JobScheduleOptions>(
    CloverBackoffice.Infrastructure.Helpers.JobScheduleHelper.JobNames.DailyFeedbackAudit,
    builder.Configuration.GetSection("JobSchedules:DailyFeedbackAudit"));
builder.Services.Configure<CloverBackoffice.Infrastructure.DTO.JobScheduleOptions>(
    CloverBackoffice.Infrastructure.Helpers.JobScheduleHelper.JobNames.TeamDailyFeedbackAudit,
    builder.Configuration.GetSection("JobSchedules:TeamDailyFeedbackAudit"));
builder.Services.Configure<CloverBackoffice.Infrastructure.DTO.JobScheduleOptions>(
    CloverBackoffice.Infrastructure.Helpers.JobScheduleHelper.JobNames.CustomQuestionExpiry,
    builder.Configuration.GetSection("JobSchedules:CustomQuestionExpiry"));
builder.Services.Configure<CloverBackoffice.Infrastructure.DTO.JobScheduleOptions>(
    CloverBackoffice.Infrastructure.Helpers.JobScheduleHelper.JobNames.ReportScheduler,
    builder.Configuration.GetSection("JobSchedules:ReportScheduler"));
builder.Services.Configure<CloverBackoffice.Infrastructure.DTO.JobScheduleOptions>(
    CloverBackoffice.Infrastructure.Helpers.JobScheduleHelper.JobNames.FeedbackAutoImport,
    builder.Configuration.GetSection("JobSchedules:FeedbackAutoImport"));
builder.Services.Configure<CloverBackoffice.Infrastructure.DTO.JobScheduleOptions>(
    CloverBackoffice.Infrastructure.Helpers.JobScheduleHelper.JobNames.TeamOnboardingNotifications,
    builder.Configuration.GetSection("JobSchedules:TeamOnboardingNotifications"));
builder.Services.Configure<CloverBackoffice.Infrastructure.DTO.JobScheduleOptions>(
    CloverBackoffice.Infrastructure.Helpers.JobScheduleHelper.JobNames.EngineDummyCheckIn,
    builder.Configuration.GetSection("JobSchedules:EngineDummyCheckIn"));

// TEST DATA: which teams get a dummy engine check-in every day, and from which
// workbook. Does nothing unless Enabled is true; see "EngineDummyCheckIn".
builder.Services.Configure<CloverBackoffice.Infrastructure.DTO.EngineDummyCheckInSettings>(
    builder.Configuration.GetSection("EngineDummyCheckIn"));

// Report cadence for units the admin never scheduled. Read by GuardShortageJob
// alongside the per-unit LLMReportSchedules rows — see the section comment in
// appsettings.json.
builder.Services.Configure<CloverBackoffice.Infrastructure.DTO.ReportGenerationFallbackOptions>(
    builder.Configuration.GetSection("ReportGenerationFallback"));

// Reviewer list + kill switch for the admin approval step. In Production the
// three addresses come from Azure App Settings
// (ReportApproval__ReviewerEmails__0/1/2), not from the checked-in file.
builder.Services.Configure<CloverBackoffice.Infrastructure.DTO.ReportApprovalOptions>(
    builder.Configuration.GetSection("ReportApproval"));

// Kill switches for the pre-scheduler report paths. All default to false: the
// AppSettings-configured background service (ReportSchedulerBackgroundService →
// GuardShortageJob) is the authoritative automated report-generation flow. The
// disabled code is retained, not removed.
builder.Services.Configure<CloverBackoffice.Infrastructure.DTO.LegacyReportPathOptions>(
    builder.Configuration.GetSection("LegacyReportPaths"));

// Day 2 / Day 5 onboarding nudges to a new team's manager. Day numbers and the
// catch-up window live here; the wake-up cadence is under
// "JobSchedules:TeamOnboardingNotifications".
builder.Services.Configure<CloverBackoffice.Infrastructure.DTO.TeamOnboardingNotificationOptions>(
    builder.Configuration.GetSection("TeamOnboardingNotifications"));
builder.Services.AddScoped<ITeamOnboardingNotificationService, TeamOnboardingNotificationService>();

builder.Services.AddHostedService<BatchProcessingBackgroundService>();
builder.Services.AddHostedService<EngineDailyRunBackgroundService>();
builder.Services.AddHostedService<FeedbackAutoImportBackgroundService>();
builder.Services.AddHostedService<EngineDummyCheckInBackgroundService>();

// Azure OpenAI batch-job polling. Replaces the CloverBackoffice.Jobs Function
// App timer trigger that used to POST /api/rag/azure-backgroundjob-function
// every 5 minutes; same work, same cadence, now in-process.
builder.Services.AddHostedService<BatchPollingBackgroundService>();

// DailyFeedback Audit Log DI
builder.Services.AddScoped<IDailyFeedbackAuditLogRepository, DailyFeedbackAuditLogRepository>();
builder.Services.AddScoped<IDailyFeedbackAuditService, DailyFeedbackAuditService>();
builder.Services.AddHostedService<DailyFeedbackAuditBackgroundService>();

// TeamDailyFeedback Audit Log DI
builder.Services.AddScoped<ITeamDailyFeedbackAuditLogRepository, TeamDailyFeedbackAuditLogRepository>();
builder.Services.AddScoped<ITeamDailyFeedbackAuditService, TeamDailyFeedbackAuditService>();
builder.Services.AddHostedService<TeamDailyFeedbackAuditBackgroundService>();

// Custom question expiry sweep. Moves a custom question to Expired once the UTC
// day its ScheduledAt falls in has ended; GET api/team-daily-question/custom-questions
// checks the same period against the clock, so this is a status correction and not
// the gate. ICustomQuestionService and its repository are registered above.
builder.Services.AddHostedService<CustomQuestionExpiryBackgroundService>();

// Log Collector (scoped per-request, used to capture detailed logs for audit)
builder.Services.AddScoped<ILogCollector, LogCollector>();

// Workbench Background Processing
builder.Services.AddSingleton<PromptfooWorkQueue>();
builder.Services.AddHostedService<PromptfooBackgroundService>();

// CloverEra Engine Background Processing
builder.Services.AddSingleton<CloverEraWorkQueue>();
builder.Services.AddHostedService<CloverEraBackgroundService>();

// One workbench run, start to finish. Shared: CloverEraBackgroundService calls
// it for runs the operator queued from the workbench, GuardShortageJob calls it
// inline for runs the report cadence raised. Same run row, same ResultJson,
// same logs — the trigger is the only difference between them.
builder.Services.AddScoped<IWorkbenchRunExecutor, WorkbenchRunExecutor>();

// Clovera Personal App — Snapshot Report Background Processing
builder.Services.AddSingleton<CloveraPersonalSnapshotWorkQueue>();
builder.Services.AddHostedService<CloveraPersonalSnapshotBackgroundService>();
builder.Services.AddScoped<ICloveraPersonalSnapshotService, CloveraPersonalSnapshotService>();
// Troubleshooting email logger for the snapshot Run/Status flow. Singleton so the
// once-per-snapshot final-status de-dupe survives across polling requests.
builder.Services.AddSingleton<ISnapshotEmailLogService, SnapshotEmailLogService>();

// Per-unit scheduled report runner.
builder.Services.AddHostedService<ReportSchedulerBackgroundService>();

// New-team onboarding nudges to the team's manager (day 2 / day 5 after the
// team is created). Time-delayed relative to team creation, so it runs here
// rather than from the upsert-team endpoint.
builder.Services.AddHostedService<TeamOnboardingNotificationBackgroundService>();

// Subscription lifecycle polling runner.
builder.Services.AddHostedService<SubscriptionLifecycleBackgroundService>();

// PromptOps Integration
builder.Services.AddScoped<IPromptTemplateRepository, PromptTemplateRepository>();
builder.Services.AddScoped<IAIRequestLogRepository, AIRequestLogRepository>();
builder.Services.AddScoped<IAIGatewayService, AzureAIGatewayService>();
builder.Services.AddScoped<IPromptManagementService, PromptManagementService>();
builder.Services.AddScoped<ICloverEraEngineService, CloverEraEngineService>();
builder.Services.AddScoped<IResponseValidatorService, ResponseValidatorService>();
builder.Services.AddHttpClient("aigateway");
builder.Services.AddMemoryCache();

// EranCoach — manager coach (ported from the ERAn reference). Own controller/service/
// client/tools/corpus; reuses ModelConfiguration creds + the DocumentEmbedding pgvector store.
builder.Services.AddScoped<IEranCoachCorpusRepository, EranCoachCorpusRepository>();
builder.Services.AddScoped<IEranCoachTeamContextRepository, EranCoachTeamContextRepository>();
builder.Services.AddScoped<IEranCoachReportRepository, EranCoachReportRepository>();
builder.Services.AddScoped<IEranCoachMemoryRepository, EranCoachMemoryRepository>();
builder.Services.AddScoped<IEranCoachConversationRepository, EranCoachConversationRepository>();
builder.Services.AddScoped<IEranCoachModelConfigRepository, EranCoachModelConfigRepository>();
// Shared role-based unit scoping (also consumed by AggregateDashboardService).
builder.Services.AddScoped<IUnitAccessService, UnitAccessService>();
builder.Services.AddScoped<IEranCoachTools, EranCoachTools>();
builder.Services.AddScoped<IEranCoachSeedService, EranCoachSeedService>();
// Corpus seeding runs off the request thread — embedding the whole bundle outlives the
// gateway's request timeout. POST /eran-coach/seed-corpus queues; the worker does the work.
builder.Services.AddSingleton<EranCoachSeedWorkQueue>();
builder.Services.AddSingleton<EranCoachSeedRunState>();
builder.Services.AddHostedService<EranCoachSeedBackgroundService>();
builder.Services.AddScoped<IEranCoachTeamContextService, EranCoachTeamContextService>();
builder.Services.AddScoped<IEranCoachReportService, EranCoachReportService>();
builder.Services.AddScoped<IEranCoachMemoryService, EranCoachMemoryService>();
builder.Services.AddScoped<IEranCoachConversationService, EranCoachConversationService>();
builder.Services.AddScoped<IEranCoachModelConfigService, EranCoachModelConfigService>();
builder.Services.AddScoped<IEranCoachAnthropicClient, EranCoachAnthropicClient>();
builder.Services.AddScoped<IEranCoachChatService, EranCoachChatService>();

// Subscription & Payment
builder.Services.Configure<CloverBackoffice.Infrastructure.DTO.StripeSettings>(builder.Configuration.GetSection("Stripe"));
builder.Services.AddScoped<IPaymentAuditLogRepository, PaymentAuditLogRepository>();
builder.Services.AddScoped<IPlanRepository, PlanRepository>();
builder.Services.AddScoped<ICompanySubscriptionRepository, CompanySubscriptionRepository>();
builder.Services.AddScoped<IPaymentRepository, PaymentRepository>();
builder.Services.AddScoped<IStripeService, StripeService>();
builder.Services.AddScoped<IPlanService, PlanService>();
builder.Services.AddScoped<ISubscriptionService, SubscriptionService>();

// RRIDA deterministic layer. Singleton because the kit is immutable once loaded; the
// resolver is NOT registered here, since it carries per-team running state and belongs to
// one run. See external/RRIDA-Engine/VENDORED.md.
builder.Services.AddSingleton<IRridaEngineContextFactory, RridaEngineContextFactory>();
builder.Services.AddScoped<IRridaReferenceDataLoader, RridaReferenceDataLoader>();
builder.Services.AddScoped<IEngineTeamProvisioner, EngineTeamProvisioner>();
builder.Services.AddScoped<IEngineCheckInService, EngineCheckInService>();
builder.Services.AddScoped<IEngineResultStore, EngineResultStore>();
builder.Services.AddScoped<IEngineWeeklyFlagWriter, EngineWeeklyFlagWriter>();
builder.Services.AddScoped<IEngineReplayRunner, EngineReplayRunner>();
builder.Services.AddScoped<EngineDailyRunJob>();
builder.Services.AddScoped<IEngineManagerService, EngineManagerService>();
builder.Services.AddScoped<IEranNarratorService, EranNarratorService>();
builder.Services.AddScoped<IEranCoachTimelineService, EranCoachTimelineService>();
builder.Services.AddScoped<EranEngineContextBuilder>();
builder.Services.AddScoped<IEngineChatContextService, EngineChatContextService>();
// Eran phrases each team's new engine messages after a run, in the background.
builder.Services.AddSingleton<EngineNarrationQueue>();
builder.Services.AddSingleton<IEngineNarrationQueue>(sp => sp.GetRequiredService<EngineNarrationQueue>());
builder.Services.AddHostedService<EngineNarrationBackgroundService>();
builder.Services.AddScoped<IEngineRollUpService, EngineRollUpService>();
builder.Services.AddScoped<IEngineScenarioSeeder, EngineScenarioSeeder>();
builder.Services.AddScoped<EngineWorkbookTeamHealthWriter>();
builder.Services.AddScoped<IEngineWorkbookImporter, EngineWorkbookImporter>();
builder.Services.AddScoped<IEngineDummyCheckInService, EngineDummyCheckInService>();
builder.Services.AddScoped<ILicenseAvailabilityService, LicenseAvailabilityService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<IPromoCodeRepository, PromoCodeRepository>();
builder.Services.AddScoped<IPromoCodeService, PromoCodeService>();

// System Default Settings (per Company + BusinessUnit)
builder.Services.AddScoped<ILLMReportScheduleRepository, LLMReportScheduleRepository>();
builder.Services.AddScoped<ILLMReportScheduleService, LLMReportScheduleService>();

// Azure Marketplace SaaS Fulfillment
builder.Services.Configure<CloverBackoffice.Infrastructure.DTO.AzureMarketplaceSettings>(
    builder.Configuration.GetSection("AzureMarketplace"));
builder.Services.PostConfigure<CloverBackoffice.Infrastructure.DTO.AzureMarketplaceSettings>(options =>
{
    var secretName = builder.Configuration["AzureKeyVault:SecretNames:AzureMarketplaceClientSecret"];
    if (!string.IsNullOrWhiteSpace(secretName))
        options.ClientSecret = builder.Configuration[secretName];
});
builder.Services.AddScoped<IAzureMarketplaceSubscriptionRepository, AzureMarketplaceSubscriptionRepository>();
builder.Services.AddHttpClient<IAzureMarketplaceService, AzureMarketplaceService>();
builder.Services.AddScoped<IAzureMarketplaceWebhookHandler, AzureMarketplaceWebhookHandler>();


//  Initialize encryption configuration
BussinessManager.Initialize(builder.Configuration, sendGridSettings);

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddControllers();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "RRIDA Web API",
        Version = "v1",
        Description = "The RRIDA Web API, built with .NET 9"
    });

    // Keeps ONLY the controllers named in Swagger:VisibleControllers and drops
    // every other operation. Nothing is dropped unless that section lists
    // something, so this is inert in any environment that does not configure it.
    options.DocumentFilter<CloverBackoffice.API.Filters.VisibleControllersDocumentFilter>();

    // Hides the endpoints named in Swagger:HiddenEndpoints. Nothing is hidden
    // unless that section lists something, so this is inert in any environment
    // that does not configure it. See HiddenEndpointsDocumentFilter — it is a
    // documentation filter only, and hides nothing from the router.
    options.DocumentFilter<CloverBackoffice.API.Filters.HiddenEndpointsDocumentFilter>();

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        Description = @"Enter 'Bearer' [space] and then your token.  
        Example: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });

});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbSeeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
    await dbSeeder.SeedSuperAdminsAsync();
}

// ONE-TIME migration, gated entirely by "Licensing:LegacyTrialBackfill:Enabled"
// in appsettings (see the block there for the full rollout note — all licensing
// configuration lives under the "Licensing" section). Flag ON → move any legacy
// company (no subscription row) onto a Trial with a license assigned to every
// existing unit and NO team/unit cap. Flag OFF or key missing → the migration
// does not run at all.
//
// The default is FALSE on purpose: this is meant to be switched on for the
// first Production deployment of the licensing system and switched off again
// once the summary line below confirms the migration completed. An absent key
// must never silently re-run the migration on a later restart or deployment.
{
    using var scope = app.Services.CreateScope();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    // Mirror the migration outcome into public."AuditLogs" so it can be inspected
    // straight from the DB (no log-stream access needed). Best-effort: a failed
    // audit write must never break startup, so it is wrapped in its own try/catch.
    async Task WriteBackfillAuditAsync(string request, string response)
    {
        try
        {
            var auditLogs = scope.ServiceProvider.GetRequiredService<IAuditLogsService>();
            await auditLogs.AddAuditLogsAsync(new CloverBackoffice.Infrastructure.Models.AuditLogs
            {
                Timestamp = DateTime.UtcNow,
                UserId = null,
                Endpoint = "[Startup] LegacyTrialBackfill",
                HttpMethod = "STARTUP",
                RequestData = request,
                ResponseData = response,
                ActionType = "MIGRATION"
            });
        }
        catch (Exception auditEx)
        {
            logger.LogWarning(auditEx, "[Startup] Failed to write LegacyTrialBackfill audit row.");
        }
    }

    if (app.Configuration.GetValue("Licensing:LegacyTrialBackfill:Enabled", false))
    {
        var trialDays = app.Configuration.GetValue("Licensing:LegacyTrialBackfill:TrialDays", 30);
        try
        {
            var backfill = scope.ServiceProvider.GetRequiredService<ILegacyTrialBackfillService>();
            logger.LogInformation(
                "[Startup] Legacy trial backfill ENABLED (Licensing:LegacyTrialBackfill:Enabled=true, TrialDays={Days}) — running.",
                trialDays);

            var result = await backfill.RunAsync(trialDays, dryRun: false);
            logger.LogInformation(
                "[Startup] Legacy trial backfill: {Count} company(ies) migrated, {Licenses} license(s) created. " +
                "Set Licensing:LegacyTrialBackfill:Enabled=false now that the migration has run.",
                result.LegacyCompanyCount, result.TotalLicensesCreated);

            await WriteBackfillAuditAsync(
                $"{{\"Enabled\":true,\"TrialDays\":{trialDays}}}",
                System.Text.Json.JsonSerializer.Serialize(result));
        }
        catch (Exception ex)
        {
            // Never let the migration take down startup.
            logger.LogError(ex, "[Startup] Legacy trial backfill failed.");
            await WriteBackfillAuditAsync(
                $"{{\"Enabled\":true,\"TrialDays\":{trialDays}}}",
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    status = "FAILED",
                    error = ex.GetBaseException().Message
                }));
        }
    }
    else
    {
        logger.LogInformation(
            "[Startup] Legacy trial backfill DISABLED (Licensing:LegacyTrialBackfill:Enabled=false) — skipped.");
        await WriteBackfillAuditAsync(
            "{\"Enabled\":false}",
            System.Text.Json.JsonSerializer.Serialize(new
            {
                status = "DISABLED",
                message = "Flag is false OR appsettings.Production.json not loaded (check ASPNETCORE_ENVIRONMENT). Migration skipped."
            }));
    }
}

// Swagger UI

// Pre-Prod is the test environment for QA and the frontend, so it shows the
// Swagger UI too, filtered by Swagger:VisibleControllers in appsettings.PreProd.json.
if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("PreProd"))
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "RRIDA Web API v1");
        options.RoutePrefix = string.Empty;
    });
}
// Middleware order matters

app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseHttpsRedirection();
app.UseCors(); //  Must come BEFORE auth and before security headers so preflight gets CORS headers
app.Use(async (context, next) =>
{
    context.Response.Headers.Add("X-Frame-Options", "DENY");
    context.Response.Headers.Add("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Add("X-XSS-Protection", "1; mode=block");
    context.Response.Headers.Add("Referrer-Policy", "strict-origin-when-cross-origin");
    context.Response.Headers.Add("Content-Security-Policy",
        "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline';");
    await next();
});
app.UseStaticFiles(); // Serve static files from wwwroot (icons, images, etc.)
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseMiddleware<TokenAuthentication>();
app.UseMiddleware<AuditMiddleware>();

// Liveness probe for App Service health checks and the deploy pipeline's smoke
// test. Anonymous and dependency-free: it answers once the host is up.
app.MapGet("/health", () => Results.Ok(new { status = "Healthy" })).AllowAnonymous();

app.MapControllers();

app.Run();
