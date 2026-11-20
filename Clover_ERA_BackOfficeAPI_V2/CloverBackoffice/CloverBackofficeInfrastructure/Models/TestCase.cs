using System;
using System.ComponentModel.DataAnnotations;

namespace CloverBackoffice.Infrastructure.Models
{
    public class TestCase
    {
        [Key]
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string PromptTemplateName { get; set; } = "";
        public string InputDataJson { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? CreatedBy { get; set; }
        public DateTime? ModifiedAt { get; set; }
        public string? ModifiedBy { get; set; }
        public bool IsDel { get; set; }
    }
}
