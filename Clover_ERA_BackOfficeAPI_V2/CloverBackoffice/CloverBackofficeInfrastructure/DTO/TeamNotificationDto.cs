using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class TeamNotificationDto
    {
        public int NoManagerCount { get; set; }
        public string NoManagerMessage { get; set; }
        public int NoResponseIn48hCount { get; set; }
        public string NoResponseIn48hMessage { get; set; }
    }
}
