# Integration Guide - CloverBackoffice Codebase

**Purpose:** Guide for integrating the Insights Automation features with Muhammad's existing CloverBackoffice C# codebase  
**Last Updated:** August 16, 2025

---

## Overview

This guide provides step-by-step instructions for integrating the new Insights Automation features into the existing CloverBackoffice API without disrupting current functionality. The integration follows the established patterns and conventions in the codebase.

---

## Prerequisites

### Existing Codebase Understanding
The current CloverBackoffice structure:
```
CloverBackoffice/
├── CloverBackoffice.API/           # API Controllers
├── CloverBackoffice.Services/       # Business Logic
├── CloverBackoffice.Repository/     # Data Access
├── CloverBackofficeInfrastructure/ # Models, DTOs
└── CloverBackoffice.Components/     # Shared Components
```

### Required NuGet Packages
```xml
<!-- Add to CloverBackoffice.API.csproj -->
<PackageReference Include="OpenAI" Version="1.11.0" />
<PackageReference Include="Pgvector" Version="0.2.0" />
<PackageReference Include="StackExchange.Redis" Version="2.7.33" />
<PackageReference Include="iTextSharp" Version="5.5.13.3" />
```

---

## Integration Points

### 1. Extending Existing Services

#### ChatGptRepository Enhancement
The existing `ChatGptRepository` needs to support batch operations:

```csharp
// Location: CloverBackoffice.Repository/Implementation/ChatGptRepository.cs
// ADD these methods to existing class:

public async Task<string> CreateBatchJobAsync(List<BatchRequest> requests)
{
    var client = new OpenAIClient(_settings.ApiKey);
    var jsonlContent = SerializeToJsonl(requests);
    
    // Upload JSONL file
    var file = await client.Files.CreateFileAsync(
        file: jsonlContent,
        purpose: "batch"
    );
    
    // Create batch job
    var batch = await client.Batches.CreateBatchAsync(
        inputFileId: file.Id,
        endpoint: "/v1/chat/completions",
        completionWindow: "24h"
    );
    
    return batch.Id;
}

public async Task<BatchStatus> GetBatchStatusAsync(string batchId)
{
    var client = new OpenAIClient(_settings.ApiKey);
    var batch = await client.Batches.RetrieveBatchAsync(batchId);
    
    return new BatchStatus
    {
        Status = batch.Status,
        CompletedAt = batch.CompletedAt,
        OutputFileId = batch.OutputFileId
    };
}
```

#### FeedbackAnalysisService Extension
Modify the existing service to use batch processing:

```csharp
// Location: CloverBackoffice.Services/Implementation/FeedbackAnalysisService.cs
// MODIFY existing GetTopDataAsync method:

public async Task<TopDataResponse> GetTopDataAsync(long unitId)
{
    // Existing code...
    
    // ADD: Check for cached insights first
    var cachedInsights = await _insightsService.GetCachedInsightsAsync(unitId);
    if (cachedInsights != null && !cachedInsights.IsStale())
    {
        return cachedInsights.ToTopDataResponse();
    }
    
    // Existing analysis code...
    
    // ADD: Trigger batch processing for deeper insights
    if (ShouldGenerateDeepInsights(unitId))
    {
        await _batchProcessingService.QueueInsightsGenerationAsync(unitId);
    }
    
    return result;
}
```

---

## 2. New Service Implementations

### Directory Structure
Create new folders within the existing structure:

```
CloverBackoffice.Services/
├── Existing services...
└── Insights/                      # NEW
    ├── IBatchProcessingService.cs
    ├── BatchProcessingService.cs
    ├── IEmbeddingService.cs
    ├── EmbeddingService.cs
    ├── IInsightsGenerationService.cs
    └── InsightsGenerationService.cs
```

### BatchProcessingService Implementation

