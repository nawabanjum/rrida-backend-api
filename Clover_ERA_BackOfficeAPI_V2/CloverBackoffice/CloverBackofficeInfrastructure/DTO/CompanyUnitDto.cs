using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class CompanyUnitDto
    {
        public int ProfileId { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public int? BusinessUnitId { get; set; }
        public string Email { get; set; } = string.Empty;
    }
}
