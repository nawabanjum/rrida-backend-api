using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class UserDto
    {
        public int UserId { get; set; }
        public string TenantId { get; set; }
        public string Username { get; set; }
        public string Name { get; set; }
        public string CreatedDate { get; set; }
        public bool IsActive { get; set; }

        public UserGroupDto Group { get; set; }
    }
    public class UserGroupDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public bool IsAdmin { get; set; }
        public int UnitId { get; set; }
        public int OrganizationId { get; set; }
        public string Description { get; set; }

        public List<AssignUserRightDto> AssignUserRights { get; set; }
    }

    public class AssignUserRightDto
    {
        public int AssignRightId { get; set; }
        public int ModuleID { get; set; }
        public string ModuleName { get; set; }
        public int PromptTypeId { get; set; }
        public string PromptTypeTitle { get; set; }
    }

}