```csharp
// Location: CloverBackoffice.Services/Insights/BatchProcessingService.cs

using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;

namespace CloverBackoffice.Services.Insights
{
    public class BatchProcessingService : IBatchProcessingService
    {
        private readonly IChatGptRepository _chatGptRepository;
        private readonly IInsightsRepository _insightsRepository;
        private readonly ILogger<BatchProcessingService> _logger;
        
        public BatchProcessingService(
            IChatGptRepository chatGptRepository,
            IInsightsRepository insightsRepository,
            ILogger<BatchProcessingService> logger)
        {
            _chatGptRepository = chatGptRepository;
            _insightsRepository = insightsRepository;
            _logger = logger;
        }
        
        public async Task<BatchJob> CreateBatchJobAsync(uint businessUnitId, int periodDays)
        {
            // 1. Aggregate data from database
            var feedbackData = await _insightsRepository.GetFeedbackDataAsync(
                businessUnitId, 
                DateTime.UtcNow.AddDays(-periodDays)
            );
            
            // 2. Create batch requests
            var batchRequests = CreateBatchRequests(feedbackData);
            
            // 3. Submit to OpenAI
            var batchId = await _chatGptRepository.CreateBatchJobAsync(batchRequests);
            
            // 4. Store job in database
            var job = new BatchJob
            {
                Id = Guid.NewGuid(),
                OpenAIJobId = batchId,
                BusinessUnitId = businessUnitId,
                Status = "submitted",
                SubmittedAt = DateTime.UtcNow
            };
            
            await _insightsRepository.SaveBatchJobAsync(job);
            
            return job;
        }
        
        // Additional methods...
    }
}
```

---

## 3. Repository Layer Integration

### New Repository Interfaces

```csharp
// Location: CloverBackoffice.Repository/Interfaces/IInsightsRepository.cs

namespace CloverBackoffice.Repository.Interfaces
{
    public interface IInsightsRepository
    {
        Task<EngagementInsights> GetLatestInsightsAsync(uint businessUnitId);
        Task<List<FeedbackData>> GetFeedbackDataAsync(uint businessUnitId, DateTime fromDate);
        Task SaveInsightsAsync(EngagementInsights insights);
        Task<BatchJob> SaveBatchJobAsync(BatchJob job);
        Task<BatchJob> GetBatchJobAsync(Guid jobId);
        Task UpdateBatchJobStatusAsync(Guid jobId, string status);
    }
}
```

### Repository Implementation

```csharp
// Location: CloverBackoffice.Repository/Implementation/InsightsRepository.cs

namespace CloverBackoffice.Repository.Implementation
{
    public class InsightsRepository : IInsightsRepository
    {
        private readonly ApplicationDbContext _context;
        
        public InsightsRepository(ApplicationDbContext context)
        {
            _context = context;
        }
        
        public async Task<EngagementInsights> GetLatestInsightsAsync(uint businessUnitId)
        {
            return await _context.EngagementInsights
                .Where(i => i.BusinessUnitId == businessUnitId)
                .OrderByDescending(i => i.GeneratedAt)
                .FirstOrDefaultAsync();
        }
        
        // Implement other methods...
    }
}
```

---

## 4. Controller Integration

### New InsightsController

```csharp
// Location: CloverBackoffice.API/Controllers/InsightsController.cs

namespace CloverBackoffice.API.Controllers
{
    [Authorize]
    [Route("api/insights")]
    [ApiController]
    public class InsightsController : ControllerBase
    {
        private readonly IInsightsGenerationService _insightsService;
        private readonly IBatchProcessingService _batchService;
        
        public InsightsController(
            IInsightsGenerationService insightsService,
            IBatchProcessingService batchService)
        {
            _insightsService = insightsService;
            _batchService = batchService;
        }
        
        [HttpPost("generate")]
        public async Task<IActionResult> GenerateInsights([FromBody] GenerateInsightsRequest request)
        {
            try
            {
                // Validate request
                if (!ModelState.IsValid)
                {
                    return BadRequest(ApiResponseHandler.Fail<string>(
                        ResponseMessages.Errors.InvalidRequest));
                }
                
                // Create batch job
                var job = await _batchService.CreateBatchJobAsync(
                    request.BusinessUnitId, 
                    request.PeriodDays
                );
                
                return Ok(ApiResponseHandler.Success(new
                {
                    JobId = job.Id,
                    Status = job.Status,
                    Message = "Insights generation initiated"
                }, ResponseMessages.Success.InsightsGenerationStarted));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate insights");
                return StatusCode(500, ApiResponseHandler.Fail<string>(
                    $"Internal server error: {ex.Message}"));
            }
        }
        
        // Additional endpoints...
    }
}
```

### Modify Existing DashboardController

