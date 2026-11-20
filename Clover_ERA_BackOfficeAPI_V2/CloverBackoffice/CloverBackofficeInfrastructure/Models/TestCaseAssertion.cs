using System;
using System.ComponentModel.DataAnnotations;

namespace CloverBackoffice.Infrastructure.Models
{
    public class TestCaseAssertion
    {
        [Key]
        public int Id { get; set; }
        public int TestCaseId { get; set; }
        public string Name { get; set; } = "";
        public string AssertionType { get; set; } = "";
        public string? JsonPath { get; set; }
        public string? Operator { get; set; }
        public string? ExpectedValue { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsDel { get; set; }
    }
}
