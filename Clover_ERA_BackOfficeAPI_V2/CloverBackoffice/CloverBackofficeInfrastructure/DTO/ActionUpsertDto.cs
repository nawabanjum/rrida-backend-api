using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class ActionUpsertDto
    {

        public int Id { get; set; }

        public uint UnitId { get; set; }
        public uint ProfileId { get; set; }

        public string ActionTitle { get; set; }
        public DateTime DateAddedToAction { get; set; }

        public string ScheduledEventActivity { get; set; }
        public DateTime? DateImplemented { get; set; }

        public string Feedback { get; set; }

        [JsonIgnore]
        public DateTime CreatedAt { get; set; }
        [JsonIgnore]
        public int CreatedBy { get; set; }
        [JsonIgnore]
        public bool IsDel { get; set; } = false;

    }
}
