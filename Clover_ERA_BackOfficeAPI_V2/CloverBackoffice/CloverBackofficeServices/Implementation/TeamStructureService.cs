using CloverBackoffice.Components.Enums;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Implementation;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Implementation
{
    // Manager-centric Team Structure page. The team-centric dashboard tree stays
    // on get-teams-structure; this one is a light read of who runs which team,
    // plus the move itself, which goes through CompanyTeamService so the
    // incumbent is released, the 3-team limit, licence coverage and the emails
    // behave exactly as on assign-user-to-team.
    public class TeamStructureService : ITeamStructureService
    {
        // First key of pg_advisory_xact_lock(int, int); the second is the profile
        // id. Two manager moves in one company queue here, so two moves that are
        // each fine alone can never combine into a reporting loop.
        private const int ManagerMoveLockNamespace = 16021;

        private static readonly string InactiveStatus = ((int)UserStatus.Suspend).ToString();
        private static readonly string PendingStatus = ((int)UserStatus.Pending).ToString();

        private readonly ApplicationDbContext _context;
        private readonly ICompanyTeamService _teamService;
        private readonly ICompanyLicenseService _licenseService;
        private readonly ILicensedPeriodService _licensedPeriod;

        public TeamStructureService(ApplicationDbContext context, ICompanyTeamService teamService, ICompanyLicenseService licenseService, ILicensedPeriodService licensedPeriod)
        {
            _context = context;
            _teamService = teamService;
            _licenseService = licenseService;
            _licensedPeriod = licensedPeriod;
        }

        public async Task<TeamStructureDto?> GetTeamStructureAsync(int profileId)
        {
            var company = await _context.Profiles.AsNoTracking()
                .Where(p => p.Id == profileId)
                .Select(p => new { p.Id, p.ProfileName })
                .FirstOrDefaultAsync();
            if (company == null)
                return null;

            var teams = await _context.BusinessUnits.AsNoTracking()
                .Where(b => b.Profile.Id == profileId && b.IsDel != true)
                .OrderBy(b => b.Name)
                .Select(b => new TeamStructureTeamDto
                {
                    UnitId = b.Id,
                    Name = b.Name,
                    ShortCode = b.BusinessUnitShortCode,
                    TeamSize = b.TeamSize
                })
                .ToListAsync();
            var teamIds = teams.Select(t => t.UnitId).ToList();

            // Active assignments, plus invited replacements still waiting to accept.
            var rows = await _context.UserTeams.AsNoTracking()
                .Where(ut => teamIds.Contains(ut.UnitId) && (!ut.IsUnAssigned || ut.IsPendingActivation))
                .Select(ut => new { ut.Id, ut.UnitId, ut.UserId, ut.CreatedDate, ut.IsUnAssigned, ut.IsPendingActivation })
                .ToListAsync();
            var holderIds = rows.Select(r => r.UserId).Distinct().ToList();

            // Team Managers and Portfolio Managers (UserRoles, legacy UserGroupId
            // only for a user with no roles) and anyone else holding a team.
            var teamManager = (int)UserGroups.TeamManager;
            var portfolioManager = (int)UserGroups.PortfolioManager;
            var users = await _context.Users.AsNoTracking()
                .Where(u => u.ProfileId == profileId && !u.IsDel
                            && (holderIds.Contains(u.UserId)
                                || u.Roles.Any(r => r.UserGroupId == teamManager || r.UserGroupId == portfolioManager)
                                || (!u.Roles.Any() && (u.UserGroupId == teamManager || u.UserGroupId == portfolioManager))))
                .OrderBy(u => u.Name)
                .Select(u => new
                {
                    u.UserId,
                    u.Name,
                    u.Username,
                    u.Status,
                    u.UserGroupId,
                    Roles = u.Roles.Select(r => r.UserGroupId).ToList()
                })
                .ToListAsync();
            var userById = users.ToDictionary(u => u.UserId);

            var portfolioByManager = (await _context.PortfolioManagerAssignments.AsNoTracking()
                    .Where(a => a.ProfileId == (uint)profileId && !a.IsUnAssigned)
                    .Select(a => new { a.Id, a.ManagerUserId, a.PortfolioManagerUserId })
                    .ToListAsync())
                .GroupBy(a => a.ManagerUserId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.Id).First().PortfolioManagerUserId);

            var licensedUnitIds = teamIds.Count == 0
                ? new HashSet<long>()
                : await _licensedPeriod.GetActivelyLicensedUnitIdsAsync(teamIds.Select(id => (long)id));
            var profileHasLicence = (await _licenseService.GetLicenseUsageAsync((uint)profileId)).TotalLicenses > 0;
            var licences = await _teamService.GetTeamLicencesAsync(profileId, teamIds);

            bool IsActive(string? status) => status != InactiveStatus;

            // Teams each manager may hold: 3, or more after a RRIDA Admin extension.
            var limits = await _context.GetProfileTeamLimitsAsync((uint)profileId);
            var teamLimitByUser = new Dictionary<int, int>();
            foreach (var u in users)
                teamLimitByUser[u.UserId] = await _context.ManagerTeamLimitAsync(u.UserId, limits);

            foreach (var team in teams)
            {
                var active = rows
                    .Where(r => r.UnitId == team.UnitId && !r.IsUnAssigned)
                    .OrderByDescending(r => r.CreatedDate).ThenByDescending(r => r.Id)
                    .FirstOrDefault();
                var pending = rows
                    .Where(r => r.UnitId == team.UnitId && r.IsPendingActivation)
                    .OrderByDescending(r => r.CreatedDate).ThenByDescending(r => r.Id)
                    .FirstOrDefault();

                // A row pointing at a removed user or another company is no manager.
                if (active != null && userById.TryGetValue(active.UserId, out var manager))
                {
                    team.ManagerUserId = manager.UserId;
                    team.ManagerName = manager.Name;
                    team.AssignedDate = active.CreatedDate;
                }

                team.PendingManagerUserId = pending?.UserId;
                team.HasActiveLicence = licensedUnitIds.Contains(team.UnitId);
                team.Licence = licences.TryGetValue(team.UnitId, out var licence) ? licence : new TeamLicenceDto();
                team.IsVisible = team.HasActiveLicence && team.ManagerUserId.HasValue
                                 && IsActive(userById[team.ManagerUserId.Value].Status);
            }

            var managers = users.Select(u =>
            {
                var roles = u.Roles.Count > 0 ? u.Roles.Distinct().OrderBy(r => r).ToList() : new List<int> { u.UserGroupId };
                var isTeamManager = roles.Contains(teamManager);
                var isActive = IsActive(u.Status);
                var own = teams.Where(t => t.ManagerUserId == u.UserId).ToList();

                // Same count as the team limit: active teams plus pending handovers.
                var teamsHeld = rows
                    .Where(r => r.UserId == u.UserId && (!r.IsUnAssigned || r.IsPendingActivation))
                    .Select(r => r.UnitId).Distinct().Count();

                return new TeamStructureManagerDto
                {
                    UserId = u.UserId,
                    Name = u.Name,
                    Email = u.Username,
                    Roles = roles,
                    IsTeamManager = isTeamManager,
                    IsPortfolioManager = roles.Contains(portfolioManager),
                    Status = u.Status == InactiveStatus ? "Inactive" : u.Status == PendingStatus ? "Pending" : "Active",
                    IsActive = isActive,
                    // A licence is held by a Team Manager; with teams, it shows as
                    // live licence coverage on every one of them.
                    HasActiveLicence = isActive && (own.Count > 0
                        ? own.All(t => t.HasActiveLicence)
                        : isTeamManager && profileHasLicence),
                    Licence = own.Select(t => t.Licence)
                        .Where(l => l.HasLicence)
                        .OrderByDescending(l => l.IsExpired)
                        .ThenBy(l => l.ExpiryDate ?? DateTime.MaxValue)
                        .FirstOrDefault(),
                    PortfolioManagerUserId = portfolioByManager.TryGetValue(u.UserId, out var pm)
                                             && pm != u.UserId && userById.ContainsKey(pm) ? pm : null,
                    TeamCount = own.Count,
                    CanTakeMoreTeams = isTeamManager && isActive && profileHasLicence
                                       && teamsHeld < teamLimitByUser[u.UserId],
                    Teams = own
                };
            }).ToList();

            foreach (var manager in managers)
                manager.ReportingManagerCount = managers.Count(m => m.PortfolioManagerUserId == manager.UserId);

            var unassigned = teams.Where(t => !t.ManagerUserId.HasValue).ToList();

            var alerts = new List<TeamStructureAlertDto>();

            // "Neil Robertson's licence expires in 24 days…" — only when it will
            // not renew by itself.
            var expiring = managers
                .Where(m => m.TeamCount > 0 && m.HasActiveLicence && m.Licence != null
                            && m.Licence.IsExpiringSoon && !m.Licence.WillAutoRenew)
                .OrderBy(m => m.Licence!.DaysRemaining)
                .ToList();
            foreach (var manager in expiring)
            {
                var days = manager.Licence!.DaysRemaining ?? 0;
                var n = manager.TeamCount;
                alerts.Add(new TeamStructureAlertDto
                {
                    Type = "LicenceExpiring",
                    ManagerUserId = manager.UserId,
                    UnitIds = manager.Teams.Select(t => t.UnitId).ToList(),
                    DaysRemaining = days,
                    ExpiryDate = manager.Licence.ExpiryDate,
                    Message = string.Format(ResponseMessages.Errors.TeamStructureLicenceExpiringAlert,
                        manager.Name,
                        days == 0 ? "today" : days == 1 ? "in 1 day" : $"in {days} days",
                        manager.Licence.ExpiryDate?.ToString("d MMMM"),
                        n == 1 ? "Their team stops" : $"Their {n} teams stop")
                });
            }
            foreach (var manager in managers.Where(m => m.TeamCount > 0 && !m.HasActiveLicence))
            {
                var n = manager.TeamCount;
                alerts.Add(new TeamStructureAlertDto
                {
                    Type = "UnlicensedManager",
                    ManagerUserId = manager.UserId,
                    UnitIds = manager.Teams.Select(t => t.UnitId).ToList(),
                    Message = string.Format(
                        manager.IsActive ? ResponseMessages.Errors.TeamStructureUnlicensedManagerAlert
                                         : ResponseMessages.Errors.TeamStructureInactiveManagerAlert,
                        n, n == 1 ? "team is" : "teams are", manager.Name, n == 1 ? "It is" : "They are")
                });
            }
            if (unassigned.Count > 0)
            {
                var n = unassigned.Count;
                alerts.Add(new TeamStructureAlertDto
                {
                    Type = "UnassignedTeams",
                    UnitIds = unassigned.Select(t => t.UnitId).ToList(),
                    Message = string.Format(ResponseMessages.Errors.TeamStructureUnassignedAlert,
                        n, n == 1 ? "team has" : "teams have", n == 1 ? "it" : "them")
                });
            }

            return new TeamStructureDto
            {
                ProfileId = (int)company.Id,
                CompanyName = company.ProfileName,
                Managers = managers,
                UnassignedTeams = unassigned,
                Alerts = alerts,
                Summary = new TeamStructureSummary
                {
                    TotalTeams = teams.Count,
                    TotalManagers = managers.Count,
                    UnassignedTeams = unassigned.Count,
                    TeamsWithoutLicensedManager = managers.Where(m => !m.HasActiveLicence).Sum(m => m.TeamCount),
                    LicencesExpiringSoon = expiring.Count,
                    MaxTeamsPerManager = limits.TeamsPerLicense
                }
            };
        }

        public async Task<(TeamAssignmentResultDto? Result, string? Error)> AssignTeamAsync(AssignTeamManagerDto dto, int actorUserId)
        {
            if (dto == null || dto.ProfileId <= 0 || dto.UnitId == 0 || dto.UserId <= 0)
                return (null, ResponseMessages.Errors.TeamStructureInvalidRequest);

            var team = await _context.BusinessUnits.AsNoTracking()
                .Where(b => b.Id == dto.UnitId && b.Profile.Id == dto.ProfileId && b.IsDel != true)
                .Select(b => new { b.Id, b.Name })
                .FirstOrDefaultAsync();
            if (team == null)
                return (null, ResponseMessages.Errors.ManagerTeamNotFound);

            var target = await _context.Users.AsNoTracking()
                .Where(u => u.UserId == dto.UserId && u.ProfileId == dto.ProfileId && !u.IsDel)
                .Select(u => new { u.UserId, u.Name, u.Status, u.UserGroupId, Roles = u.Roles.Select(r => r.UserGroupId).ToList() })
                .FirstOrDefaultAsync();
            if (target == null)
                return (null, ResponseMessages.Errors.TeamStructureManagerNotFound);

            var roles = target.Roles.Count > 0 ? target.Roles : new List<int> { target.UserGroupId };
            var isTeamManager = roles.Contains((int)UserGroups.TeamManager);

            // A Portfolio Manager, Profile Admin or user with no role can be dropped on
            // too: they become a Team Manager by taking a Team Manager licence (below).
            // Anyone else still cannot.
            if (!isTeamManager && !RoleHierarchy.CanBecomeTeamManager(roles))
                return (null, string.Format(ResponseMessages.Errors.TeamStructureNotTeamManager, target.Name));

            if (target.Status == InactiveStatus)
                return (null, string.Format(ResponseMessages.Errors.TeamStructureManagerInactive, target.Name));

            var currentUserId = await _context.UserTeams.AsNoTracking()
                .Where(ut => ut.UnitId == dto.UnitId && !ut.IsUnAssigned)
                .OrderByDescending(ut => ut.CreatedDate).ThenByDescending(ut => ut.Id)
                .Select(ut => (int?)ut.UserId)
                .FirstOrDefaultAsync();
            var current = currentUserId == null
                ? null
                : await _context.Users.AsNoTracking()
                    .Where(u => u.UserId == currentUserId.Value)
                    .Select(u => new { u.UserId, u.Name })
                    .FirstOrDefaultAsync();

            var result = new TeamAssignmentResultDto
            {
                UnitId = team.Id,
                TeamName = team.Name,
                PreviousManagerUserId = current?.UserId,
                PreviousManagerName = current?.Name,
                ManagerUserId = target.UserId,
                ManagerName = target.Name
            };

            // Dropped back on its own manager: nothing to do.
            if (current?.UserId == target.UserId)
            {
                result.Structure = await GetTeamStructureAsync(dto.ProfileId) ?? new TeamStructureDto();
                return (result, null);
            }

            LicenseAssignmentResultDto? promotion = null;
            if (isTeamManager)
            {
                // No live licence in the profile means no Team Manager can hold a team.
                if ((await _licenseService.GetLicenseUsageAsync((uint)dto.ProfileId)).TotalLicenses <= 0)
                    return (null, string.Format(ResponseMessages.Errors.TeamStructureNoLicence, target.Name));
            }
            else
            {
                // Portfolio Manager / Profile Admin: validate, check a licence is free, and only then
                // (on confirmation) assign the licence + Team Manager role and move
                // the team. Nothing is written until the admin confirms.
                var available = (await _licenseService.GetLicenseUsageAsync((uint)dto.ProfileId)).AvailableLicenses;
                result.RequiresTeamManagerLicence = true;
                result.LicenceAvailable = available > 0;
                result.AvailableLicences = available;

                if (available <= 0 || !dto.AssignTeamManagerLicence)
                {
                    result.ConfirmWith = available > 0 ? "assignTeamManagerLicence" : null;
                    return (result, null);
                }

                // Checked before the licence is given, so a team the manager cannot
                // take never costs a licence.
                var limitError = await _context.ManagerTeamLimitErrorAsync(target.UserId, team.Id);
                if (limitError != null)
                    return (null, limitError);

                try
                {
                    promotion = await _licenseService.ExecuteLifecycleAsync(new LicenseAssignmentRequestDto
                    {
                        Action = LicenseAssignmentAction.Assign,
                        ManagerId = target.UserId
                    }, actorUserId);
                }
                catch (InvalidOperationException ex)
                {
                    // e.g. the last free licence was taken a moment ago.
                    return (null, ex.Message);
                }

                result.RequiresTeamManagerLicence = false;
                result.TeamManagerLicenceAssigned = true;
                result.CompanyLicenseId = promotion.CompanyLicenseId;
            }

            string? assignError = null;
            try
            {
                // Releases the incumbent and any pending handover, applies the
                // 3-team limit, re-covers licences and sends the emails.
                var assigned = await _teamService.AssignUserToTeamAsync(
                    new UserTeamDto { UserId = target.UserId, UnitId = team.Id, IsReadOnly = false }, actorUserId);
                if (assigned == null)
                    assignError = ResponseMessages.Errors.UserAssignteamFail;
            }
            catch (InvalidOperationException ex)
            {
                // e.g. the manager already runs the maximum number of teams.
                assignError = ex.Message;
            }
            catch (Exception) when (promotion != null)
            {
                await RevertPromotionAsync(target.UserId, promotion, actorUserId);
                throw;
            }

            if (assignError != null)
            {
                // The team did not move, so the licence and role given for it are
                // taken back: no half-done Team Manager.
                if (promotion != null)
                {
                    await RevertPromotionAsync(target.UserId, promotion, actorUserId);
                    return (null, ResponseMessages.Errors.TeamManagerLicenceAssignFailed + " " + assignError);
                }
                return (null, assignError);
            }

            result.Changed = true;
            result.Structure = await GetTeamStructureAsync(dto.ProfileId) ?? new TeamStructureDto();
            return (result, null);
        }

        private Task RevertPromotionAsync(int userId, LicenseAssignmentResultDto promotion, int actorUserId)
            => _licenseService.RevertTeamManagerPromotionAsync(
                userId, promotion.CompanyLicenseId, promotion.LicenseNewlyAssigned, actorUserId);

        public async Task<(ManagerMoveResultDto? Result, string? Error)> MoveManagerAsync(MoveManagerDto dto, int actorUserId)
        {
            if (dto == null || dto.ProfileId <= 0 || dto.UserId <= 0)
                return (null, ResponseMessages.Errors.MoveManagerInvalidRequest);

            var targetId = dto.PortfolioManagerUserId is > 0 ? dto.PortfolioManagerUserId : null;
            if (targetId == dto.UserId)
                return (null, ResponseMessages.Errors.MoveManagerOntoSelf);

            var profileName = await _context.Profiles.AsNoTracking()
                .Where(p => p.Id == dto.ProfileId)
                .Select(p => p.ProfileName)
                .FirstOrDefaultAsync();
            if (profileName == null)
                return (null, ResponseMessages.Errors.InvalidCompanyUnit);

            var teamManager = (int)UserGroups.TeamManager;
            var portfolioManager = (int)UserGroups.PortfolioManager;

            var ids = targetId.HasValue ? new[] { dto.UserId, targetId.Value } : new[] { dto.UserId };
            var people = await _context.Users.AsNoTracking()
                .Where(u => ids.Contains(u.UserId) && u.ProfileId == dto.ProfileId && !u.IsDel)
                .Select(u => new { u.UserId, u.Name, u.Status, u.UserGroupId, Roles = u.Roles.Select(r => r.UserGroupId).ToList() })
                .ToListAsync();

            bool IsManager(List<int> roles, int legacy)
            {
                var effective = roles.Count > 0 ? roles : new List<int> { legacy };
                return effective.Contains(teamManager) || effective.Contains(portfolioManager);
            }

            var moved = people.FirstOrDefault(u => u.UserId == dto.UserId);
            if (moved == null)
                return (null, ResponseMessages.Errors.MoveManagerNotFound);

            // Someone holding a team shows in the structure even without a role.
            var holdsTeam = await _context.UserTeams.AnyAsync(ut => ut.UserId == moved.UserId && !ut.IsUnAssigned);
            if (!IsManager(moved.Roles, moved.UserGroupId) && !holdsTeam)
                return (null, string.Format(ResponseMessages.Errors.MoveManagerNotAManager, moved.Name));

            var target = targetId.HasValue ? people.FirstOrDefault(u => u.UserId == targetId.Value) : null;
            if (targetId.HasValue)
            {
                if (target == null)
                    return (null, ResponseMessages.Errors.MoveManagerTargetNotFound);
                if (!IsManager(target.Roles, target.UserGroupId))
                    return (null, string.Format(ResponseMessages.Errors.MoveManagerTargetNotAManager, target.Name));
                if (target.Status == InactiveStatus)
                    return (null, string.Format(ResponseMessages.Errors.MoveManagerTargetInactive, target.Name));
            }

            var result = new ManagerMoveResultDto
            {
                UserId = moved.UserId,
                Name = moved.Name,
                PortfolioManagerUserId = target?.UserId,
                PortfolioManagerName = target?.Name ?? profileName
            };

            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                await LockManagerMovesAsync(dto.ProfileId);

                // Reporting lines of the whole company, read under the lock.
                var live = await _context.PortfolioManagerAssignments
                    .Where(a => a.ProfileId == (uint)dto.ProfileId && !a.IsUnAssigned)
                    .ToListAsync();
                var parentOf = live
                    .GroupBy(a => a.ManagerUserId)
                    .ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.Id).First().PortfolioManagerUserId);

                var current = live.Where(a => a.ManagerUserId == moved.UserId).ToList();
                var currentParentId = parentOf.TryGetValue(moved.UserId, out var parent) ? (int?)parent : null;
                result.PreviousPortfolioManagerUserId = currentParentId;
                result.PreviousPortfolioManagerName = currentParentId.HasValue
                    ? await _context.Users.AsNoTracking()
                        .Where(u => u.UserId == currentParentId.Value)
                        .Select(u => u.Name)
                        .FirstOrDefaultAsync()
                    : profileName;

                // Dropped where they already are: nothing to do.
                if (currentParentId == targetId)
                {
                    await transaction.RollbackAsync();
                    result.Structure = await GetTeamStructureAsync(dto.ProfileId) ?? new TeamStructureDto();
                    return (result, null);
                }

                // A manager cannot go under one of their own reports: walk up from
                // the target; reaching the moved manager means a loop.
                if (target != null)
                {
                    var seen = new HashSet<int>();
                    int? walk = target.UserId;
                    while (walk.HasValue && seen.Add(walk.Value))
                    {
                        if (walk.Value == moved.UserId)
                        {
                            await transaction.RollbackAsync();
                            return (null, string.Format(ResponseMessages.Errors.MoveManagerCircular, moved.Name, target.Name));
                        }
                        walk = parentOf.TryGetValue(walk.Value, out var up) ? up : null;
                    }
                }

                // Close the old reporting line (history row kept).
                var now = DateTime.UtcNow;
                foreach (var row in current)
                {
                    row.IsUnAssigned = true;
                    row.UnAssignedDate = now;
                }

                // Partial unique index on ManagerUserId: flip first, insert second.
                await _context.SaveChangesAsync();

                if (target != null)
                {
                    _context.PortfolioManagerAssignments.Add(new PortfolioManagerAssignment
                    {
                        PortfolioManagerUserId = target.UserId,
                        ManagerUserId = moved.UserId,
                        ProfileId = (uint)dto.ProfileId,
                        IsUnAssigned = false,
                        CreatedBy = actorUserId,
                        CreatedDate = now
                    });

                    // The manager dropped on now has someone reporting to them.
                    result.PortfolioManagerRoleGranted = await EnsurePortfolioManagerRoleAsync(target.UserId, actorUserId);
                }

                // The old parent has nobody left reporting to them: they are no
                // longer a Portfolio Manager. If that leaves them with no manager
                // role at all they leave the structure, which can empty their own
                // Portfolio Manager in turn, so walk up.
                var emptied = currentParentId;
                var walked = new HashSet<int>();
                while (emptied.HasValue && emptied != target?.UserId && walked.Add(emptied.Value)
                       && !live.Any(a => !a.IsUnAssigned && a.PortfolioManagerUserId == emptied.Value))
                {
                    var (removed, leftStructure) = await RemovePortfolioManagerRoleAsync(emptied.Value, live, now);
                    if (removed)
                        result.PortfolioManagerRoleRemovedUserIds.Add(emptied.Value);
                    emptied = leftStructure && parentOf.TryGetValue(emptied.Value, out var next) ? next : null;
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                try { await transaction.RollbackAsync(); } catch { /* already gone */ }
                throw;
            }

            result.Changed = true;
            result.Structure = await GetTeamStructureAsync(dto.ProfileId) ?? new TeamStructureDto();
            return (result, null);
        }

        // Virtual only so in-memory tests can skip the advisory lock.
        protected virtual Task LockManagerMovesAsync(int profileId)
            => _context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({ManagerMoveLockNamespace}, {profileId})");

        // Adds the Portfolio Manager role if missing and re-derives
        // Users.UserGroupId (highest role), as Add User does. No SaveChanges —
        // the caller owns the transaction. True when the role was added.
        private async Task<bool> EnsurePortfolioManagerRoleAsync(int userId, int actorUserId)
        {
            var role = (int)UserGroups.PortfolioManager;
            var user = await _context.Users.FirstAsync(u => u.UserId == userId);
            var rows = await _context.UserRoles.Where(r => r.UserId == userId).ToListAsync();
            if (rows.Any(r => r.UserGroupId == role))
                return false;

            // Not backfilled yet: keep the role the column already gives them.
            if (rows.Count == 0 && user.UserGroupId > 0 && user.UserGroupId != role)
            {
                var legacy = new UserRole { UserId = userId, UserGroupId = user.UserGroupId, CreatedDate = DateTime.UtcNow };
                _context.UserRoles.Add(legacy);
                rows.Add(legacy);
            }

            var added = new UserRole { UserId = userId, UserGroupId = role, CreatedBy = actorUserId, CreatedDate = DateTime.UtcNow };
            _context.UserRoles.Add(added);
            rows.Add(added);

            var primary = RoleHierarchy.PrimaryOf(rows.Select(r => r.UserGroupId)) ?? role;
            if (user.UserGroupId != primary)
            {
                user.UserGroupId = primary;
                user.ModifiedDate = DateTime.UtcNow.ToString("o");
            }
            return true;
        }

        // Drops the Portfolio Manager role and re-derives Users.UserGroupId, as
        // the Team Manager removal does (no role left = "No Role"). Left with no
        // Team Manager role and no team, they are out of the structure, so their
        // own reporting line (one of the live rows) is closed too. No SaveChanges —
        // the caller owns the transaction.
        private async Task<(bool Removed, bool LeftStructure)> RemovePortfolioManagerRoleAsync(
            int userId, List<PortfolioManagerAssignment> live, DateTime now)
        {
            var role = (int)UserGroups.PortfolioManager;
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);
            if (user == null)
                return (false, false);

            var rows = await _context.UserRoles.Where(r => r.UserId == userId).ToListAsync();
            var pmRows = rows.Where(r => r.UserGroupId == role).ToList();
            var legacyOnly = rows.Count == 0 && user.UserGroupId == role;
            if (pmRows.Count == 0 && !legacyOnly)
                return (false, false);

            _context.UserRoles.RemoveRange(pmRows);
            var remaining = rows.Where(r => r.UserGroupId != role).Select(r => r.UserGroupId).ToList();

            var primary = RoleHierarchy.PrimaryOf(remaining) ?? RoleHierarchy.NoRoleGroupId;
            if (user.UserGroupId != primary)
            {
                user.UserGroupId = primary;
                user.ModifiedDate = now.ToString("o");
            }

            if (remaining.Contains((int)UserGroups.TeamManager)
                || await _context.UserTeams.AnyAsync(ut => ut.UserId == userId && !ut.IsUnAssigned))
                return (true, false);

            foreach (var line in live.Where(a => a.ManagerUserId == userId && !a.IsUnAssigned))
            {
                line.IsUnAssigned = true;
                line.UnAssignedDate = now;
            }
            return (true, true);
        }
    }
}
