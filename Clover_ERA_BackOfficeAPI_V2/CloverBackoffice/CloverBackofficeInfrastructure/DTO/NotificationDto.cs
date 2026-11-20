using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class NotificationDto
    {
        public string UnitName { get; set; }
        public string CompanyName { get; set; }
        public String UserName { get; set; }
        public string Type { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Message { get; set; }
    }
}
