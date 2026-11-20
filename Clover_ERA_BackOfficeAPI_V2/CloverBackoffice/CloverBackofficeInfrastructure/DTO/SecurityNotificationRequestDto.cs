using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class SecurityNotificationRequestDto
    {
        public int UserId { get; set; }
        public string? IPAddress { get; set; }
        public string? UserAgent { get; set; }
        public string? CustomTitle { get; set; }
        public string? CustomMessage { get; set; }
    }
}
