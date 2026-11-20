using System.Collections.Generic;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class EditTopInsightsRequestDto
    {
        public List<string> TopThemes { get; set; } = new();
        public List<string> TopRecommendations { get; set; } = new();
        public List<string> TopActions { get; set; } = new();
    }

    public class TopInsightsResponseDto
    {
        public long ReportId { get; set; }
        public List<string> TopThemes { get; set; } = new();
        public List<string> TopRecommendations { get; set; } = new();
        public List<string> TopActions { get; set; } = new();
    }
}
