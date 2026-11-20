using System;
using System.Text.Json.Serialization;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class UpsertModelConfigurationRequest
    {
        public int? Id { get; set; }
        public string ModelName { get; set; }
        public string FriendlyName { get; set; }
        public string ApiKey { get; set; }
        public string Endpoint { get; set; }
        public string ApiVersion { get; set; }
        public int MaxTokens { get; set; } = 4096;
        public double Temperature { get; set; } = 0.3;

        public bool? IsDefault { get; set; }
        public int ModelConfigTypeId { get; set; }

        public string Provider { get; set; } = "";
        public string Source { get; set; } = "";
        public decimal InputCostPer1k { get; set; } = 0;
        public decimal OutputCostPer1k { get; set; } = 0;
        public double TopP { get; set; } = 1.0;
        public bool? IsSelected { get; set; }

        [JsonIgnore]
        public string? CreatedBy { get; set; }

        [JsonIgnore]
        public string? ModifiedBy { get; set; }
    }

    public class ModelConfigurationResponse
    {
        public int Id { get; set; }
        public string ModelName { get; set; }
        public string FriendlyName { get; set; }
        public string ApiKey { get; set; }
        public string Endpoint { get; set; }
        public string ApiVersion { get; set; }
        public int MaxTokens { get; set; }
        public double Temperature { get; set; }

        public bool IsDefault { get; set; }
        public int ModelConfigTypeId { get; set; }

        public string Provider { get; set; }
        public string Source { get; set; }
        public decimal InputCostPer1k { get; set; }
        public decimal OutputCostPer1k { get; set; }
        public double TopP { get; set; }
        public bool IsSelected { get; set; }

        public DateTime CreatedAt { get; set; }
        public string CreatedBy { get; set; }
        public DateTime? ModifiedAt { get; set; }
        public string ModifiedBy { get; set; }
    }
}
