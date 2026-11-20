using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.Models
{
    public class ActionEvent
    {
        [Key]
        public int Id { get; set; }

        public DateTime DateAddedToAction { get; set; }
        public string ScheduledEventActivity { get; set; }
        public DateTime? DateImplemented { get; set; }
        public string Feedback { get; set; }
        public string Attachment { get; set; }
        public string Type { get; set; }
        public DateTime CreatedAt { get; set; }
        public int CreatedBy { get; set; }
        public bool IsDel { get; set; } = false;

        //  Foreign Key for ActionHub
        public int ActionHubId { get; set; }

        //  Navigation Property
        public ActionHub ActionHub { get; set; }
    }
}
