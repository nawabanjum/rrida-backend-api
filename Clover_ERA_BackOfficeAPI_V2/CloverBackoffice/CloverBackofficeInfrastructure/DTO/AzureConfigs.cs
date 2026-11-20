using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class AzureConfigs
    {
        public class AzureSearchConfig
        {
            public string Endpoint { get; set; } = "";
            public string ApiKey { get; set; } = "";
            public string IndexName { get; set; } = "";
        }
    }
}
