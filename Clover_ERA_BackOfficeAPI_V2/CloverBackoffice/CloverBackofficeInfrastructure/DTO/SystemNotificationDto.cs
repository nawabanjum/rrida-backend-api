using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class CreateSystemNotificationDto
    {
        public int UserId { get; set; }
        public string? Title { get; set; }
        public string? Message { get; set; }
        public string? NotificationType { get; set; }
        public string? IpAddress { get; set; }
        public string? NotificationUrl { get; set; }
    }

    public class SystemNotificationResponseDto
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string? Title { get; set; }
        public string? Message { get; set; }
        public string? NotificationType { get; set; }
        public string? NotificationUrl { get; set; }
        public bool IsRead { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? ReadAt { get; set; }
    }

    public class PaginatedNotificationResponseDto
    {
        public List<SystemNotificationResponseDto> Notifications { get; set; }
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }
}
