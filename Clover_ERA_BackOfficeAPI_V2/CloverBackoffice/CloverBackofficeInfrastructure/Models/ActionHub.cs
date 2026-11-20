using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.Models
{
    public class ActionHub
    {
        [Key]
        public int Id { get; set; }
        public uint UnitId { get; set; }
        public uint ProfileId { get; set; }
        public long? ReportId { get; set; }
        public string ActionTitle { get; set; }
        public string Description { get; set; }
        public string Type { get; set; }
        public DateTime CreatedAt { get; set; }
        public int CreatedBy { get; set; }
        public bool IsDel { get; set; } = false;
        public DateTime? UpdatedAt { get; set; }
        public ICollection<ActionEvent> ActionEvents { get; set; }
    }
}
