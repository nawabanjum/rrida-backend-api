using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.Models
{
    public class PromptTypes
    {
        [Key]
        public int PromptTypeId { get; set; }
        public string PromptTypeTitle { get; set; }
        [JsonIgnore]
        public int UnitId { get; set; }
        [JsonIgnore]
        public int OrganizationId { get; set; }

    }
}
