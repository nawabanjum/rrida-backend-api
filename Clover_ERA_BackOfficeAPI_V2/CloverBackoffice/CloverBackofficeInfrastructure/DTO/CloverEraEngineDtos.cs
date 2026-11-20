using System.Collections.Generic;

namespace CloverBackoffice.Infrastructure.DTO
{
public class CloverEraPillarStatsDto
    {
        public string Pillar { get; set; } = "";
        public int ResponseCount { get; set; }
        public int HappyCount { get; set; }
        public int IndifferentCount { get; set; }
        public int SadCount { get; set; }
        public int AngryCount { get; set; }
        public double RawScore { get; set; }
        public double CorrectedScore { get; set; }
        public double GapFromTarget { get; set; }
        public string Status { get; set; } = "";
        public double ConfidenceWeight { get; set; }
        public double FocusPriorityScore { get; set; }
        public Dictionary<string, int> ReasonTextCounts { get; set; } = new();
        public List<Dictionary<string, string>> FreeTextResponses { get; set; } = new();
        public List<string> NegativeReasonTexts { get; set; } = new();
    }

    public class CloverEraFocusRecommendationDto
    {
        public string Pillar { get; set; } = "";
        public double CorrectedScore { get; set; }
        public double GapFromTarget { get; set; }
        public double ConfidenceScore { get; set; }
        public List<string> WhySelected { get; set; } = new();
        public string? SecondaryPillar { get; set; }
        public string? SecondaryRationale { get; set; }
    }

    public class CloverEraThemeAssignmentDto
    {
        public int ThemeIndex { get; set; }
        public string Sentiment { get; set; } = "";
        public string Pillar { get; set; } = "";
        public string Rationale { get; set; } = "";
    }

    public class CloverEraWrittenThemeDto
    {
        public int ThemeIndex { get; set; }
        public string Sentiment { get; set; } = "";
        public string Pillar { get; set; } = "";
        public string Headline { get; set; } = "";
        public string Text { get; set; } = "";
    }

    public class CloverEraRecommendationDto
    {
        public string Text { get; set; } = "";
    }

    public class CloverEraWorksheetActionDto
    {
        public string WorksheetCode { get; set; } = "";
        public string WorksheetTitle { get; set; } = "";
        public int DurationMinutes { get; set; }
        public string Text { get; set; } = "";
        public string SuccessMetric { get; set; } = "";
        public string? SwapNote { get; set; }
    }

    public class CloverEraMicroActionDto
    {
        public int DurationMinutes { get; set; }
        public string Text { get; set; } = "";
    }

    public class CloverEraEscalationActionDto
    {
        public string SignalCategory { get; set; } = "";
        public string Route { get; set; } = "";
        public int TimeframeDays { get; set; }
        public string Text { get; set; } = "";
    }

    public class CloverEraReportSummaryDto
    {
        public List<CloverEraWrittenThemeDto> Themes { get; set; } = new();
        public List<CloverEraRecommendationDto> Recommendations { get; set; } = new();
        public CloverEraWorksheetActionDto? WorksheetAction { get; set; }
        public List<CloverEraMicroActionDto> MicroActions { get; set; } = new();
    }

    public class CloverEraTopInsightDto
    {
        public TopInsightDto TopInsight { get; set; } = new();
    }

    public class TopInsightDto
    {
        public string TopThemes { get; set; } = "";
        public string TopActions { get; set; } = "";
        public string TopRecommendations { get; set; } = "";
    }

    public class CloverEraCompleteReportDto
    {
        public string BusinessUnit { get; set; } = "";
        public string Company { get; set; } = "";
        public string PeriodStart { get; set; } = "";
        public string PeriodEnd { get; set; } = "";
        public int TotalResponses { get; set; }
        public double OverallRawScore { get; set; }
        public double OverallCorrectedScore { get; set; }
        public Dictionary<string, CloverEraPillarStatsDto> PillarStats { get; set; } = new();
        public CloverEraFocusRecommendationDto FocusRecommendation { get; set; } = new();
        public List<CloverEraWrittenThemeDto> Themes { get; set; } = new();
        public List<CloverEraRecommendationDto> Recommendations { get; set; } = new();
        public CloverEraWorksheetActionDto? WorksheetAction { get; set; }
        public List<CloverEraMicroActionDto> MicroActions { get; set; } = new();
        public CloverEraEscalationActionDto? EscalationAction { get; set; }
        public List<string> DataQualityIssues { get; set; } = new();
        public double FreeTextCompletionRate { get; set; }
        public List<string> LowSamplePillars { get; set; } = new();
    }
}
