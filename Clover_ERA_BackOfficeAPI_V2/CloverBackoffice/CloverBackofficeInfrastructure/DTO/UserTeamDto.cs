using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class UserTeamDto
    {
        public int UserId { get; set; }
        public uint UnitId { get; set; }
        public bool? IsReadOnly { get; set; } = false;

    }

}
