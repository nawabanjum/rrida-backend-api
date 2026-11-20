using System;
using System.Collections.Generic;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class CreatePromptTemplateDto
    {
        public int? Id { get; set; }
        public string Name { get; set; } = "";
        public string Version { get; set; } = "";
        public string Description { get; set; } = "";
        public string SystemPrompt { get; set; } = "";
        public string Template { get; set; } = "";
        public bool ActivateImmediately { get; set; }
        public string? MetadataJson { get; set; }
    }

    public class PromptTemplateResponseDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Version { get; set; } = "";
        public string Description { get; set; } = "";
        public string SystemPrompt { get; set; } = "";
        public string Template { get; set; } = "";
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? CreatedBy { get; set; }
        public string? MetadataJson { get; set; }
        public int CharCount { get; set; }
        public int EstimatedTokenCount { get; set; }
    }

    public class EstimateTokensRequestDto
    {
        public string Text { get; set; } = "";
    }

    public class EstimateTokensResponseDto
    {
        public int CharCount { get; set; }
        public int EstimatedTokenCount { get; set; }
    }

    public class ActivatePromptDto
    {
        public string Name { get; set; } = "";
        public string Version { get; set; } = "";
    }

    public class RenderPromptRequestDto
    {
        public string PromptName { get; set; } = "";
        public Dictionary<string, string> Variables { get; set; } = new();
    }

    // ── Workbench Run ──

    public class WorkbenchRunRequestDto
    {
        public int PromptId { get; set; }
        public List<int> ModelIds { get; set; } = new();
        public string? ManualTestCaseJson { get; set; }          // JSON array of InlineTestCaseDto
        public double? Temperature { get; set; }
        public int? MaxTokens { get; set; }
        public double? TopP { get; set; }
        // Optional feedback-source filters. When ProfileId, UnitId and the date range
        // are all provided, the API fetches feedback data using these IDs instead of
        // requiring an uploaded test case file.
        public long? ProfileId { get; set; }
        public long? UnitId { get; set; }
        public DateTimeOffset? FromDate { get; set; }
        public DateTimeOffset? ToDate { get; set; }
    }

    public class InlineTestCaseDto
    {
        public string Name { get; set; } = "";
        public string InputDataJson { get; set; } = "";
        public List<InlineAssertionDto> Assertions { get; set; } = new();
    }

    public class InlineAssertionDto
    {
        public string Name { get; set; } = "";
        public string AssertionType { get; set; } = "";
        public string? JsonPath { get; set; }
        public string? Operator { get; set; }
        public string? ExpectedValue { get; set; }
    }

    public class WorkbenchRunResultDto
    {
        public int PromptId { get; set; }
        public string PromptName { get; set; } = "";
        public List<WorkbenchRunItemDto> Results { get; set; } = new();
    }

    public class WorkbenchRunItemDto
    {
        public int ModelId { get; set; }
        public string ModelName { get; set; } = "";
        public string Provider { get; set; } = "";
        public int TestCaseId { get; set; }
        public string TestCaseName { get; set; } = "";
        public string Response { get; set; } = "";
        public int PromptTokens { get; set; }
        public int CompletionTokens { get; set; }
        public int TotalTokens { get; set; }
        public long LatencyMs { get; set; }
        public string? Error { get; set; }
        public List<WorkbenchAssertionResultDto> Assertions { get; set; } = new();
    }

    public class WorkbenchAssertionResultDto
    {
        public int AssertionId { get; set; }
        public string Name { get; set; } = "";
        public string AssertionType { get; set; } = "";
        public string? ExpectedValue { get; set; }
        public bool Passed { get; set; }
        public string? ActualValue { get; set; }
    }

    // ── Promptfoo Evaluation Results ──

    public class PromptfooEvalResultDto
    {
        public int PromptId { get; set; }
        public string PromptName { get; set; } = "";
        public PromptfooEvalSummaryDto Summary { get; set; } = new();
        public List<PromptfooTestResultDto> TestResults { get; set; } = new();
        public string? ShareUrl { get; set; }
        public string? DashboardUrl { get; set; }
    }

    public class PromptfooEvalSummaryDto
    {
        public int TotalTests { get; set; }
        public int TotalPassed { get; set; }
        public int TotalFailed { get; set; }
        public int TotalErrors { get; set; }
        public double PassRate { get; set; }
        public decimal TotalEstimatedCost { get; set; }
        public int TotalPromptTokens { get; set; }
        public int TotalCompletionTokens { get; set; }
        public int TotalTokens { get; set; }
    }

    public class PromptfooTestResultDto
    {
        public string TestCaseName { get; set; } = "";
        public string ProviderName { get; set; } = "";
        public string ModelName { get; set; } = "";
        public string Response { get; set; } = "";
        public string RenderedPrompt { get; set; } = "";
        public bool Pass { get; set; }
        public double Score { get; set; }
        public long LatencyMs { get; set; }
        public int PromptTokens { get; set; }
        public int CompletionTokens { get; set; }
        public int TotalTokens { get; set; }
        public decimal EstimatedCost { get; set; }
        public Dictionary<string, string> Variables { get; set; } = new();
        public string? Error { get; set; }
        public List<PromptfooAssertionResultDto> Assertions { get; set; } = new();
    }

    public class PromptfooAssertionResultDto
    {
        public string Type { get; set; } = "";
        public bool Pass { get; set; }
        public double Score { get; set; }
        public string? Expected { get; set; }
        public string? Actual { get; set; }
        public string? Reason { get; set; }
    }
}
