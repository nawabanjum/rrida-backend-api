using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class NeedsAttentionSectionDto
    {
        public int NeedsAttentionCount { get; set; }
        public int NoManagerCount { get; set; }
        public int NoResponseIn48hCount { get; set; }
        public int NoActionHubUpdateCount { get; set; }
    }
}
