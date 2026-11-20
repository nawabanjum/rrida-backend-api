using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CloverBackoffice.Infrastructure.Models
{
    [Table("UserSystemDetails")]
    public class UserSystemDetails
    {
        [Key]
        public int Id { get; set; }

        // User & Network
        public int? UserId { get; set; }

        [Column(TypeName = "varchar(50)")]
        public string? IPAddress { get; set; }

        // Device / System
        [Column(TypeName = "varchar(50)")]
        public string? DeviceType { get; set; } // Desktop / Laptop / Mobile

        [Column(TypeName = "varchar(100)")]
        public string? DeviceBrand { get; set; } // Dell, HP, Lenovo, Apple

        [Column(TypeName = "varchar(100)")]
        public string? DeviceModel { get; set; }

        [Column(TypeName = "varchar(150)")]
        public string? MachineName { get; set; }

        // Operating System
        [Column(TypeName = "varchar(100)")]
        public string? OperatingSystem { get; set; } // Windows / macOS / Linux

        [Column(TypeName = "varchar(50)")]
        public string? OSVersion { get; set; }

        [Column(TypeName = "varchar(50)")]
        public string? Architecture { get; set; } // x64 / x86 / ARM

        // Browser / App
        [Column(TypeName = "varchar(100)")]
        public string? BrowserName { get; set; }

        [Column(TypeName = "varchar(50)")]
        public string? BrowserVersion { get; set; }

        public string? UserAgent { get; set; }

        // Push Notification
        [Column(TypeName = "varchar(255)")]
        public string? OneSignalPlayerId { get; set; }

        // Audit
        public DateTime? LoginTime { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
