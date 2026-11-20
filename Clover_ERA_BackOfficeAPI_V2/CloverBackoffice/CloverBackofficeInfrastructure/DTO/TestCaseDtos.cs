using System;
using System.Collections.Generic;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class UpsertTestCaseRequest
    {
        public int? Id { get; set; }
        public string Name { get; set; } = "";
        public string PromptTemplateName { get; set; } = "";
        public string InputDataJson { get; set; } = "";
        public List<UpsertTestCaseAssertionDto> Assertions { get; set; } = new();
    }

    public class UpsertTestCaseAssertionDto
    {
        public int? Id { get; set; }
        public string Name { get; set; } = "";
        public string AssertionType { get; set; } = "";
        public string? JsonPath { get; set; }
        public string? Operator { get; set; }
        public string? ExpectedValue { get; set; }
    }

    public class TestCaseResponseDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string PromptTemplateName { get; set; } = "";
        public string InputDataJson { get; set; } = "";
        public int AssertionCount { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? CreatedBy { get; set; }
        public DateTime? ModifiedAt { get; set; }
    }

    public class TestCaseDetailResponseDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string PromptTemplateName { get; set; } = "";
        public string InputDataJson { get; set; } = "";
        public int AssertionCount { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? CreatedBy { get; set; }
        public DateTime? ModifiedAt { get; set; }
        public List<TestCaseAssertionDto> Assertions { get; set; } = new();
    }

    public class TestCaseAssertionDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string AssertionType { get; set; } = "";
        public string? JsonPath { get; set; }
        public string? Operator { get; set; }
        public string? ExpectedValue { get; set; }
    }
}
