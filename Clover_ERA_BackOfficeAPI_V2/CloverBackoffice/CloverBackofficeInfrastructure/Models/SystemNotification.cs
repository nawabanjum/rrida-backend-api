using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.Models
{
    public class SystemNotification
    {
        [Key]
        public int Id { get; set; }

        public int UserId { get; set; }

        public string? Title { get; set; }

        public string? Message { get; set; }

        public string? NotificationType { get; set; }

        public string? IpAddress { get; set; }

        public string? NotificationUrl { get; set; }

        public bool IsRead { get; set; } = false;

        public DateTime? CreatedAt { get; set; }

        public DateTime? ReadAt { get; set; }
    }
}

