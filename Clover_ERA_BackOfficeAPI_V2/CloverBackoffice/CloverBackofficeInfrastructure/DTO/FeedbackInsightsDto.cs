using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class FeedbackInsightsDto
    {
        public List<string> TopThemes { get; set; }
        public List<string> TopRecommendations { get; set; }
        public List<string> TopActions { get; set; }
    }
}
