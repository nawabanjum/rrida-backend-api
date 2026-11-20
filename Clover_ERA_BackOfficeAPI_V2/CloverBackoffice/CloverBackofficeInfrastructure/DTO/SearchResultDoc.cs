using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class SearchResultDoc
    {
        public string Id { get; set; } = "";
        public string Content { get; set; } = "";
        public IDictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
        public double Score { get; set; }
    }
}
