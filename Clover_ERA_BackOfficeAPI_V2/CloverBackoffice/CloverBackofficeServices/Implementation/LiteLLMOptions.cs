using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Implementation
{
    public class LiteLLMOptions
    {
        public const string SectionName = "LiteLLM";

        [Required]
        [Url]
        public string BaseUrl { get; set; } = "http://localhost:4000";

        [Required]
        public string DefaultModel { get; set; } = "gpt-4o";

        [Range(1, 600)]
        public int TimeoutSeconds { get; set; } = 300;

        [Range(1, 128000)]
        public int MaxTokens { get; set; } = 4096;

        [Range(0.0, 2.0)]
        public double Temperature { get; set; } = 0.3;

        [Range(0.0, 1.0)]
        public double TopP { get; set; } = 1.0;

        [Required]
        public string MasterKey { get; set; } = "";

        // Optional: Add these for better configuration
        public string ApiVersion { get; set; } = "2025-01-01-preview";

        public bool Stream { get; set; } = true;

        public double FrequencyPenalty { get; set; } = 0.0;

        public double PresencePenalty { get; set; } = 0.0;

        [Range(1, 10)]
        public int N { get; set; } = 1;

        public string[] StopSequences { get; set; } = Array.Empty<string>();

        public string LogLevel { get; set; } = "Information";
    }
}