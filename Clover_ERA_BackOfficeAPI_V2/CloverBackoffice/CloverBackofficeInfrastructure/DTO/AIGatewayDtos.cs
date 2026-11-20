using System;
using System.Collections.Generic;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class AIGatewayRequestDto
    {
        public string? Model { get; set; }
        public string SystemPrompt { get; set; } = "";
        public string UserContent { get; set; } = "";
        public double Temperature { get; set; } = 0.7;
        public int MaxTokens { get; set; } = 1024;
        public double TopP { get; set; } = 1.0;
        public bool EnforceJson { get; set; } = true;
    }

    public class AIGatewayResponseDto
    {
        public string Content { get; set; } = "";
        public string ModelUsed { get; set; } = "";
        public long LatencyMs { get; set; }
        public TokenUsageDto Usage { get; set; } = new();
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public bool FallbackUsed { get; set; }
    }

    public class TokenUsageDto
    {
        public int PromptTokens { get; set; }
        public int CompletionTokens { get; set; }
        public int TotalTokens => PromptTokens + CompletionTokens;
    }

    public class AIGatewayTestRequestDto
    {
        public string? PromptName { get; set; }
        public string? Model { get; set; }
        public string UserContent { get; set; } = "";
        public bool ValidateResponse { get; set; } = true;
    }
}
