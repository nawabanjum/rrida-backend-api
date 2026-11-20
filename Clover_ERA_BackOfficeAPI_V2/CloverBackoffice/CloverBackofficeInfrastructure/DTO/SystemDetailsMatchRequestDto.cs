using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class SystemDetailsMatchRequestDto
    {
        public int UserId { get; set; }
        public string? IPAddress { get; set; }
        public string? DeviceModel { get; set; }
        public string? MachineName { get; set; }
        public string? OSVersion { get; set; }
        public string? BrowserName { get; set; }
        public string? BrowserVersion { get; set; }
        public string? UserAgent { get; set; }
        public string? OneSignalPlayerId { get; set; }
    }
}
