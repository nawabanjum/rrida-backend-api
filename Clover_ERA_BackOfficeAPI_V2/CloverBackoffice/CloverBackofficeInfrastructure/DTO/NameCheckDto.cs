using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class NameCheckDto
    {
        public string Name { get; set; }
        public bool IsCompany { get; set; }
        public int ProfileId { get; set; }
    }
}
