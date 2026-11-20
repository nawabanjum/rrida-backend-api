using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.Models
{
    public class SystemModule
    {
        [Key]
        public int ModuleID { get; set; }
        public string ModuleName { get; set; }
        public string FriendlyName { get; set; }
        public string? Description { get; set; }
    }
}
