using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class CustomAddQuestionDto
    {
        public string QuestionText { get; set; }
        public string CloverCategory { get; set; }
        // Delimited (delimiter specified by Delimiter property)
        public string PositiveReasons { get; set; }
        public string NegativeReasons { get; set; }

        public string PositiveActions { get; set; }
        public string NegativeActions { get; set; }

        /// <summary>
        /// Internal delimiter used to separate reasons/actions. Defaults to comma for API callers.
        /// Import flow uses "|||" to avoid splitting on commas within text.
        /// </summary>
        [JsonIgnore]
        public string Delimiter { get; set; } = ",";
    }
}
