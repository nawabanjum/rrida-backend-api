using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.Models
{
    public class LLMReport
    {
        public long Id { get; set; }

        public DateTime FromDate { get; set; }

        public DateTime ToDate { get; set; }
        public string? WorksheetFile { get; set; }  //added for create report manually

        public long UnitId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
