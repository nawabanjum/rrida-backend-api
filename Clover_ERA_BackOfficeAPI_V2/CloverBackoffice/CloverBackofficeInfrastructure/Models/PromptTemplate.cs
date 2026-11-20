using System;
using System.ComponentModel.DataAnnotations;

namespace CloverBackoffice.Infrastructure.Models
{
    public class PromptTemplate
    {
        [Key]
        public int Id { get; set; }

        /// <summary>Logical name, e.g. "clover-era-insights"</summary>
        public string Name { get; set; } = "";

        /// <summary>Semantic version, e.g. "2.0.0"</summary>
        public string Version { get; set; } = "";

        public string Description { get; set; } = "";

        /// <summary>System-role instruction for the LLM</summary>
        public string SystemPrompt { get; set; } = "";

        /// <summary>User-role template with {{placeholders}}</summary>
        public string Template { get; set; } = "";

        /// <summary>Only ONE active version per Name</summary>
        public bool IsActive { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? CreatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public bool IsDel { get; set; }

        /// <summary>JSON: {"Temperature":0.7,"MaxTokens":1024,"TopP":1.0}</summary>
        public string? MetadataJson { get; set; }
    }
}
