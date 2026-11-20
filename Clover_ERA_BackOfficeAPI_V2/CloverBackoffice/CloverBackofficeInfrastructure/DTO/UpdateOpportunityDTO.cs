using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class UpdateOpportunityDTO
    {
        public int OpportunityId { get; set; }
        public int stageid { get; set; }
        public string? AdditionalNotes { get; set; }
        public DateTime ExpectedCloseDate { get; set; }

        [JsonIgnore]
        public int UpdatedBy { get; set; }

    }
}
