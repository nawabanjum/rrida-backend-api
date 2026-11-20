using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class LLMReportRequestDto
    {
        public long UnitId { get; set; }
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public string? WorksheetFile { get; set; }  //added for create report manually
        public List<string> TopActions { get; set; } = new();
        public List<string> TopRecommendations { get; set; } = new();
        public List<string> TopThemes { get; set; } = new();
    }
}
