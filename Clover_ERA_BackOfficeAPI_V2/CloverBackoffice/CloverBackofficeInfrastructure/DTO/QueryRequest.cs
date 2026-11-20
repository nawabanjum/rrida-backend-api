using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class QueryRequest
    {
        public string Query { get; set; } = "";
        public int? TopK { get; set; }
    }
}
