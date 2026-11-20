using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.Models
{
    public class DailyQuestion
    {
        public uint Id { get; init; }

        [MaxLength(1000)]
        public required string Text { get; set; }
        public virtual required ContentCulture ContentCulture { get; set; }
    }
}
