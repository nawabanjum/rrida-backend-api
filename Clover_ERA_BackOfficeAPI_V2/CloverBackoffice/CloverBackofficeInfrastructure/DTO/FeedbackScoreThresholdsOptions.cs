using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class FeedbackScoreThresholdsOptions
    {
        public int ExcellentMin { get; set; }
        public int GoodMin { get; set; }
        public int NeedsWorkMin { get; set; }
        public int UrgentAttentionMin { get; set; }
    }
}
