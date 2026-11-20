using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class ActionHubWithEventsDto
    {
        public int Id { get; set; }
        public string ActionTitle { get; set; }
        public string Description { get; set; }
        public string ActionType { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string? WorksheetFile { get; set; }
        public List<ActionEventDto> ActionEvents { get; set; } = new();
    }

    public class ActionEventDto
    {
        public int Id { get; set; }
        public string ScheduledEvent { get; set; }
        public DateTime? DateImplemented { get; set; }
        public string Feedback { get; set; }

    }

}
