using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.Models
{
    public class CompanyLink
    {
        public int CompanyLinkId { get; set; }
        public int ProfileId { get; set; }
        public int? ParentId { get; set; }
        public int UserId { get; set; }
        //for company list
        public User User { get; set; }
        public int? UnitId { get; set; }
        public string ExpiryDate { get; set; }
        public string? CreatedBy { get; set; }
        public int? BussinessUnitId { get; set; }
    }
}