```csharp
// Location: CloverBackoffice.API/Controllers/DashboardController.cs
// ADD to existing class:

[HttpGet("insights/{unitId}")]
public async Task<IActionResult> GetInsightsDashboard(long unitId)
{
    try
    {
        // Check for generated insights
        var insights = await _insightsService.GetLatestInsightsAsync((uint)unitId);
        
        if (insights == null)
        {
            // Fall back to existing method
            return await GetTopData(unitId);
        }
        
        // Return enhanced dashboard with insights
        var dashboard = _insightsService.BuildDashboard(insights);
        
        return Ok(ApiResponseHandler.Success(dashboard, 
            ResponseMessages.Success.DashboardGenerated));
    }
    catch (Exception ex)
    {
        return StatusCode(500, ApiResponseHandler.Fail<string>(
            $"Internal server error: {ex.Message}"));
    }
}
```

---

## 5. Dependency Injection Configuration

### Program.cs Updates

```csharp
// Location: CloverBackoffice.API/Program.cs
// ADD to service configuration:

// Insights Services
builder.Services.AddScoped<IBatchProcessingService, BatchProcessingService>();
builder.Services.AddScoped<IEmbeddingService, EmbeddingService>();
builder.Services.AddScoped<IInsightsGenerationService, InsightsGenerationService>();
builder.Services.AddScoped<IInsightsRepository, InsightsRepository>();

// Vector Search Service (Singleton for performance)
builder.Services.AddSingleton<IVectorSearchService>(provider =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    return new VectorSearchService(connectionString);
});

// Redis Cache
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis");
    options.InstanceName = "CloverInsights";
});

// OpenAI Batch Settings
builder.Services.Configure<OpenAIBatchSettings>(
    builder.Configuration.GetSection("OpenAIBatch"));

// Background Service for Batch Processing
builder.Services.AddHostedService<BatchProcessingBackgroundService>();
```

---

## 6. Configuration Updates

### appsettings.json Additions

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "existing...",
    "Redis": "localhost:6379,password=yourpassword"
  },
  "OpenAI": {
    "ApiKey": "existing...",
    "Model": "gpt-4-turbo-preview",
    "SystemPrompt": "existing..."
  },
  "OpenAIBatch": {
    "ApiKey": "same-or-different-key",
    "MaxTokensPerBatch": 50000,
    "CompletionWindow": "24h",
    "WebhookUrl": "https://yourapi.com/webhooks/batch",
    "EmbeddingModel": "text-embedding-3-small"
  },
  "InsightsGeneration": {
    "CacheExpirationHours": 24,
    "MinResponsesRequired": 10,
    "ConfidenceThreshold": 0.75,
    "WorksheetMappingPath": "Resources/worksheet-mappings.json"
  }
}
```

---

## 7. Database Migrations

### Create Migration

```bash
# In Package Manager Console
Add-Migration AddInsightsAutomationTables -Project CloverBackoffice.Repository
```

### Migration File

```csharp
// Location: CloverBackoffice.Repository/Migrations/[timestamp]_AddInsightsAutomationTables.cs

public partial class AddInsightsAutomationTables : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Enable pgvector extension
        migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS vector;");
        
        // Create EngagementInsights table
        migrationBuilder.CreateTable(
            name: "EngagementInsights",
            columns: table => new
            {
                Id = table.Column<int>(nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", 
                        NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                BusinessUnitId = table.Column<uint>(nullable: false),
                GeneratedAt = table.Column<DateTime>(nullable: false),
                PeriodStart = table.Column<DateTime>(nullable: false),
                PeriodEnd = table.Column<DateTime>(nullable: false),
                OverallScore = table.Column<decimal>(type: "decimal(5,2)", nullable: true),
                CategoryScores = table.Column<string>(type: "jsonb", nullable: true),
                TopThemes = table.Column<string>(type: "jsonb", nullable: true),
                Recommendations = table.Column<string>(type: "jsonb", nullable: true),
                StatisticalSummary = table.Column<string>(type: "jsonb", nullable: true),
                CreatedBy = table.Column<string>(maxLength: 255, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_EngagementInsights", x => x.Id);
                table.ForeignKey(
                    name: "FK_EngagementInsights_BusinessUnit",
                    column: x => x.BusinessUnitId,
                    principalTable: "BusinessUnit",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });
        
        // Create indexes
        migrationBuilder.CreateIndex(
            name: "IX_EngagementInsights_BusinessUnitId",
            table: "EngagementInsights",
            column: "BusinessUnitId");
        
        // Create other tables...
    }
    
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "EngagementInsights");
        // Drop other tables...
        migrationBuilder.Sql("DROP EXTENSION IF EXISTS vector;");
    }
}
```

---

## 8. Background Service Integration

### Batch Processing Background Service

```csharp
// Location: CloverBackoffice.Services/Background/BatchProcessingBackgroundService.cs

