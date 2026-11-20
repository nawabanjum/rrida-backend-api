using System;
using System.Collections.Generic;

namespace CloverBackoffice.Infrastructure.DTO
{
    // Manager-centric Team Structure page: the company, its managers (with the
    // teams each one runs and who they report to) and the teams nobody runs.
    //
    //   GET  /api/teamhierarchy/team-structure?profileId=56
    //   POST /api/teamhierarchy/team-structure/assign-team
    public class TeamStructureDto
    {
        public int ProfileId { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public TeamStructureSummary Summary { get; set; } = new();

        // Team Managers, Portfolio Managers and anyone else holding a team, by
        // name. The tree is built from PortfolioManagerUserId: a manager whose
        // PortfolioManagerUserId is null sits directly under the company.
        public List<TeamStructureManagerDto> Managers { get; set; } = new();

        // Teams with no active manager (e.g. their manager was removed in Users).
        public List<TeamStructureTeamDto> UnassignedTeams { get; set; } = new();

        // Banner messages for the top of the page.
        public List<TeamStructureAlertDto> Alerts { get; set; } = new();
    }

    public class TeamStructureSummary
    {
        public int TotalTeams { get; set; }
        public int TotalManagers { get; set; }
        public int UnassignedTeams { get; set; }

        // Teams run by a manager without an active licence (or who is inactive).
        public int TeamsWithoutLicensedManager { get; set; }

        // Managers whose licence ends within 30 days and will not renew.
        public int LicencesExpiringSoon { get; set; }
        public int MaxTeamsPerManager { get; set; }
    }

    public class TeamStructureManagerDto
    {
        public int UserId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Email { get; set; }
        public List<int> Roles { get; set; } = new();
        public bool IsTeamManager { get; set; }
        public bool IsPortfolioManager { get; set; }

        // "Active" | "Pending" | "Inactive"
        public string Status { get; set; } = string.Empty;
        public bool IsActive { get; set; }

        // False for an inactive manager, or when the manager's teams are not
        // covered by a live licence. Their teams are not visible in Team Health.
        public bool HasActiveLicence { get; set; }

        // The licence ending soonest across the manager's teams (an expired one
        // first). Null when the manager has no team with a licence.
        public TeamLicenceDto? Licence { get; set; }

        // Who this manager reports to (Portfolio Manager). Null = the company.
        public int? PortfolioManagerUserId { get; set; }
        public int ReportingManagerCount { get; set; }

        public int TeamCount { get; set; }

        // True when one more team can be dropped on this manager.
        public bool CanTakeMoreTeams { get; set; }
        public List<TeamStructureTeamDto> Teams { get; set; } = new();
    }

    public class TeamStructureTeamDto
    {
        public uint UnitId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? ShortCode { get; set; }
        public int? TeamSize { get; set; }

        public int? ManagerUserId { get; set; }
        public string? ManagerName { get; set; }
        public DateTime? AssignedDate { get; set; }

        // An invited manager who takes the team over once they accept.
        public int? PendingManagerUserId { get; set; }

        // The team itself holds a live licence (purchased or complimentary).
        public bool HasActiveLicence { get; set; }
        public TeamLicenceDto Licence { get; set; } = new();

        // Licensed AND run by an active manager — shown in Team Health.
        public bool IsVisible { get; set; }
    }

    // Licence behind a team (or, on a manager, the one ending soonest across
    // their teams). Drives the "licence expires in N days" banner.
    public class TeamLicenceDto
    {
        public const int ExpiringSoonDays = 30;

        public bool HasLicence { get; set; }

        // "Active" | "Expired" | "None"
        public string Status { get; set; } = "None";
        public string? LicenceKey { get; set; }

        // "Trial" | "Paid"; null when there is no licence.
        public string? LicenceType { get; set; }
        public DateTime? StartDate { get; set; }

        // Grace period included. Null = never expires (legacy / exempt).
        public DateTime? ExpiryDate { get; set; }
        public int? DaysRemaining { get; set; }
        public bool IsExpired { get; set; }

