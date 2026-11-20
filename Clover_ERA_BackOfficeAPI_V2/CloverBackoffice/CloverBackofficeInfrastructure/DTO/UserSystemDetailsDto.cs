using System;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class SaveSystemDetailsRequestDto
    {
        public string? OneSignalPlayerId { get; set; }
    }

    public class UserSystemDetailsDto
    {
        public int Id { get; set; }
        public int? UserId { get; set; }
        public string? IPAddress { get; set; }
        public string? DeviceType { get; set; }
        public string? DeviceBrand { get; set; }
        public string? DeviceModel { get; set; }
        public string? MachineName { get; set; }
        public string? OperatingSystem { get; set; }
        public string? OSVersion { get; set; }
        public string? Architecture { get; set; }
        public string? BrowserName { get; set; }
        public string? BrowserVersion { get; set; }
        public string? UserAgent { get; set; }
        public string? OneSignalPlayerId { get; set; }
        public DateTime? LoginTime { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
