using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.Models
{
    public class TopTheme
    {
        [Key]
        public int Id { get; set; }
        public uint UnitId { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Theme { get; set; }
        public long ReportId { get; set; }
    }
}
