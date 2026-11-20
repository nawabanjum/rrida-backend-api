using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class UpdatePlayerIdRequestDto
    {
        public int UserId { get; set; }
        public string OneSignalPlayerId { get; set; }
    }
}
