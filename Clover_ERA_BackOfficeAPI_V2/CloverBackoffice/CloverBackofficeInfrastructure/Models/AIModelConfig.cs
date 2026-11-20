using System;
using System.ComponentModel.DataAnnotations;

namespace CloverBackoffice.Infrastructure.Models
{
    public class AIModelConfig
    {
        [Key]
        public int Id { get; set; }

        /// <summary>Links to PromptTemplate.Name, e.g. "clover-era-insights"</summary>
        public string PromptName { get; set; } = "";

        /// <summary>LLM model identifier, e.g. "claude-sonnet", "gpt-4o"</summary>
        public string ModelName { get; set; } = "";

        /// <summary>Provider: "azure-ai-foundry", "azure-openai", "litellm"</summary>
        public string Provider { get; set; } = "";

        /// <summary>Endpoint URL for this model</summary>
        public string EndpointUrl { get; set; } = "";

        /// <summary>"Primary" or "Fallback"</summary>
        public string Role { get; set; } = "Primary";

        /// <summary>Lower number = higher priority (1 = primary)</summary>
        public int Priority { get; set; } = 1;

        /// <summary>"quality-first" or "cost-fallback"</summary>
        public string? Strategy { get; set; }

        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsDel { get; set; }
    }
}
