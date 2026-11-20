using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CloverBackoffice.Components.Enums;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CloverBackoffice.Services.Implementation
{
    /// <summary>
    /// Existing Manager → New Invited Manager (Pending) → New Manager (Active).
    ///
    /// Add User with replaceExistingManager for a user who has not accepted their
    /// invitation writes the new manager's UserTeams row unassigned with
    /// IsPendingActivation = true, so the outgoing manager keeps the team.
    /// Accepting the invitation (reset-password / post-password-landing) calls
    /// <see cref="FinalizePendingForUserAsync"/>, which performs the swap. If the
    /// invitation is never accepted, nothing changes on the team.
    /// </summary>
    public class TeamManagerHandoverService : ITeamManagerHandoverService
    {
        private static readonly string ActiveStatus = ((int)UserStatus.Active).ToString();

        private readonly ApplicationDbContext _context;
        private readonly ICompanyLicenseService _licenseService;
        private readonly ITeamOnboardingNotificationService _onboardingNotification;
        private readonly ILogger<TeamManagerHandoverService> _logger;

        public TeamManagerHandoverService(
            ApplicationDbContext context,
            ICompanyLicenseService licenseService,
            ITeamOnboardingNotificationService onboardingNotification,
            ILogger<TeamManagerHandoverService> logger)
        {
            _context = context;
            _licenseService = licenseService;
            _onboardingNotification = onboardingNotification;
            _logger = logger;
        }

        public async Task<bool> IsAwaitingActivationAsync(int userId, CancellationToken ct = default)
        {
            var user = await _context.Users.AsNoTracking()
                .Where(u => u.UserId == userId)
                .Select(u => new { u.IsVerified, u.Status })
                .FirstOrDefaultAsync(ct);

            return user != null && (!user.IsVerified || user.Status != ActiveStatus);
        }

        public async Task CancelPendingForUnitAsync(uint unitId, CancellationToken ct = default)
        {
            var pending = await _context.UserTeams
                .Where(ut => ut.UnitId == unitId && ut.IsPendingActivation)
                .ToListAsync(ct);

            foreach (var row in pending)
            {
                row.IsPendingActivation = false;
                row.IsUnAssigned = true;
            }
        }

        public async Task<int> FinalizePendingForUserAsync(int userId, CancellationToken ct = default)
        {
            var finalized = new List<(uint UnitId, int? ReplacedUserId)>();

            try
            {
                var pendingIds = await _context.UserTeams.AsNoTracking()
                    .Where(ut => ut.UserId == userId && ut.IsPendingActivation)
                    .Select(ut => ut.Id)
                    .ToListAsync(ct);

                if (pendingIds.Count == 0)
                    return 0;

                var userIsLive = await _context.Users.AsNoTracking()
                    .AnyAsync(u => u.UserId == userId && !u.IsDel, ct);

                foreach (var id in pendingIds)
                {
                    await using var transaction = await _context.Database.BeginTransactionAsync(ct);
                    try
                    {
                        // Re-read inside the transaction: an assignment made since
                        // may have cancelled it.
                        var row = await _context.UserTeams
                            .FirstOrDefaultAsync(ut => ut.Id == id && ut.IsPendingActivation, ct);
                        if (row == null)
                        {
                            await transaction.RollbackAsync(ct);
                            continue;
                        }

                        var unitIsLive = await _context.BusinessUnits.AsNoTracking()
                            .AnyAsync(bu => bu.Id == row.UnitId && bu.IsDel != true, ct);

                        if (!userIsLive || !unitIsLive)
                        {
                            // Nobody to hand over to, or no team left to take over.
                            row.IsPendingActivation = false;
                            await _context.SaveChangesAsync(ct);
                            await transaction.CommitAsync(ct);
                            continue;
                        }

                        var incumbents = await _context.UserTeams
                            .Where(ut => ut.UnitId == row.UnitId && !ut.IsUnAssigned && ut.Id != row.Id)
                            .ToListAsync(ct);

                        int? replacedUserId = null;
                        foreach (var incumbent in incumbents)
                        {
                            incumbent.IsUnAssigned = true;
                            if (incumbent.UserId != userId)
                                replacedUserId = incumbent.UserId;
                        }

                        // Flip first, then activate — the same order as every other
                        // assign path, so it never depends on EF's batching.
                        await _context.SaveChangesAsync(ct);

                        row.IsPendingActivation = false;
                        row.IsUnAssigned = false;
                        await _context.SaveChangesAsync(ct);

                        await transaction.CommitAsync(ct);
                        finalized.Add((row.UnitId, replacedUserId));
                    }
                    catch (Exception ex)
                    {
                        try { await transaction.RollbackAsync(ct); } catch { /* already gone */ }
                        _context.ChangeTracker.Clear();
                        _logger.LogError(ex,
                            "Pending team manager replacement {UserTeamId} for user {UserId} could not be finalized.", id, userId);
                    }
                }
            }
            catch (Exception ex)
            {
                // The invitation has already been accepted; this must not undo that.
                _logger.LogError(ex, "Pending team manager replacements for user {UserId} could not be loaded.", userId);
            }

            foreach (var (unitId, replacedUserId) in finalized)
                await AfterHandoverAsync(userId, unitId, replacedUserId, ct);

            return finalized.Count;
        }

        // The side effects an immediate replacement has, now that it has happened.
        private async Task AfterHandoverAsync(int userId, uint unitId, int? replacedUserId, CancellationToken ct)
        {
            try { await _licenseService.ReconcileGrantsForUnitAsync(unitId, userId); }
            catch (Exception ex) { _logger.LogWarning(ex, "Licence reconciliation failed for unit {UnitId}.", unitId); }

            // Idempotent per (team, manager): a new user's notification was
            // suppressed at Add User — the invitation covered it — and is not resent.
            try { await _onboardingNotification.NotifyManagerAssignedAsync(userId, unitId, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "Assignment notification failed for user {UserId}, unit {UnitId}.", userId, unitId); }

            if (replacedUserId.HasValue)
            {
                try { await _onboardingNotification.NotifyManagerUnassignedAsync(replacedUserId.Value, unitId, ct); }
                catch (Exception ex) { _logger.LogWarning(ex, "Unassigned notification failed for user {UserId}.", replacedUserId); }
            }
        }
    }
}
