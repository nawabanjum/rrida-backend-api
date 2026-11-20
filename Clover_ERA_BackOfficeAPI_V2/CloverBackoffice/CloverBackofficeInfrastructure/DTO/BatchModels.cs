using System;
using System.Collections.Generic;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class BatchRequest
    {
        public string Type { get; set; }
        public string Priority { get; set; }
        public object Data { get; set; }
        public string NotificationEmail { get; set; }
    }

    public class BatchStatus
    {
        public string Status { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
        public string OutputFileId { get; set; }
    }
}


