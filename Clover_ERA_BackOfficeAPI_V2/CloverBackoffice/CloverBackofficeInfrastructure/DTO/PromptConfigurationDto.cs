using System;
using System.Text.Json.Serialization;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class UpsertPromptConfigurationRequest
    {
        public int? Id { get; set; }
        public string Text { get; set; }
        public bool? IsDefault { get; set; }

        [JsonIgnore]
        public string? CreatedBy { get; set; }

        [JsonIgnore]
        public string? ModifiedBy { get; set; }
    }

    public class PromptConfigurationResponse
    {
        public int Id { get; set; }
        public string Text { get; set; }
        public bool IsDefault { get; set; }
        public DateTime CreatedAt { get; set; }
        public string CreatedBy { get; set; }
        public DateTime? ModifiedAt { get; set; }
        public string ModifiedBy { get; set; }
    }
}
