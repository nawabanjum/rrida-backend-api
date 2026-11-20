using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class ActionHubSummaryDto
    {
        public int TotalActions { get; set; }
        public string TotalActionsLabel { get; set; }
        public ActionHubLastActionDto? LastAction { get; set; }
        public ActionHubLastUpdatedActionDto? LastUpdatedAction { get; set; }
    }

    public class ActionHubLastActionDto
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string ActionType { get; set; }
        public DateTime CreatedDate { get; set; }
    }

    public class ActionHubLastUpdatedActionDto
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string ActionType { get; set; }
        public DateTime UpdatedDate { get; set; }
    }

    // Flat row used only to build the summary cards (one DB round trip).
    public class ActionHubSummaryRowDto
    {
        public int Id { get; set; }
        public string ActionTitle { get; set; }
        public string Type { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
