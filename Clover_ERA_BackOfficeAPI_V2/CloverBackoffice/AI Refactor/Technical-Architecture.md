# Clover ERA Insights Automation - Technical Architecture

**Version:** 1.0  
**Date:** August 16, 2025  
**Author:** Technical Architecture Team

---

## Table of Contents
1. [System Overview](#system-overview)
2. [Architecture Principles](#architecture-principles)
3. [Component Architecture](#component-architecture)
4. [Data Architecture](#data-architecture)
5. [Integration Architecture](#integration-architecture)
6. [Security Architecture](#security-architecture)
7. [Deployment Architecture](#deployment-architecture)
8. [Technology Stack](#technology-stack)

---

## System Overview

The Clover ERA Insights Automation system extends the existing CloverBackoffice API with intelligent insights generation capabilities using OpenAI's Batch API and embedding technologies.

### High-Level Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                     Client Applications                      │
│          (React Frontend, Mobile Apps, Partner APIs)         │
└─────────────────────────┬───────────────────────────────────┘
                          │ HTTPS/REST
┌─────────────────────────▼───────────────────────────────────┐
│                  CloverBackoffice API Layer                  │
│  ┌─────────────────────────────────────────────────────┐    │
│  │            Enhanced Controllers                      │    │
│  │  • DashboardController                              │    │
│  │  • InsightsController (NEW)                         │    │
│  │  • BatchJobController (NEW)                         │    │
│  └─────────────────────┬───────────────────────────────┘    │
└────────────────────────┼────────────────────────────────────┘
                         │
┌────────────────────────▼────────────────────────────────────┐
│                    Service Layer                             │
│  ┌──────────────────────────────────────────────────────┐   │
│  │         Existing Services          │  New Services    │   │
│  │  • FeedbackAnalysisService        │ • BatchService   │   │
│  │  • ChatGptRepository              │ • EmbeddingService│   │
│  │  • UserService                    │ • InsightsService│   │
│  └──────────────────────────────────────────────────────┘   │
└─────────────────────────┬───────────────────────────────────┘
                          │
┌─────────────────────────▼───────────────────────────────────┐
│                    Data Layer                                │
│  ┌───────────────┐  ┌────────────────┐  ┌──────────────┐   │
│  │  PostgreSQL   │  │  Redis Cache   │  │ Azure Blob   │   │
│  │   Database    │  │  (Embeddings)  │  │  (Reports)   │   │
│  └───────────────┘  └────────────────┘  └──────────────┘   │
└─────────────────────────┬───────────────────────────────────┘
                          │
┌─────────────────────────▼───────────────────────────────────┐
│                 External Services                            │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐     │
│  │  OpenAI API  │  │  SendGrid    │  │  Azure AD    │     │
│  │  (Batch/Emb) │  │  (Email)     │  │  (Auth)      │     │
│  └──────────────┘  └──────────────┘  └──────────────┘     │
└──────────────────────────────────────────────────────────────┘
```

---

## Architecture Principles

### 1. Separation of Concerns
- Clear boundaries between layers
- Single responsibility for each service
- Minimal coupling between components

### 2. Scalability
- Horizontal scaling for batch processing
- Async processing for long-running operations
- Caching strategy for embeddings

### 3. Maintainability
- Following existing CloverBackoffice patterns
- Consistent coding standards (C# conventions)
- Comprehensive logging and monitoring

### 4. Security First
- Zero-trust architecture
- Encryption at rest and in transit
- Principle of least privilege

---

## Component Architecture

### Core Services

#### 1. BatchProcessingService
```csharp
namespace CloverBackoffice.Services.Interfaces
{
    public interface IBatchProcessingService
    {
        Task<BatchJob> CreateBatchJobAsync(uint businessUnitId, int periodDays);
        Task ProcessCompletedJobsAsync();
    }
}
```

**Responsibilities:**
- Manage OpenAI Batch API lifecycle
- Handle JSONL file generation
- Monitor batch job status
- Process and store results

#### 2. EmbeddingService
```csharp
namespace CloverBackoffice.Services.Interfaces
{
    public interface IEmbeddingService
    {
        Task<float[]> GenerateEmbeddingAsync(string text);
    }
}
```

**Responsibilities:**
- Generate text embeddings
- Manage vector database
- Perform similarity searches
- Maintain knowledge base

#### 3. InsightsGenerationService
```csharp
namespace CloverBackoffice.Services.Interfaces
{
    public interface IInsightsGenerationService
    {
        Task<EngagementInsights> GetLatestInsightsAsync(uint businessUnitId);
    }
}
```

**Responsibilities:**
- Orchestrate insights generation
- Apply business logic and rules
- Format output for dashboards
- Generate comprehensive reports

### Supporting Components

#### 4. PromptTemplateService
- Manage prompt templates
- Variable substitution
- Context injection
- Version control for prompts

#### 5. VectorSearchService
- Efficient similarity search
- Index management
- Query optimization
- Result ranking

#### 6. WorksheetRecommendationEngine
- Map scores to worksheets
- Priority ranking
- Personalization logic
- Track recommendation history

---

## Data Architecture

### Database Schema Extensions

#### New Tables

```sql
-- Stores generated insights for caching and history
CREATE TABLE EngagementInsights (
    Id SERIAL PRIMARY KEY,
    BusinessUnitId INTEGER NOT NULL,
    GeneratedAt TIMESTAMP NOT NULL,
    PeriodStart DATE NOT NULL,
    PeriodEnd DATE NOT NULL,
    OverallScore DECIMAL(5,2),
    CategoryScores JSONB,
    TopThemes JSONB,
    Recommendations JSONB,
    StatisticalSummary JSONB,
    CreatedBy VARCHAR(255),
    FOREIGN KEY (BusinessUnitId) REFERENCES BusinessUnit(Id)
);

-- Tracks OpenAI batch processing jobs
CREATE TABLE BatchJobs (
    Id UUID PRIMARY KEY,
    OpenAIJobId VARCHAR(255) UNIQUE NOT NULL,
    Status VARCHAR(50) NOT NULL,
    RequestType VARCHAR(100),
    SubmittedAt TIMESTAMP NOT NULL,
    CompletedAt TIMESTAMP,
    InputFileUrl TEXT,
    OutputFileUrl TEXT,
    ErrorMessage TEXT,
    Metadata JSONB
);

-- Stores document embeddings for knowledge base
CREATE TABLE EmbeddingVectors (
    Id SERIAL PRIMARY KEY,
    DocumentId VARCHAR(255) UNIQUE NOT NULL,
    DocumentType VARCHAR(50) NOT NULL, -- 'book', 'worksheet', 'custom'
    Title VARCHAR(500),
    Content TEXT,
    Vector vector(1536), -- Using pgvector extension
    Metadata JSONB,
    CreatedAt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Maps insights to worksheet recommendations
CREATE TABLE WorksheetRecommendations (
    Id SERIAL PRIMARY KEY,
    InsightId INTEGER NOT NULL,
    WorksheetId VARCHAR(255) NOT NULL,
    RelevanceScore DECIMAL(5,2),
    Justification TEXT,
    Priority INTEGER,
    FOREIGN KEY (InsightId) REFERENCES EngagementInsights(Id)
);

-- Indexes for performance
CREATE INDEX idx_insights_business_unit ON EngagementInsights(BusinessUnitId);
CREATE INDEX idx_insights_period ON EngagementInsights(PeriodStart, PeriodEnd);
CREATE INDEX idx_batch_jobs_status ON BatchJobs(Status);
CREATE INDEX idx_embeddings_vector ON EmbeddingVectors USING ivfflat (Vector vector_cosine_ops);
```

### Data Flow

```
User Request → API Controller → Service Layer
                                    ↓
                            Data Aggregation
                                    ↓
                            Batch Job Creation
                                    ↓
                            OpenAI Processing
                                    ↓
                            Result Processing
                                    ↓
                            Database Storage
                                    ↓
                            Response to User
```

---

## Integration Architecture

### Integration with Existing CloverBackoffice

#### 1. Service Registration (Program.cs)
```csharp
// Add new services to DI container
builder.Services.AddScoped<IInsightsRepository, InsightsRepository>();
builder.Services.AddScoped<IBatchProcessingService, BatchProcessingService>();
builder.Services.AddScoped<IEmbeddingService, EmbeddingService>();
builder.Services.AddScoped<IInsightsGenerationService, InsightsGenerationService>();
```

#### 2. Repository Pattern Extension
```csharp
public interface IInsightsRepository
{
    Task<EngagementInsights> GetLatestInsightsAsync(uint businessUnitId);
    Task<List<EngagementInsights>> GetInsightsHistoryAsync(uint businessUnitId, int days, int page = 1, int pageSize = 10);
    Task SaveInsightsAsync(EngagementInsights insights);
    Task<BatchJob> SaveBatchJobAsync(BatchJob job);
    Task<BatchJob> GetBatchJobAsync(Guid jobId);
    Task UpdateBatchJobStatusAsync(Guid jobId, string status, DateTime? completedAt = null, string errorMessage = null, string outputFileUrl = null);
}
```

#### 3. Controller Integration
```csharp
[Authorize]
[Route("api/insights")]
[ApiController]
public class InsightsController : ControllerBase
{
    private readonly IInsightsGenerationService _insightsService;
    private readonly IBatchProcessingService _batchService;
    private readonly ILogger<InsightsController> _logger;

    public InsightsController(
        IInsightsGenerationService insightsService,
        IBatchProcessingService batchService,
        ILogger<InsightsController> logger)
    {
        _insightsService = insightsService;
        _batchService = batchService;
        _logger = logger;
    }

    public class GenerateInsightsRequest
    {
        [Required]
        public uint BusinessUnitId { get; set; }
        [Range(1, 365)]
        public int PeriodDays { get; set; } = 30;
    }

    [HttpPost("generate")]
    public async Task<IActionResult> Generate([FromBody] GenerateInsightsRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ApiResponseHandler.Fail<string>("Invalid request"));
        }

        var job = await _batchService.CreateBatchJobAsync(request.BusinessUnitId, request.PeriodDays);
        return Ok(ApiResponseHandler.Success(new { jobId = job.Id, status = job.Status, message = "Insights generation initiated" }));
    }
}
```

---

## Security Architecture

### Authentication & Authorization
- Leverage existing JWT authentication
- Role-based access control (RBAC)
- API key management for external services

### Data Security
```yaml
Encryption:
  - At Rest: AES-256 for database
  - In Transit: TLS 1.3 minimum
  - Keys: Azure Key Vault managed

Data Privacy:
  - PII masking in logs
  - Data retention policies
  - GDPR compliance
  - Audit logging
```

### API Security
- Rate limiting per endpoint
- Request validation
- SQL injection prevention
- XSS protection

---

## Deployment Architecture

### Azure Infrastructure

```yaml
Production Environment:
  App Service:
    - Plan: P2v3 (4 vCores, 14 GB RAM)
    - Instances: 2-5 (auto-scaling)
    - Region: East US 2
  
  Database:
    - Azure Database for PostgreSQL
    - Version: 14
    - SKU: General Purpose, 4 vCores
    - Storage: 100 GB SSD
    - Backup: Geo-redundant, 30 days
  
  Redis Cache:
    - SKU: Standard C2 (2.5 GB)
    - Purpose: Embedding cache
  
  Storage:
    - Azure Blob Storage
    - Tier: Hot
    - Purpose: Batch files, reports
  
  Monitoring:
    - Application Insights
    - Log Analytics Workspace
    - Azure Monitor Alerts
```

### CI/CD Pipeline

```yaml
Pipeline Stages:
  1. Build:
     - Restore NuGet packages
     - Build solution
     - Run unit tests
     
  2. Test:
     - Integration tests
     - API tests
     - Security scanning
     
  3. Deploy to Staging:
     - Deploy to staging slot
     - Run smoke tests
     - Performance tests
     
  4. Deploy to Production:
     - Blue-green deployment
     - Health checks
     - Rollback capability
```

---

## Technology Stack

### Core Technologies
- **Framework:** .NET 8.0 / ASP.NET Core
- **Language:** C# 12
- **Database:** PostgreSQL 14 with pgvector
- **Cache:** Redis 7.0
- **Queue:** Azure Service Bus

### AI/ML Stack
- **LLM:** OpenAI GPT-4 Turbo
- **Embeddings:** text-embedding-3-small
- **Batch Processing:** OpenAI Batch API
- **Vector Search:** pgvector with IVFFlat index

### Infrastructure
- **Cloud:** Microsoft Azure
- **Container:** Docker (optional)
- **Orchestration:** Azure Container Instances
- **Monitoring:** Application Insights
- **Logging:** Serilog + Azure Log Analytics

### Development Tools
- **IDE:** Visual Studio 2022 / VS Code
- **Version Control:** Git / Azure DevOps
- **API Testing:** Postman / Swagger
- **Unit Testing:** xUnit
- **Mocking:** Moq

---

## Performance Considerations

### Caching Strategy
```csharp
// Multi-level caching
Level 1: In-memory cache (5 minutes)
Level 2: Redis cache (1 hour)
Level 3: Database cache (24 hours)
```

### Batch Processing Optimization
- Process in chunks of 50,000 tokens
- Parallel processing where applicable
- Async/await throughout
- Connection pooling

### Database Optimization
- Appropriate indexes on all foreign keys
- Partitioning for large tables
- Query optimization
- Connection pooling

---

## Monitoring & Observability

### Key Metrics
```yaml
Application Metrics:
  - Request latency (p50, p95, p99)
  - Error rates by endpoint
  - Active users
  - Batch job completion time

Business Metrics:
  - Insights generated per day
  - Worksheet recommendations accepted
  - User engagement with insights
  - Cost per insight generated

Infrastructure Metrics:
  - CPU/Memory utilization
  - Database connection pool
  - Redis hit rate
  - OpenAI API usage
```

### Logging Strategy
```csharp
// Structured logging with Serilog
Log.Information("Batch job {JobId} completed in {Duration}ms", 
    jobId, duration);

// Correlation IDs for request tracing
// Error logging with context
// Performance logging for slow operations
```

---

## Disaster Recovery

### Backup Strategy
- Database: Daily automated backups, 30-day retention
- Code: Git repository with tagged releases
- Configuration: Azure Key Vault with versioning
- Documents: Blob storage with soft delete

### Recovery Procedures
- RTO (Recovery Time Objective): 4 hours
- RPO (Recovery Point Objective): 24 hours
- Automated failover for critical services
- Manual runbooks for complex scenarios

---

## Future Considerations

### Potential Enhancements
1. Real-time insights using streaming
2. Multi-language support
3. Custom ML models for predictions
4. GraphQL API option
5. Webhook notifications

### Scalability Path
1. Microservices architecture
2. Event-driven processing
3. Kubernetes orchestration
4. Global distribution

---

**Document Version:** 1.0  
**Last Updated:** August 16, 2025  
**Next Review:** September 2025