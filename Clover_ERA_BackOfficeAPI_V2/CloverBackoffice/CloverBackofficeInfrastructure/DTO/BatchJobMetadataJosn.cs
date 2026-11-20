using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class BatchJobMetadataJosn
    {
        public DateTimeOffset? FromDate { get; set; }
        public DateTimeOffset? ToDate { get; set; }

        public string? TopTheme { get; set; }
        public string? TopRecomandation { get; set; }
        public string? TopAction { get; set; }

        public string? Status { get; set; }

        public DateTime? ApprovedAt { get; set; }

        public int? ApprovedBy { get; set; }
        public bool? IsSystemGenerated { get; set; }
        public long? ReportId { get; set; }
        public string? WorksheetCategory { get; set; }
        public string? WorksheetFileName { get; set; }
    }
}
