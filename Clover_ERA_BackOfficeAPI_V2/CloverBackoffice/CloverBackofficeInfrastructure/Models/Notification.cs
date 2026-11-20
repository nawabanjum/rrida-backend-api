using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.Models
{
    public class Notification
    {
        [Key]
        public int NotificationId { get; set; }
        public int UnitId { get; set; }
        public int BussinessUnitId { get; set; }
        public int ProfileId { get; set; }
        public int UserId { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Message { get; set; }
        public int? Type { get; set; }
    }
}
