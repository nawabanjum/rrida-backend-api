using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class DeleteByRangeDto
    {
        public long FromId { get; set; }
        public long ToId { get; set; }
    }

    public class DeleteByRangeResultDto
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; }
        public int DeletedCount { get; set; }
    }
}
