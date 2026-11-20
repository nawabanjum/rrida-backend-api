using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.Models
{
    public class ModelConfiguration
    {
        [Key]
        public int Id { get; set; }

        public string ModelName { get; set; }

        public string FriendlyName { get; set; }

        public string ApiKey { get; set; }

        public string Endpoint { get; set; }

        public string ApiVersion { get; set; }

        public int MaxTokens { get; set; } = 4096;

        public double Temperature { get; set; } = 0.3;

        public bool IsDefault { get; set; } = false;

        public DateTime CreatedAt { get; set; }

        public string CreatedBy { get; set; }

        public DateTime? ModifiedAt { get; set; }

        public string ModifiedBy { get; set; }

        public bool IsDel { get; set; } = false;

        public int ModelConfigTypeId { get; set; }

        public string Provider { get; set; } = "";
        public string Source { get; set; } = "";
        public decimal InputCostPer1k { get; set; } = 0;
        public decimal OutputCostPer1k { get; set; } = 0;
        public double TopP { get; set; } = 1.0;
        public bool IsSelected { get; set; } = false;
    }
}
