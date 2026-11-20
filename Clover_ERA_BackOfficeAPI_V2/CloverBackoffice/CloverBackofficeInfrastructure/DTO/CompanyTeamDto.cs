using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class CompanyTeamDto
    {
        public int TeamId { get; set; }
        public string TeamName { get; set; }
        public int ProfileId { get; set; }
        public int UnitId { get; set; }
        public int OrderBy { get; set; }
        public int? ParentId { get; set; }
        [JsonIgnore]
        public int CreatedBy { get; set; }
        [JsonIgnore]
        public string? message { get; set; }
    }
}
