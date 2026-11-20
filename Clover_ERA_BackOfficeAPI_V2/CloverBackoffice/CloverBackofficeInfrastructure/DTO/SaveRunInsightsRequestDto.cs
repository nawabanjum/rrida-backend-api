using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class SaveRunInsightsRequestDto
    {
        [Required]
        [Range(1, long.MaxValue, ErrorMessage = "RunId must be greater than 0.")]
        public long RunId { get; set; }

        [Required]
        public List<string> TopThemes { get; set; } = new();

        [Required]
        public List<string> TopRecommendations { get; set; } = new();

        [Required]
        public List<string> TopActions { get; set; } = new();
    }
}
