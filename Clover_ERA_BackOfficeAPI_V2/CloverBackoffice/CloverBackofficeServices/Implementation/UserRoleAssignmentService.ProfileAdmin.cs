using System;
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
    // Assign / remove the Profile Admin role on an EXISTING user. Only UserRoles
    // and the derived Users.UserGroupId change; name, email, status and the
    // user's other roles are left as they are. No user is ever created here.
    public partial class UserRoleAssignmentService
    {
        // First key of pg_advisory_xact_lock(int, int) for Profile Admin role
        // changes; the second is the profile id. Two admins removing each other at
        // the same moment would otherwise both pass the last-admin check.
        private const int ProfileAdminLockNamespace = 16018;

        public async Task<ProfileAdminRoleOutcomeDto> AssignProfileAdminAsync(
            int targetUserId, int actorUserId, CancellationToken ct = default)
        {
            var (target, denied) = await ResolveProfileAdminTargetAsync(targetUserId, actorUserId, ct);
            if (denied != null) return denied;

            var roles = await GetRoleIdsAsync(targetUserId, ct);
            if (roles.Contains((int)UserGroups.ProfileAdmin))
                return ProfileAdminRoleOutcomeDto.Fail(ResponseMessages.Errors.ProfileAdminAlreadyAssigned);

            await using var transaction = await _context.Database.BeginTransactionAsync(ct);
            await EnsureRoleAsync(targetUserId, (int)UserGroups.ProfileAdmin, actorUserId, stampCreator: false, ct);
            await _context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            _logger.LogInformation("Profile Admin role assigned to user {UserId} (profile {ProfileId}) by {ActorId}.",
                targetUserId, target!.ProfileId, actorUserId);

            return await ProfileAdminResultAsync(targetUserId, ct);
        }

        public async Task<ProfileAdminRoleOutcomeDto> RemoveProfileAdminAsync(
            int targetUserId, int actorUserId, CancellationToken ct = default)
        {
            var (target, denied) = await ResolveProfileAdminTargetAsync(targetUserId, actorUserId, ct);
            if (denied != null) return denied;

            await using var transaction = await _context.Database.BeginTransactionAsync(ct);

            // Checked under the per-profile lock, so the count cannot change between
            // the check and the write.
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({ProfileAdminLockNamespace}, {target!.ProfileId ?? 0})", ct);

            var roles = await GetRoleIdsAsync(targetUserId, ct);
            if (!roles.Contains((int)UserGroups.ProfileAdmin))
                return ProfileAdminRoleOutcomeDto.Fail(ResponseMessages.Errors.ProfileAdminNotAssigned);

            // Same rule as delete: no self-removal, never the last active Profile Admin.
            var removalError = await ProfileAdminRemovalErrorAsync(targetUserId, actorUserId, refuseSelf: true, ct);
            if (removalError != null)
                return ProfileAdminRoleOutcomeDto.Fail(removalError);

            // The user keeps their other roles. With none left they stay on the
            // profile with no role at all; the derived column then points at the
            // "No Role" group, never still at Profile Admin — the legacy fallback in
            // GetRoleIdsAsync and ~35 column readers would otherwise keep them admin.
            var remaining = roles.Where(r => r != (int)UserGroups.ProfileAdmin).ToList();

            var rows = await _context.UserRoles
                .Where(r => r.UserId == targetUserId && r.UserGroupId == (int)UserGroups.ProfileAdmin)
                .ToListAsync(ct);
            _context.UserRoles.RemoveRange(rows);

            var user = await _context.Users.FirstAsync(u => u.UserId == targetUserId, ct);
            var primary = RoleHierarchy.PrimaryOf(remaining) ?? RoleHierarchy.NoRoleGroupId;
            if (user.UserGroupId != primary)
            {
                user.UserGroupId = primary;
                user.ModifiedDate = DateTime.UtcNow.ToString("o");
            }

            await _context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            _logger.LogInformation("Profile Admin role removed from user {UserId} (profile {ProfileId}) by {ActorId}.",
                targetUserId, target.ProfileId, actorUserId);

            return await ProfileAdminResultAsync(targetUserId, ct);
        }

        // The caller must be a Profile Admin (own profile only) or a RRIDA Admin
        // (any profile). A user outside the caller's profile answers "not found",
        // so another profile's user ids cannot be probed.
        private async Task<(TargetUser? Target, ProfileAdminRoleOutcomeDto? Denied)> ResolveProfileAdminTargetAsync(
            int targetUserId, int actorUserId, CancellationToken ct)
        {
            var actor = await _context.Users
                .AsNoTracking()
                .Where(u => u.UserId == actorUserId && !u.IsDel)
                .Select(u => new { u.UserId, u.ProfileId })
                .FirstOrDefaultAsync(ct);

            var actorRoles = actor == null ? new() : await GetRoleIdsAsync(actorUserId, ct);
            var isRridaAdmin = actorRoles.Contains((int)UserGroups.RridaAdmin);
            if (!isRridaAdmin && !actorRoles.Contains((int)UserGroups.ProfileAdmin))
                return (null, new ProfileAdminRoleOutcomeDto
                {
                    IsForbidden = true,
                    ErrorMessage = ResponseMessages.Errors.ProfileAdminRoleNotPermitted
                });

            var target = await _context.Users
                .AsNoTracking()
                .Where(u => u.UserId == targetUserId && !u.IsDel && u.ProfileId != null)
                .Select(u => new TargetUser(u.UserId, u.ProfileId))
                .FirstOrDefaultAsync(ct);

            if (target == null || (!isRridaAdmin && target.ProfileId != actor!.ProfileId))
                return (null, new ProfileAdminRoleOutcomeDto
                {
                    IsNotFound = true,
                    ErrorMessage = ResponseMessages.Errors.UserNotFound
                });

            return (target, null);
        }

        private async Task<ProfileAdminRoleOutcomeDto> ProfileAdminResultAsync(int userId, CancellationToken ct)
        {
            var user = await _context.Users
                .AsNoTracking()
                .Where(u => u.UserId == userId)
                .Select(u => new { u.UserId, u.Name, u.Username, u.ProfileId, u.UserGroupId })
                .FirstAsync(ct);

            var roles = await GetRolesAsync(userId, ct);

            return new ProfileAdminRoleOutcomeDto
            {
                IsSuccess = true,
                Data = new ProfileAdminRoleResultDto
                {
                    UserId = user.UserId,
                    Name = user.Name,
                    Email = user.Username,
                    ProfileId = user.ProfileId,
                    PrimaryUserGroupId = user.UserGroupId,
                    IsProfileAdmin = roles.Any(r => r.Id == (int)UserGroups.ProfileAdmin),
                    Roles = roles
                }
            };
        }

        private sealed record TargetUser(int UserId, int? ProfileId);
    }
}
