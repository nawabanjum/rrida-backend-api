# Clover ERA Insights Automation - API Specification

**Version:** 1.0  
**Base URL:** `https://api.cloverera.com/api`  
**Authentication:** Bearer JWT Token

---

## Table of Contents
1. [Overview](#overview)
2. [Authentication](#authentication)
3. [Insights Endpoints](#insights-endpoints)
4. [Batch Processing Endpoints](#batch-processing-endpoints)
5. [Embedding Management Endpoints](#embedding-management-endpoints)
6. [Dashboard Endpoints](#dashboard-endpoints)
7. [Worksheet Endpoints](#worksheet-endpoints)
8. [Models](#models)
9. [Error Responses](#error-responses)

---

## Overview

This API extends the existing CloverBackoffice API with intelligent insights generation capabilities. All endpoints follow RESTful conventions and return JSON responses.

### API Versioning
- Current Version: v1
- Version in URL: `/api/v1/insights`
- Header: `X-API-Version: 1.0`

### Rate Limiting
- Default: 1000 requests per hour
- Batch operations: 10 requests per hour
- Embedding operations: 100 requests per hour

---

## Authentication

All endpoints require authentication using the existing CloverBackoffice JWT authentication system.

### Request Headers
```http
Authorization: Bearer {jwt_token}
Content-Type: application/json
X-Company-Id: {company_id}
X-Unit-Id: {unit_id} (optional)
```

---

## Insights Endpoints

### Generate Insights for Business Unit

**POST** `/api/insights/generate`

Triggers insights generation for a specific business unit.

#### Request Body
```json
{
  "businessUnitId": 12345,
  "periodDays": 30,
  "includeRecommendations": true,
  "includeActionPlan": true,
  "categories": ["communication", "learning", "opportunity", "vulnerability", "enablement", "reflection"]
}
```

#### Response
```json
{
  "success": true,
  "data": {
    "jobId": "batch_abc123xyz",
    "status": "processing",
    "estimatedCompletionTime": "2025-08-16T15:30:00Z",
    "message": "Insights generation initiated successfully"
  }
}
```

### Get Insights Status

**GET** `/api/insights/status/{jobId}`

Check the status of an insights generation job.

#### Response
```json
{
  "success": true,
  "data": {
    "jobId": "batch_abc123xyz",
    "status": "completed",
    "progress": 100,
    "completedAt": "2025-08-16T15:28:00Z",
    "resultUrl": "/api/insights/results/batch_abc123xyz"
  }
}
```

### Retrieve Generated Insights

**GET** `/api/insights/results/{jobId}`

Get the completed insights for a job.

#### Response
```json
{
  "success": true,
  "data": {
    "businessUnitId": 12345,
    "generatedAt": "2025-08-16T15:28:00Z",
    "periodStart": "2025-07-16",
    "periodEnd": "2025-08-16",
    "overallScore": 68.5,
    "categoryScores": {
      "communication": {
        "score": 48,
        "status": "needs_work",
        "trend": "declining"
      },
      "learning": {
        "score": 18,
        "status": "urgent_attention",
        "trend": "stable"
      },
      "opportunity": {
        "score": 59,
        "status": "needs_work",
        "trend": "improving"
      },
      "vulnerability": {
        "score": 43,
        "status": "needs_work",
        "trend": "stable"
      },
      "enablement": {
        "score": 89,
        "status": "good",
        "trend": "improving"
      },
      "reflection": {
        "score": 42,
        "status": "needs_work",
        "trend": "declining"
      }
    },
    "topThemes": [
      {
        "theme": "Limited Growth and Development Opportunities",
        "frequency": 45,
        "sentiment": "negative"
      },
      {
        "theme": "Time and Priority Constraints for Learning",
        "frequency": 38,
        "sentiment": "negative"
      },
      {
        "theme": "Communication Gaps and Mixed Experiences",
        "frequency": 32,
        "sentiment": "mixed"
      }
    ],
    "recommendations": [
      {
        "priority": 1,
        "category": "learning",
        "title": "Implement Structured Learning Sessions",
        "description": "Learning scores are critically low. Implement regular learning sessions.",
        "worksheetId": "WS-L2",
        "worksheetTitle": "Peer-to-Peer Learning Sessions",
        "estimatedImpact": "high"
      }
    ],
    "statisticalSummary": {
      "responseRate": 78.5,
      "totalResponses": 156,
      "variability": "moderate",
      "confidence": 95
    }
  }
}
```

### Get Historical Insights

**GET** `/api/insights/history`

Retrieve historical insights for a business unit.

#### Query Parameters
- `businessUnitId` (required): Business unit identifier
- `days` (optional): Number of days of history (default: 90)
- `page` (optional): Page number (default: 1)
- `pageSize` (optional): Results per page (default: 10)

#### Response
```json
{
  "success": true,
  "data": {
    "insights": [
      {
        "id": "ins_123",
        "generatedAt": "2025-08-16T00:00:00Z",
        "overallScore": 68.5,
        "trend": "improving"
      }
    ],
    "pagination": {
      "page": 1,
      "pageSize": 10,
      "totalPages": 5,
      "totalRecords": 48
    }
  }
}
```

---

## Batch Processing Endpoints

### Submit Batch Job

**POST** `/api/batch/submit`

Submit a batch processing job to OpenAI.

#### Request Body
```json
{
  "type": "insights_generation",
  "priority": "standard",
  "data": {
    "businessUnitIds": [123, 124, 125],
    "periodDays": 30
  },
  "notificationEmail": "admin@company.com"
}
```

#### Response
```json
{
  "success": true,
  "data": {
    "batchId": "batch_abc123",
    "status": "submitted",
    "estimatedCompletion": "2025-08-17T00:00:00Z",
    "cost": {
      "estimated": "$12.50",
      "tokens": 250000
    }
  }
}
```

### Get Batch Status

**GET** `/api/batch/status/{batchId}`

Check the status of a batch job.

#### Response
```json
{
  "success": true,
  "data": {
    "batchId": "batch_abc123",
    "status": "in_progress",
    "progress": {
      "completed": 2,
      "total": 3,
      "percentage": 66.67
    },
    "startedAt": "2025-08-16T12:00:00Z"
  }
}
```

### Cancel Batch Job

**DELETE** `/api/batch/cancel/{batchId}`

Cancel a running batch job.

#### Response
```json
{
  "success": true,
  "message": "Batch job cancelled successfully"
}
```

### List Batch Jobs

**GET** `/api/batch/list`

List all batch jobs for the organization.

#### Query Parameters
- `status` (optional): Filter by status (pending, processing, completed, failed)
- `from` (optional): Start date (ISO 8601)
- `to` (optional): End date (ISO 8601)

---

## Embedding Management Endpoints

### Update Knowledge Base

**POST** `/api/embeddings/knowledge-base/update`

Upload and process documents for the knowledge base.

#### Request Body (multipart/form-data)
```
file: [PDF file]
type: "book" | "worksheet" | "custom"
metadata: {
  "title": "Document Title",
  "version": "1.0",
  "tags": ["tag1", "tag2"]
}
```

#### Response
```json
{
  "success": true,
  "data": {
    "documentId": "doc_789xyz",
    "chunksProcessed": 45,
    "embeddingsGenerated": 45,
    "processingTime": 8500
  }
}
```

### Search Similar Content

**POST** `/api/embeddings/search`

Search for similar content in the knowledge base.

#### Request Body
```json
{
  "query": "How to improve team communication",
  "topK": 5,
  "documentTypes": ["book", "worksheet"],
  "minSimilarity": 0.7
}
```

#### Response
```json
{
  "success": true,
  "data": {
    "results": [
      {
        "documentId": "doc_123",
        "title": "Chapter 3: Communication in Teams",
        "content": "Effective communication strategies...",
        "similarity": 0.89,
        "metadata": {
          "page": 45,
          "section": "3.2"
        }
      }
    ]
  }
}
```

### List Documents

**GET** `/api/embeddings/documents`

List all documents in the knowledge base.

#### Response
```json
{
  "success": true,
  "data": {
    "documents": [
      {
        "id": "doc_123",
        "title": "The Neuroscience of Employee Engagement",
        "type": "book",
        "chunks": 145,
        "uploadedAt": "2025-08-01T00:00:00Z"
      }
    ]
  }
}
```

---

## Dashboard Endpoints

### Get Dashboard Data

**GET** `/api/dashboard/insights/{businessUnitId}`

Get the latest dashboard data for a business unit.

#### Query Parameters
- `includeHistory` (optional): Include historical data (default: false)
- `format` (optional): Response format (json, pdf, excel)

#### Response
```json
{
  "success": true,
  "data": {
    "dashboardId": "dash_456",
    "businessUnit": {
      "id": 12345,
      "name": "XYZ School",
      "employeeCount": 200
    },
    "engagementScore": {
      "overall": 68.5,
      "previousMonth": 65.2,
      "change": 3.3
    },
    "categories": {
      "communication": { /* category data */ },
      "learning": { /* category data */ }
    },
    "charts": {
      "trend": { /* chart data */ },
      "distribution": { /* chart data */ }
    },
    "recommendations": [ /* recommendations */ ],
    "generatedAt": "2025-08-16T15:30:00Z"
  }
}
```

### Export Dashboard

**POST** `/api/dashboard/export`

Export dashboard as PDF or Excel.

#### Request Body
```json
{
  "businessUnitId": 12345,
  "format": "pdf",
  "includeRawData": false,
  "emailTo": "manager@company.com"
}
```

---

## Worksheet Endpoints

### Get Worksheet Recommendations

**GET** `/api/worksheets/recommendations/{businessUnitId}`

Get worksheet recommendations based on engagement scores.

#### Response
```json
{
  "success": true,
  "data": {
    "recommendations": [
      {
        "worksheetId": "WS-L2",
        "title": "Peer-to-Peer Learning Sessions",
        "category": "learning",
        "relevanceScore": 0.92,
        "description": "Structured approach to peer learning",
        "estimatedDuration": "45 minutes",
        "targetIssues": [
          "Low learning scores",
          "Knowledge sharing gaps"
        ],
        "downloadUrl": "/api/worksheets/download/WS-L2"
      }
    ]
  }
}
```

### Download Worksheet

**GET** `/api/worksheets/download/{worksheetId}`

Download a specific worksheet.

#### Response
Binary PDF file with appropriate headers.

### List All Worksheets

**GET** `/api/worksheets/list`

Get a list of all available worksheets.

#### Query Parameters
- `category` (optional): Filter by CLOVER category
- `searchTerm` (optional): Search in title and description

---

## Models

### Common Models

#### EngagementScore
```typescript
interface EngagementScore {
  score: number;           // 0-100
  status: 'excellent' | 'good' | 'needs_work' | 'urgent_attention';
  trend: 'improving' | 'stable' | 'declining';
  confidence: number;      // 0-100
}
```

#### CategoryData
```typescript
interface CategoryData {
  name: string;
  score: EngagementScore;
  topIssues: string[];
  suggestedActions: string[];
  historicalScores: number[];
}
```

#### Recommendation
```typescript
interface Recommendation {
  id: string;
  priority: number;
  category: string;
  title: string;
  description: string;
  worksheetId?: string;
  estimatedImpact: 'low' | 'medium' | 'high';
  implementationTime: string;
}
```

#### BatchJob
```typescript
interface BatchJob {
  id: string;
  openAIJobId: string;
  status: 'pending' | 'processing' | 'completed' | 'failed';
  type: string;
  submittedAt: string;
  completedAt?: string;
  error?: string;
  metadata: object;
}
```

---

## Error Responses

### Standard Error Response
```json
{
  "success": false,
  "error": {
    "code": "INSIGHTS_001",
    "message": "Failed to generate insights",
    "details": "Invalid business unit ID",
    "timestamp": "2025-08-16T12:00:00Z",
    "requestId": "req_abc123"
  }
}
```

### Error Codes

| Code | Description | HTTP Status |
|------|-------------|------------|
| AUTH_001 | Authentication failed | 401 |
| AUTH_002 | Insufficient permissions | 403 |
| INSIGHTS_001 | Insights generation failed | 500 |
| INSIGHTS_002 | Invalid parameters | 400 |
| BATCH_001 | Batch job failed | 500 |
| BATCH_002 | Batch job not found | 404 |
| EMBED_001 | Embedding generation failed | 500 |
| EMBED_002 | Document processing failed | 400 |
| RATE_001 | Rate limit exceeded | 429 |
| VALIDATION_001 | Validation error | 400 |

---

## Rate Limiting

### Headers
```http
X-RateLimit-Limit: 1000
X-RateLimit-Remaining: 999
X-RateLimit-Reset: 1692198400
```

### Rate Limit Response
```json
{
  "success": false,
  "error": {
    "code": "RATE_001",
    "message": "Rate limit exceeded",
    "retryAfter": 3600
  }
}
```

---

## Webhooks

### Webhook Events

#### Batch Job Completed
```json
{
  "event": "batch.completed",
  "timestamp": "2025-08-16T15:30:00Z",
  "data": {
    "batchId": "batch_123",
    "status": "completed",
    "resultsUrl": "https://api.cloverera.com/api/batch/results/batch_123"
  }
}
```

#### Insights Generated
```json
{
  "event": "insights.generated",
  "timestamp": "2025-08-16T15:30:00Z",
  "data": {
    "businessUnitId": 12345,
    "insightsId": "ins_456",
    "overallScore": 68.5
  }
}
```

---

## SDK Support

### C# Client Example
```csharp
var client = new CloverInsightsClient(apiKey);
var insights = await client.Insights.GenerateAsync(
    businessUnitId: 12345,
    periodDays: 30
);
```

### TypeScript Client Example
```typescript
const client = new CloverInsightsClient({ apiKey });
const insights = await client.insights.generate({
  businessUnitId: 12345,
  periodDays: 30
});
```

---

## Changelog

### Version 1.0 (August 2025)
- Initial release
- Insights generation endpoints
- Batch processing support
- Embedding management
- Dashboard APIs
- Worksheet recommendations

---

**API Version:** 1.0  
**Last Updated:** August 16, 2025  
**Contact:** api-support@cloverera.com