public class BatchProcessingBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BatchProcessingBackgroundService> _logger;
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var batchService = scope.ServiceProvider
                    .GetRequiredService<IBatchProcessingService>();
                
                // Check for completed batch jobs
                await batchService.ProcessCompletedJobsAsync();
            }
            
            // Check every 5 minutes
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
}
```

---

## 9. Testing Integration

### Unit Test Setup

```csharp
// Location: CloverBackoffice.Tests/Services/InsightsGenerationServiceTests.cs

[TestClass]
public class InsightsGenerationServiceTests
{
    private Mock<IChatGptRepository> _mockChatGptRepo;
    private Mock<IInsightsRepository> _mockInsightsRepo;
    private InsightsGenerationService _service;
    
    [TestInitialize]
    public void Setup()
    {
        _mockChatGptRepo = new Mock<IChatGptRepository>();
        _mockInsightsRepo = new Mock<IInsightsRepository>();
        _service = new InsightsGenerationService(
            _mockChatGptRepo.Object,
            _mockInsightsRepo.Object
        );
    }
    
    [TestMethod]
    public async Task GenerateInsights_ValidData_ReturnsInsights()
    {
        // Arrange
        var businessUnitId = 123u;
        var mockFeedback = GetMockFeedbackData();
        
        _mockInsightsRepo.Setup(x => x.GetFeedbackDataAsync(It.IsAny<uint>(), It.IsAny<DateTime>()))
            .ReturnsAsync(mockFeedback);
        
        // Act
        var result = await _service.GenerateInsightsAsync(businessUnitId, 30);
        
        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(businessUnitId, result.BusinessUnitId);
    }
}
```

---

## 10. Deployment Considerations

### Environment Variables
```bash
# Azure App Service Configuration
OPENAI_API_KEY=sk-...
REDIS_CONNECTION=...
PGVECTOR_ENABLED=true
BATCH_PROCESSING_ENABLED=true
```

### Feature Flags
```csharp
// Use feature flags for gradual rollout
if (_featureManager.IsEnabledAsync("InsightsAutomation").Result)
{
    // New insights code
}
else
{
    // Existing code
}
```

---

## Common Integration Patterns

### 1. Reuse Existing Helpers
```csharp
// Use existing ApiResponseHandler
return Ok(ApiResponseHandler.Success(data, message));

// Use existing ResponseMessages
ResponseMessages.Success.InsightsGenerated
```

### 2. Follow Existing Naming Conventions
```csharp
// DTOs in CloverBackofficeInfrastructure/DTO/
public class InsightsRequestDto { }

// Models in CloverBackofficeInfrastructure/Models/
public class EngagementInsights { }

// Services use I{Name}Service pattern
public interface IInsightsGenerationService { }
```

### 3. Maintain Consistent Error Handling
```csharp
try
{
    // Business logic
}
catch (Exception ex)
{
    _logger.LogError(ex, "Error message");
    return StatusCode(500, ApiResponseHandler.Fail<string>(
        $"Internal server error: {ex.Message}"));
}
```

---

## Troubleshooting Guide

### Common Issues

1. **pgvector Extension Not Found**
   - Solution: Install pgvector on PostgreSQL server
   - Command: `CREATE EXTENSION vector;`

2. **Redis Connection Failed**
   - Check Redis server is running
   - Verify connection string in appsettings.json

3. **OpenAI Rate Limits**
   - Implement exponential backoff
   - Use batch API for bulk operations

4. **Migration Conflicts**
   - Resolve with: `Update-Database -TargetMigration [previous]`
   - Then reapply: `Update-Database`

---

## Next Steps

1. **Phase 1:** Implement core services
2. **Phase 2:** Add batch processing
3. **Phase 3:** Integrate embeddings
4. **Phase 4:** Connect to UI
5. **Phase 5:** Testing and optimization

---

**Document Version:** 1.0  
**Compatibility:** CloverBackoffice v2.x  
**Author:** Integration Team  
**Support:** Contact Muhammad or Technical Lead