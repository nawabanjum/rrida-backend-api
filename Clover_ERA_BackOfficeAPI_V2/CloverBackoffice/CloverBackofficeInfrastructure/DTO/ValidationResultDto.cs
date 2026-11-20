using System.Collections.Generic;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class ValidationResultDto
    {
        public bool IsValid { get; set; }
        public int Passed { get; set; }
        public int Total { get; set; }
        public double PassRate => Total > 0 ? (double)Passed / Total * 100 : 0;
        public List<AssertionResultDto> Assertions { get; set; } = new();
    }

    public class AssertionResultDto
    {
        public string Name { get; set; } = "";
        public bool Passed { get; set; }
        public string? FailReason { get; set; }
    }
}
