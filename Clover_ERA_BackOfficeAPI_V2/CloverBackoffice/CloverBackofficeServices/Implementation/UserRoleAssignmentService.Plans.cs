using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CloverBackoffice.Components.Enums;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.BusinessModel;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Implementation;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Services.Implementation
{
    // Validation of the relationship a role needs (read-only, before any write),
    // plus the small write helpers the main flow composes.
    public partial class UserRoleAssignmentService
    {
        private sealed class TeamPlan
        {
            public bool CreateNew { get; set; }
            public uint? UnitId { get; set; }
            public string Name { get; set; } = string.Empty;
            public bool AlreadyManaged { get; set; }
        }

        private sealed class PortfolioPlan
        {
            public int ManagerId { get; set; }
            public string ManagerName { get; set; } = string.Empty;
            public bool AlreadyInPortfolio { get; set; }
        }

        // First key of pg_advisory_xact_lock(int, int); the second is the profile id.
        private const int LicenseLockNamespace = 16016;

        /// <summary>
        /// The licence rule for Add User. Creating a user, or giving one the Team
        /// Manager role, is never blocked by licences: a new Team Manager is given
        /// one free licence after the commit when one exists, other roles none.
        ///   new team → must fit the team capacity (licences x 3)
        ///   existing Team Manager given another team → the 3-team limit is checked earlier
        /// Must run inside the Add User transaction: the per-profile advisory lock
        /// is held until commit / rollback, so two concurrent requests cannot both
        /// take the last team slot.
        /// </summary>
        private async Task<AddUserOutcomeDto?> CheckLicenseAsync(
            TeamPlan? team, uint profileId, CancellationToken ct)
        {
            if (team?.CreateNew != true)
                return null;

            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({LicenseLockNamespace}, {(int)profileId})", ct);

            var error = (await _licenseService.GetLicenseUsageAsync(profileId)).TeamCapacityError();
            if (error == null)
                return null;

            var outcome = AddUserOutcomeDto.Fail(error);
            outcome.IsLicenseLimitReached = true;
            return outcome;
        }

        private static AddUserOutcomeDto Confirm(AddUserConfirmationDto confirmation, string message) =>
            new() { ErrorMessage = message, Confirmation = confirmation };

        /// <summary>
        /// Team Manager: an existing team of the SAME profile, or a new one. A team
        /// already managed by someone else needs replaceExistingManager.
        /// </summary>
        private async Task<(TeamPlan? Plan, AddUserOutcomeDto? Outcome)> PlanTeamAsync(
            UserUpsertDto dto, uint profileId, int targetUserId, CancellationToken ct)
        {
            if (dto.IsNewTeam)
            {
                var name = (dto.TeamName ?? string.Empty).Trim();

                // No name, no team. The user is saved without one and sets their
                // team up after logging in, so login reports managedTeams empty and
                // the client can offer "Set up your team". No licence is used.
                if (name.Length == 0)
                    return (null, null);

                if (name.Length > DataConstants.OrganisationalUnitNameLength)
                    name = name[..DataConstants.OrganisationalUnitNameLength];

                if (await _accountRegistration.BussinessNameExistsAsync(name, (int)profileId) != null)
                    return (null, AddUserOutcomeDto.Fail(ResponseMessages.Errors.TeamNameTaken));

                return (new TeamPlan { CreateNew = true, Name = name }, null);
            }

            var unitId = (uint)dto.ExistingUnitId!.Value;

            // Profile is part of the filter: another profile's team is "not found".
            var unit = await _context.BusinessUnits
                .AsNoTracking()
                .Where(bu => bu.Id == unitId && bu.ProfileId == profileId && bu.IsDel != true)
                .Select(bu => new { bu.Id, bu.Name })
                .FirstOrDefaultAsync(ct);

            if (unit == null)
                return (null, AddUserOutcomeDto.Fail(ResponseMessages.Errors.ManagerTeamNotFound));

            var plan = new TeamPlan { UnitId = unit.Id, Name = unit.Name };

            var incumbent = await (from ut in _context.UserTeams.AsNoTracking()
                                   join u in _context.Users.AsNoTracking() on ut.UserId equals u.UserId
                                   where ut.UnitId == unitId && !ut.IsUnAssigned
                                   orderby ut.CreatedDate descending
                                   select new { u.UserId, u.Name, u.Username })
                                  .FirstOrDefaultAsync(ct);

            if (incumbent == null)
                return (plan, null);

            if (incumbent.UserId == targetUserId)
            {
                plan.AlreadyManaged = true;
                return (plan, null);
            }

            if (!dto.ReplaceExistingManager)
            {
                return (null, Confirm(new AddUserConfirmationDto
                {
                    ConfirmationType = "ReplaceTeamManager",
                    ConfirmWith = "replaceExistingManager",
                    UnitId = unit.Id,
                    TeamName = unit.Name,
                    ExistingUserId = incumbent.UserId,
                    ExistingUserName = incumbent.Name,
                    ExistingUserEmail = incumbent.Username
                }, string.Format(ResponseMessages.Errors.TeamManagerReplaceConfirm, unit.Name, incumbent.Name)));
            }

            return (plan, null);
        }

        /// <summary>
        /// Portfolio Manager: a live Team Manager of the SAME profile, not the user
        /// themself, not already this user's own Portfolio Manager. A manager who
        /// already reports to someone else needs replaceExistingPortfolioManager.
        /// </summary>
        private async Task<(PortfolioPlan? Plan, AddUserOutcomeDto? Outcome)> PlanPortfolioAsync(
            UserUpsertDto dto, uint profileId, int targetUserId, CancellationToken ct)
        {
            var managerId = dto.ManagerId!.Value;

            if (managerId == targetUserId)
                return (null, AddUserOutcomeDto.Fail(ResponseMessages.Errors.PortfolioManagerSelf));

            var pid = (int)profileId;
            var manager = await _context.Users
                .AsNoTracking()
                .Where(u => u.UserId == managerId && u.ProfileId == pid && !u.IsDel)
                .Select(u => new
                {
                    u.UserId,
                    u.Name,
                    IsTeamManager = u.Roles.Any(r => r.UserGroupId == (int)UserGroups.TeamManager)
                })
                .FirstOrDefaultAsync(ct);

            if (manager == null)
                return (null, AddUserOutcomeDto.Fail(ResponseMessages.Errors.PortfolioManagerManagerNotFound));

            if (!manager.IsTeamManager)
                return (null, AddUserOutcomeDto.Fail(ResponseMessages.Errors.PortfolioManagerManagerNotEligible));

            if (targetUserId > 0 && await _context.PortfolioManagerAssignments.AnyAsync(a =>
                    a.PortfolioManagerUserId == managerId && a.ManagerUserId == targetUserId && !a.IsUnAssigned, ct))
                return (null, AddUserOutcomeDto.Fail(ResponseMessages.Errors.PortfolioManagerCycle));

            var plan = new PortfolioPlan { ManagerId = manager.UserId, ManagerName = manager.Name };

            var incumbent = await (from a in _context.PortfolioManagerAssignments.AsNoTracking()
                                   join u in _context.Users.AsNoTracking() on a.PortfolioManagerUserId equals u.UserId
                                   where a.ManagerUserId == managerId && !a.IsUnAssigned
                                   select new { u.UserId, u.Name, u.Username })
                                  .FirstOrDefaultAsync(ct);

            if (incumbent == null)
                return (plan, null);

            if (incumbent.UserId == targetUserId)
            {
                plan.AlreadyInPortfolio = true;
                return (plan, null);
            }

            if (!dto.ReplaceExistingPortfolioManager)
            {
                return (null, Confirm(new AddUserConfirmationDto
                {
                    ConfirmationType = "ReplacePortfolioManager",
                    ConfirmWith = "replaceExistingPortfolioManager",
                    ManagerId = manager.UserId,
                    ManagerName = manager.Name,
                    ExistingUserId = incumbent.UserId,
                    ExistingUserName = incumbent.Name,
                    ExistingUserEmail = incumbent.Username
                }, string.Format(ResponseMessages.Errors.PortfolioManagerReplaceConfirm, manager.Name, incumbent.Name)));
            }

            return (plan, null);
        }

        /// <summary>
        /// Adds the role if missing and re-derives Users.UserGroupId (highest role).
        /// No SaveChanges — the caller owns the transaction.
        /// </summary>
        private async Task<(bool Added, int Primary)> EnsureRoleAsync(
            int userId, int role, int actorUserId, bool stampCreator, CancellationToken ct)
        {
            var user = await _context.Users.FirstAsync(u => u.UserId == userId, ct);
            var rows = await _context.UserRoles.Where(r => r.UserId == userId).ToListAsync(ct);

            // Not backfilled yet: record the role the column already says they have,
            // or adding a lower role would demote them.
            if (rows.Count == 0 && user.UserGroupId > 0 && user.UserGroupId != role)
            {
                var legacy = new UserRole { UserId = userId, UserGroupId = user.UserGroupId, CreatedDate = DateTime.UtcNow };
                _context.UserRoles.Add(legacy);
                rows.Add(legacy);
            }

            var added = false;
            var row = rows.FirstOrDefault(r => r.UserGroupId == role);
            if (row == null)
            {
                row = new UserRole { UserId = userId, UserGroupId = role, CreatedBy = actorUserId, CreatedDate = DateTime.UtcNow };
                _context.UserRoles.Add(row);
                rows.Add(row);
                added = true;
            }
            else if (stampCreator && row.CreatedBy == null)
            {
                // Row just written by the ApplicationDbContext hook for a new user.
                row.CreatedBy = actorUserId;
            }

            var primary = RoleHierarchy.PrimaryOf(rows.Select(r => r.UserGroupId)) ?? role;
            if (user.UserGroupId != primary)
            {
                user.UserGroupId = primary;
                user.ModifiedDate = DateTime.UtcNow.ToString("o");
            }

            return (added, primary);
        }
    }
}
