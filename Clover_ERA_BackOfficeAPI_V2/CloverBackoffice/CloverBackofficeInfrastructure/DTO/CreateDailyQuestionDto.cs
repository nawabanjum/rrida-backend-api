using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class CreateDailyQuestionDto
    {
        [JsonPropertyName("question")]
        public string Question { get; set; }

        [JsonPropertyName("category")]
        public string Category { get; set; }

        [JsonPropertyName("positiveReasons")]
        public string PositiveReasons { get; set; } // comma separated or single value

        [JsonPropertyName("positiveActions")]
        public string PositiveActions { get; set; } // comma separated or single value

        [JsonPropertyName("negativeReasons")]
        public string NegativeReasons { get; set; } // comma separated or single value

        [JsonPropertyName("negativeActions")]
        public string NegativeActions { get; set; } // comma separated or single value
    }
}
