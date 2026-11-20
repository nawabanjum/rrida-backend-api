using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class OpportunityCombinedResponseDto
    {
        public List<OpportunityListDto> Opportunities { get; set; }
        public OpportunityDashboardDto Stats { get; set; }
    }
}
