using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.Models
{
    public class AuditLogs
    {
        [Key]
        public int LogId { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public int? UserId { get; set; }
        public string Endpoint { get; set; }
        public string HttpMethod { get; set; }
        public string RequestData { get; set; }
        public string ResponseData { get; set; }
        public string ActionType { get; set; }
    }
}
