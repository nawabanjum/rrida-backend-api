using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.Models
{
    public class CompanyTeam
    {
        [Key]
        public int TeamId { get; set; }
        public string TeamName { get; set; }
        public int CompanyId { get; set; }
        public int UnitId { get; set; }
        public DateTime CreatedAt { get; set; }
        public int CreatedBy { get; set; }
        public int? ModifiedBy { get; set; }
        public int OrderBy { get; set; }
        public bool IsDeleted { get; set; }
        public int? ParentId { get; set; }
    }
}
