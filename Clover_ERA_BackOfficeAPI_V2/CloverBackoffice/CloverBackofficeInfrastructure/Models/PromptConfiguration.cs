using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.Models
{
    public class PromptConfiguration
    {
        [Key]
        public int Id { get; set; }

        public string Text { get; set; }

        public bool IsDefault { get; set; } = false;

        public DateTime CreatedAt { get; set; }

        public string CreatedBy { get; set; }

        public DateTime? ModifiedAt { get; set; }

        public string ModifiedBy { get; set; }

        public bool IsDel { get; set; } = false;
    }
}
