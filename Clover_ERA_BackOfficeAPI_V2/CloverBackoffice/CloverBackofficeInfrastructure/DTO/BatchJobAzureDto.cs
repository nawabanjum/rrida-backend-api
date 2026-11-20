using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class BatchJobAzureDto
    {
        public string JobId { get; set; }
        public string JobType { get; set; }
        public long UnitId { get; set; }
        public string Status { get; set; }
        [JsonIgnore]
        public string FileId { get; set; }
        [JsonIgnore]
        public string ResultFileId { get; set; }
        [JsonIgnore]
        public string RawResponse { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
    }
}
