using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class ImportPromptDataDto
    {
        public string CloverCategory { get; set; }
        public string Question { get; set; }
        public string Sentiment { get; set; }
        public string Answer { get; set; }
        public string Reason { get; set; }
    }
}
