using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static CloverBackoffice.Infrastructure.BusinessModel.PermissionMatrix;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class LoginDto
    {
        public class CompanyDto
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public bool IsPartner { get; set; }
        }

        public class UserDto
        {
            public int UserId { get; set; }
            public string Name { get; set; }
        }

        public class BussinessUnitDto
        {
            public int UnitId { get; set; }
            public string UnitName { get; set; }
        }

        public class UserGroupDto
        {
            public int Id { get; set; }
            public string Name { get; set; }

            public bool IsAdmin { get; set; }
        }
        public class ModulePermission
        {
            public int ModuleId { get; set; }
            public string Name { get; set; }
            public Dictionary<string, List<PromptTypesitem>> Prompts { get; set; } = new();

        }

        public UserDto? User { get; set; }
        public CompanyDto? Company { get; set; }
        public List<BussinessUnitDto> CompanyUnit { get; set; } = new();
        /// <summary>The user's highest role. See <see cref="Roles"/> for all of them.</summary>
        public UserGroupDto? UserGroup { get; set; }

        /// <summary>Every role the user holds, highest first.</summary>
        public List<UserGroupDto> Roles { get; set; } = new();

        /// <summary>Teams the user manages (Team Manager).</summary>
        public List<ManagedTeamDto> ManagedTeams { get; set; } = new();

        /// <summary>
        /// True when an existing manager has been assigned one or more teams since
        /// they last acknowledged — show the new-team popup. Always false on a first login.
        /// </summary>
        public bool HasNewTeamAssignment { get; set; }

        /// <summary>The unacknowledged teams behind <see cref="HasNewTeamAssignment"/> (subset of ManagedTeams).</summary>
        public List<ManagedTeamDto> NewTeamAssignments { get; set; } = new();

        /// <summary>Team Managers under this user (Portfolio Manager).</summary>
        public List<RelatedUserDto> ManagersInPortfolio { get; set; } = new();

        /// <summary>The Portfolio Manager this user reports to, if any.</summary>
        public RelatedUserDto? PortfolioManager { get; set; }

        /// <summary>Prompts is keyed by role name — one entry per role the user holds.</summary>
        public List<ModulePermission>? Modules { get; set; }
        public bool IsVerified { get; set; }
        public bool IsFirstLogin { get; set; }
        public string accessToken { get; set; }
        public string refreshToken { get; set; }
        public int expiresIn { get; set; }
    }
}
