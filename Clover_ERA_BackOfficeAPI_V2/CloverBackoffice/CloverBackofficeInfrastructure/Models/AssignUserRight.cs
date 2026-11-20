using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.Models
{
    public class AssignUserRight
    {
        [Key]
        public int AssignRightId { get; set; }

        // FK to Group
        public int UserGroupId { get; set; }
        public UserGroup UserGroup { get; set; }

        // FK to SystemModule
        public int SystemModuleId { get; set; }
        public SystemModule SystemModule { get; set; }

        // FK to PromptType
        public int PromptTypeId { get; set; }
        public PromptTypes PromptType { get; set; }
    }
}
