using CloverBackoffice.Infrastructure.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class UpsertEventDto
    {
        public int Id { get; set; }

        //  Foreign Key for ActionHub
        public int ActionHubId { get; set; }
        public DateTime DateAddedToAction { get; set; }
        public string ScheduledEventActivity { get; set; }
        public DateTime? DateImplemented { get; set; }
        public string Feedback { get; set; }
        public string Type { get; set; }

        [JsonIgnore]
        public string? Attachment { get; set; }

        [JsonIgnore]
        public DateTime CreatedAt { get; set; }
        [JsonIgnore]
        public int CreatedBy { get; set; }
        [JsonIgnore]
        public bool IsDel { get; set; } = false;
        [JsonIgnore]
        public string? message { get; set; }

    }
}
