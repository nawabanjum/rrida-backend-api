using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class BusinessUnitDto
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public List<BusinessUnitDto> Children { get; set; } = new List<BusinessUnitDto>();
    }
}
