using CloverBackoffice.Infrastructure.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class GroupConfigurationDto
    {
        public int UserGroupId { get; set; }
        public int ModuleID { get; set; }
        public List<PromptTypes> PromptTypes { get; set; }
    }
}
