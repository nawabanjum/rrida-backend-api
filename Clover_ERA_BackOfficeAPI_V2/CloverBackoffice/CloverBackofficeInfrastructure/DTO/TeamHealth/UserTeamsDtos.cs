using System.Collections.Generic;

namespace CloverBackoffice.Infrastructure.DTO.TeamHealth
{
    /// <summary>Body for POST api/team-health/user-teams.</summary>
    public class UserTeamsRequestDto
    {
        /// <summary>The user whose teams are wanted. Binds "userID" too.</summary>
        public int UserId { get; set; }
    }

    /// <summary>
    /// The teams a user can open in Team Health — the Team dropdown, and the team
    /// context for the ERAN Coach chat. Empty <see cref="Teams"/> when they have none.
    /// </summary>
    public class UserTeamsDto
    {
        public int UserId { get; set; }
        public string? UserName { get; set; }
        public uint? ProfileId { get; set; }

        /// <summary>The user's role ids: 1 Profile Admin, 2 Team Manager, 3 RRIDA Admin, 4 Portfolio Manager.</summary>
        public List<int> Roles { get; set; } = new();

        public int TotalTeams { get; set; }
        public List<UserTeamDto> Teams { get; set; } = new();
    }

    public class UserTeamDto
    {
        public uint TeamId { get; set; }
        public string TeamName { get; set; } = string.Empty;

        /// <summary>The team's join code.</summary>
        public string? TeamCode { get; set; }

        public uint ProfileId { get; set; }

        /// <summary>The parent team, or null for a top-level team.</summary>
        public int? ParentTeamId { get; set; }

        public int? TeamSize { get; set; }

        /// <summary>Always "Active": deleted teams are never returned.</summary>
        public string Status { get; set; } = "Active";

        /// <summary>The team's current Team Managers.</summary>
        public List<UserTeamManagerDto> Managers { get; set; } = new();
    }

    public class UserTeamManagerDto
    {
        public int UserId { get; set; }
        public string? Name { get; set; }
        public string? Email { get; set; }
    }
}
