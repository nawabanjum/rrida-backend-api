using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CloverBackoffice.Components.Enums;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CloverBackoffice.Services.Implementation
{
    // Take the Team Manager role off an existing user, from Manage User. Their teams
    // stay, with no manager and nobody put in their place; their licence goes back to
    // the pool; their other roles, details and status are left as they are.
    public partial class UserRoleAssignmentService
    {
        private static readonly string PendingStatus = ((int)UserStatus.Pending).ToString();

        public async Task<TeamManagerRemovalOutcomeDto> RemoveTeamManagerAsync(
            int targetUserId, int actorUserId, CancellationToken ct = default)
        {
            var (target, denied) = await ResolveProfileAdminTargetAsync(targetUserId, actorUserId, ct);
            if (denied != null)
                return new TeamManagerRemovalOutcomeDto
                {
                    IsForbidden = denied.IsForbidden,
                    IsNotFound = denied.IsNotFound,
                    ErrorMessage = denied.IsForbidden
                        ? ResponseMessages.Errors.TeamManagerRemovalNotPermitted
                        : denied.ErrorMessage
                };

            List<uint> clearedUnitIds;
            int releasedLicenses;

            await using var transaction = await _context.Database.BeginTransactionAsync(ct);
            try
            {
                // The licence lock, so nobody can be given this user's licence, or this
                // user a new team, while it is being released.
                await LockProfileLicensesAsync((uint)target!.ProfileId!.Value, ct);

                var roles = await GetRoleIdsAsync(targetUserId, ct);
                if (!roles.Contains((int)UserGroups.TeamManager))
                {
                    await transaction.RollbackAsync(ct);
                    return TeamManagerRemovalOutcomeDto.Fail(ResponseMessages.Errors.TeamManagerNotAssigned);
                }

                var remaining = roles.Where(r => r != (int)UserGroups.TeamManager).ToList();

                // A replacement still waiting on their invitation goes too, or accepting
                // it would hand a team to someone who is no longer a manager.
                var teamRows = await _context.UserTeams
                    .Where(ut => ut.UserId == targetUserId && (!ut.IsUnAssigned || ut.IsPendingActivation))
                    .ToListAsync(ct);

                clearedUnitIds = teamRows.Where(r => !r.IsUnAssigned).Select(r => r.UnitId).Distinct().ToList();

                foreach (var row in teamRows)
                {
                    row.IsUnAssigned = true;
                    row.IsPendingActivation = false;
                }

                // Their line to a Portfolio Manager exists because they manage teams.
                // A Portfolio Manager's own reporting line uses the same rows and stays.
                if (!remaining.Contains((int)UserGroups.PortfolioManager))
                {
                    var reportingLines = await _context.PortfolioManagerAssignments
                        .Where(a => a.ManagerUserId == targetUserId && !a.IsUnAssigned)
                        .ToListAsync(ct);

                    foreach (var line in reportingLines)
                    {
                        line.IsUnAssigned = true;
                        line.UnAssignedDate = DateTime.UtcNow;
                    }
                }

                var roleRows = await _context.UserRoles
                    .Where(r => r.UserId == targetUserId && r.UserGroupId == (int)UserGroups.TeamManager)
                    .ToListAsync(ct);
                _context.UserRoles.RemoveRange(roleRows);

                // As Profile Admin removal: with no role left the derived column points
                // at "No Role", or the legacy fallback would keep them a Team Manager.
                var user = await _context.Users.FirstAsync(u => u.UserId == targetUserId, ct);
                var primary = RoleHierarchy.PrimaryOf(remaining) ?? RoleHierarchy.NoRoleGroupId;
                if (user.UserGroupId != primary)
                {
                    user.UserGroupId = primary;
                    user.ModifiedDate = DateTime.UtcNow.ToString("o");
                }

                await _context.SaveChangesAsync(ct);

                releasedLicenses = await ReleaseTeamManagerLicensesAsync(targetUserId, clearedUnitIds, actorUserId, ct);

                await transaction.CommitAsync(ct);
            }
            catch
            {
                try { await transaction.RollbackAsync(ct); } catch { /* already gone */ }
                throw;
            }

            _logger.LogInformation(
                "Team Manager role removed from user {UserId} (profile {ProfileId}) by {ActorId}: {Teams} team(s) cleared, {Licenses} licence release(s).",
                targetUserId, target.ProfileId, actorUserId, clearedUnitIds.Count, releasedLicenses);

            // As the manual unassign path: tell them, per team. The removal has happened
            // by now, so a mail that fails must not report it as failed.
            foreach (var unitId in clearedUnitIds)
            {
                try { await _onboardingNotification.NotifyManagerUnassignedAsync(targetUserId, unitId, ct); }
                catch (Exception ex) { _logger.LogWarning(ex, "Unassigned notification failed for user {UserId}, unit {UnitId}.", targetUserId, unitId); }
            }

            return await TeamManagerRemovalResultAsync(targetUserId, ct);
        }

        // A licence counts as consumed while it is held by a Team Manager OR actively on
        // a live team, so clearing the holder alone would leave it consumed. A team with
        // no manager left gives its coverage back. One that still has another manager
        // keeps it: the licence is theirs now.
        private async Task<int> ReleaseTeamManagerLicensesAsync(
            int userId, List<uint> clearedUnitIds, int actorUserId, CancellationToken ct)
        {
            var released = 0;

            foreach (var unitId in clearedUnitIds)
            {
                var stillManaged = await _context.UserTeams
                    .AnyAsync(ut => ut.UnitId == unitId && ut.UserId != userId && !ut.IsUnAssigned, ct);

                if (stillManaged)
                    await _licenseService.ReconcileGrantsForUnitAsync(unitId, actorUserId);
                else
                    released += await _licenseService.ReleaseLicensesForUnitAsync(unitId, actorUserId);
            }

            return released + await _licenseService.ReleaseLicensesHeldByUserAsync(userId, actorUserId);
        }

        // Separate so tests can run on the in-memory database, which has no advisory locks.
        protected virtual Task LockProfileLicensesAsync(uint profileId, CancellationToken ct)
            => _context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({LicenseLockNamespace}, {(int)profileId})", ct);

        private async Task<TeamManagerRemovalOutcomeDto> TeamManagerRemovalResultAsync(int userId, CancellationToken ct)
        {
            var status = await _context.Users
                .AsNoTracking()
                .Where(u => u.UserId == userId)
                .Select(u => u.Status)
                .FirstAsync(ct);

            var roles = await GetRolesAsync(userId, ct);

            return new TeamManagerRemovalOutcomeDto
            {
                IsSuccess = true,
                Data = new TeamManagerRemovalResultDto
                {
                    UserId = userId,
                    Roles = roles.Select(r => r.Name).ToList(),
                    IsTeamManager = roles.Any(r => r.Id == (int)UserGroups.TeamManager),
                    Status = status == SuspendedStatus ? "Inactive" : status == PendingStatus ? "Pending" : "Active"
                }
            };
        }
    }
}
