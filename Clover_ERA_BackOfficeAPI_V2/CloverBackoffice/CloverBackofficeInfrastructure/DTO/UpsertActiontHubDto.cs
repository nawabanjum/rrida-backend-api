using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class UpsertActiontHubDto
    {
        public int Id { get; set; }
        public uint UnitId { get; set; }
        public uint ProfileId { get; set; }
        public long? ReportId { get; set; } = null;
        public string ActionTitle { get; set; }
        public string Description { get; set; }
        public string Type { get; set; }

        [JsonIgnore]
        public DateTime CreatedAt { get; set; }
        [JsonIgnore]
        public int CreatedBy { get; set; }
        [JsonIgnore]
        public bool IsDel { get; set; } = false;
        [JsonIgnore]
        public string? message { get; set; }
    }
}
