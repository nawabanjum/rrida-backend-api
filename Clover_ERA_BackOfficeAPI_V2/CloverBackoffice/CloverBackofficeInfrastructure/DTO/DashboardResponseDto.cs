using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class DashboardResponseDto
    {
        public TeamsSectionDto TotalTeamsSection { get; set; }
        public ResponseRateSectionDto AvgResponsesPerTeamSection { get; set; }
        public NeedsAttentionSectionDto NeedsAttentionSection { get; set; }
        public EngagementSectionDto AvgTrendScoreSection { get; set; }
        public ActionHubActivitiesDto ActionHubActivities { get; set; }
    }
}
