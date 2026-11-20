using CloverBackoffice.Infrastructure.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.BusinessModel
{
    public class PermissionMatrix
    {
        public List<Groupitem> UserGroups { get; set; }
        public List<PromptTypesitem> PromptTypes { get; set; }
        public List<ModulePermission> Modules { get; set; }

        public class Groupitem
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }

        public class PromptTypesitem
        {
            public int PromptTypeId { get; set; }
            public string PromptTypeTitle { get; set; }
        }
        public class ModulePermission
        {
            public int ModuleId { get; set; }
            public string Name { get; set; }
            // public Dictionary<string, List<string>> Prompts { get; set; }
            public Dictionary<string, List<PromptTypesitem>> Prompts { get; set; } = new();

        }
    }
}
