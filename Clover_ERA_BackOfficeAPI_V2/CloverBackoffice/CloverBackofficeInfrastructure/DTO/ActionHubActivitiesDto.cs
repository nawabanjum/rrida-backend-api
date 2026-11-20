using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class ActionHubActivitiesDto
    {
        public List<ActionHubActivityDto> LastActionHubActivities { get; set; }
        public List<RecentTeamDto> RecentlyTeamsActivities { get; set; }
    }
}