        // Active and ending within ExpiringSoonDays.
        public bool IsExpiringSoon { get; set; }
        public bool WillAutoRenew { get; set; }

        // Covered by another team's licence of the same manager.
        public bool IsComplimentary { get; set; }
        public uint? ParentUnitId { get; set; }

        // e.g. "Monthly - Renews October 30, 2026" / "Trial - Expires September 30, 2026"
        public string? SubscriptionMessage { get; set; }
    }

    public class TeamStructureAlertDto
    {
        // "LicenceExpiring" | "UnlicensedManager" | "UnassignedTeams"
        public string Type { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public int? ManagerUserId { get; set; }
        public List<uint> UnitIds { get; set; } = new();

        // LicenceExpiring only.
        public int? DaysRemaining { get; set; }
        public DateTime? ExpiryDate { get; set; }
    }

    // Drop a team on a Team Manager. Assigns a team with no manager, or moves it
    // from its current manager. Names match the tenant guard (profileId, *UnitId,
    // userId) so every id is checked against the caller's company.
    public class AssignTeamManagerDto
    {
        public int ProfileId { get; set; }

        // The team being moved.
        public uint UnitId { get; set; }

        // The target Team Manager, or a Portfolio Manager without a Team Manager
        // licence (see AssignTeamManagerLicence).
        public int UserId { get; set; }

        // Dropping on a Portfolio Manager who is not a Team Manager: the first
        // call (false) only answers whether a licence is free; resend with true
        // to assign the licence, add the Team Manager role and move the team.
        public bool AssignTeamManagerLicence { get; set; }
    }

    // Drag a manager onto another manager (or onto the company). The manager
    // they are dropped on becomes their Portfolio Manager; null = the company.
    public class MoveManagerDto
    {
        public int ProfileId { get; set; }

        // The manager being moved.
        public int UserId { get; set; }

        // The manager they now report to. Null or 0 = directly under the company.
        public int? PortfolioManagerUserId { get; set; }
    }

    public class ManagerMoveResultDto
    {
        public int UserId { get; set; }
        public string Name { get; set; } = string.Empty;

        // Null = they reported to the company.
        public int? PreviousPortfolioManagerUserId { get; set; }
        public string? PreviousPortfolioManagerName { get; set; }

        // Null = they now report to the company (name is then the company name).
        public int? PortfolioManagerUserId { get; set; }
        public string? PortfolioManagerName { get; set; }

        // True when the new parent was given the Portfolio Manager role by this move.
        public bool PortfolioManagerRoleGranted { get; set; }

        // Users who lost the Portfolio Manager role because this move left nobody
        // reporting to them: the previous parent, and further up when that parent
        // had no other manager role and so left the structure too.
        public List<int> PortfolioManagerRoleRemovedUserIds { get; set; } = new();

        // False when the manager already reported there (nothing changed).
        public bool Changed { get; set; }

        public TeamStructureDto Structure { get; set; } = new();
    }

    public class TeamAssignmentResultDto
    {
        public uint UnitId { get; set; }
        public string TeamName { get; set; } = string.Empty;
        public int? PreviousManagerUserId { get; set; }
        public string? PreviousManagerName { get; set; }
        public int ManagerUserId { get; set; }
        public string ManagerName { get; set; } = string.Empty;

        // False when the team was already with this manager (nothing changed).
        public bool Changed { get; set; }

        // Portfolio Manager without a Team Manager licence: nothing was changed.
        // LicenceAvailable says whether one is free; resend with the ConfirmWith
        // flag set to true to assign it and move the team.
        public bool RequiresTeamManagerLicence { get; set; }
        public bool LicenceAvailable { get; set; }
        public int AvailableLicences { get; set; }
        public string? ConfirmWith { get; set; }

        // True when this call gave the manager a Team Manager licence and role.
        public bool TeamManagerLicenceAssigned { get; set; }
        public int? CompanyLicenseId { get; set; }

        // The whole structure after the change, so the page re-renders from it.
        public TeamStructureDto Structure { get; set; } = new();
    }
}
