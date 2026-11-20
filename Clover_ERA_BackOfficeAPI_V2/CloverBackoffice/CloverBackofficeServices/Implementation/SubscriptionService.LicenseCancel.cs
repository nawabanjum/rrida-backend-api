using System.Globalization;
using CloverBackoffice.Components.Enums;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Implementation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CloverBackoffice.Services.Implementation
{
    // Per-licence end-of-period cancellation (POST /api/license-assignments/cancel).
    //
    // A licence belongs to a Team Manager through the teams they manage: the
    // licence's active assignment sits on one of their teams, or one of their
    // teams rides on it through a complimentary grant.
    //
    // Cancelling never revokes anything now — the licence stays ACTIVE until
    // its ExpiryDate (= the paid period end) and simply does not renew:
    //   * other renewing seats share its Stripe subscription → the Stripe
    //     quantity drops by one with no proration (next renewal bills one seat
    //     less) and the licence is flagged CancelAtPeriodEnd, which keeps
    //     ExtendLicensesToAsync from pushing its expiry forward on renewal;
    //   * it is the last renewing seat → the whole subscription is set to
    //     cancel_at_period_end (Stripe stops billing it after the period).
    public partial class SubscriptionService
    {
        // First key of pg_advisory_xact_lock(int, int); the second is the
        // subscription id. Two cancels on one subscription queue here, so they
        // can never both read the same Stripe quantity and drop only one seat.
        private const int LicenseCancelLockNamespace = 16023;

        public async Task<CancelLicenseResultDto> CancelLicenseAsync(int userId, User actor, int? profileId = null)
        {
            profileId = await ResolveLicenceHolderProfileAsync(userId, actor, adminsOnly: true, profileId);

            var candidates = await GetUserLicenceCandidatesAsync(userId, profileId);
            if (candidates.Count == 0)
                throw new InvalidOperationException(ResponseMessages.Errors.NoActiveLicenseForUser);

            // A renewing paid licence is the one to cancel. When none is left,
            // the cancellation was already requested: answer with that licence
            // again instead of doing anything (idempotent).
            var renewing = candidates.FirstOrDefault(c => c.IsRecurring && !c.IsCancelled);
            if (renewing == null)
            {
                var cancelled = candidates.FirstOrDefault(c => c.IsCancelled);
                if (cancelled != null)
                    return BuildCancelLicenseResult(cancelled.License, cancelled.Subscription, cancelled.AssignmentId, userId);

                throw new InvalidOperationException(ResponseMessages.Errors.LicenseHasNoRecurringBilling);
            }

            await CancelLicenceAtPeriodEndAsync(renewing.License.Id, renewing.Subscription.Id, actor.UserId);

            var license = await _db.CompanyLicenses.AsNoTracking().FirstAsync(l => l.Id == renewing.License.Id);
            var sub = await _db.CompanySubscriptions.AsNoTracking().FirstAsync(s => s.Id == renewing.Subscription.Id);
            return BuildCancelLicenseResult(license, sub, renewing.AssignmentId, userId);
        }

        // Validates the licence holder and the caller; returns the holder's
        // profile. Throws InvalidOperationException for an unknown user, a user
        // outside the expected profile (when one is sent) or a user who is not a
        // Team Manager; UnauthorizedAccessException when the caller may not act:
        //   * adminsOnly (cancel / reactivate): RRIDA Admins any profile,
        //     Profile Admins their own profile only, every other role never;
        //   * otherwise (plan change, unchanged): RRIDA Admins any profile,
        //     anyone else their own profile.
        private async Task<int?> ResolveLicenceHolderProfileAsync(
            int userId, User actor, bool adminsOnly = false, int? expectedProfileId = null)
        {
            var teamManager = (int)UserGroups.TeamManager;
            var target = await _db.Users.AsNoTracking()
                .Where(u => u.UserId == userId && !u.IsDel)
                .Select(u => new
                {
                    u.UserId,
                    u.ProfileId,
                    // Same Team Manager rule as Add User: UserRoles, legacy
                    // UserGroupId only for a user with no UserRoles rows.
                    IsTeamManager = u.Roles.Any(r => r.UserGroupId == teamManager)
                                    || (!u.Roles.Any() && u.UserGroupId == teamManager)
                })
                .FirstOrDefaultAsync()
                ?? throw new InvalidOperationException("User not found.");

            if (adminsOnly)
            {
                // Same role source as the rest of the API: UserRoles, legacy
                // UserGroupId only for a user with no UserRoles rows.
                var actorRoles = await _db.UserRoles.AsNoTracking()
                    .Where(r => r.UserId == actor.UserId)
                    .Select(r => r.UserGroupId)
                    .ToListAsync();
                if (actorRoles.Count == 0)
                    actorRoles.Add(actor.UserGroupId);

                bool allowed = actorRoles.Contains((int)UserGroups.RridaAdmin)
                    || (actorRoles.Contains((int)UserGroups.ProfileAdmin)
                        && actor.ProfileId.HasValue && actor.ProfileId == target.ProfileId);
                if (!allowed)
                    throw new UnauthorizedAccessException(ResponseMessages.Errors.LicenseBillingNotAllowed);
            }
            else
            {
                bool allowed = actor.UserGroupId == (int)UserGroups.RridaAdmin
                    || (actor.ProfileId.HasValue && actor.ProfileId == target.ProfileId);
                if (!allowed)
                    throw new UnauthorizedAccessException(ResponseMessages.Errors.LicenseModuleAccessDenied);
            }

            // The profile picked on screen must be the user's own profile.
            if (expectedProfileId is > 0 && expectedProfileId != target.ProfileId)
                throw new InvalidOperationException(ResponseMessages.Errors.LicenseUserNotInProfile);

            // Only a Team Manager holds a licence (1 licence = 1 Team Manager);
            // a team member must never reach their manager's licence.
            if (!target.IsTeamManager)
                throw new InvalidOperationException(ResponseMessages.Errors.NoActiveLicenseForUser);

            return target.ProfileId;
        }

        private sealed record LicenceCandidate(
            CompanyLicense License, CompanySubscription Subscription, int AssignmentId)
        {
            // Billed by a Stripe subscription, so there is a renewal to stop.
            public bool IsRecurring => License.LicenseType != "Trial"
                                       && !string.IsNullOrEmpty(Subscription.StripeSubscriptionId);

            // Already scheduled to cancel, or the Stripe subscription has already
            // ended — there is no renewal left to stop.
            public bool IsCancelled => License.CancelAtPeriodEnd
                                       || Subscription.CancelAtPeriodEnd
                                       || Subscription.Status == SubscriptionStatus.Canceled
                                       || Subscription.Status == SubscriptionStatus.Expired;
        }

        // Live licences the user holds (CompanyLicenses.AssignedUserId, team or
        // not) or covering the user's teams, oldest assignment first.
        private async Task<List<LicenceCandidate>> GetUserLicenceCandidatesAsync(int userId, int? profileId)
        {
            var teamIds = await _db.ManagerTeamIds(userId).ToListAsync();
            var heldIds = await _db.CompanyLicenses.AsNoTracking()
                .Where(l => l.AssignedUserId == userId)
                .Select(l => l.Id)
                .ToListAsync();
            if (teamIds.Count == 0 && heldIds.Count == 0) return new List<LicenceCandidate>();

            var assignedIds = await _db.AssignedCompanyLicenses.AsNoTracking()
                .Where(a => teamIds.Contains(a.BusinessUnitId) && a.IsActive && !a.IsDel)
                .Select(a => a.CompanyLicenseId)
                .ToListAsync();
            var grantedIds = await _db.ComplimentaryLicenseGrants.AsNoTracking()
                .Where(g => teamIds.Contains(g.ComplimentaryBusinessUnitId) && g.IsActive && !g.IsDel)
                .Select(g => g.CompanyLicenseId)
                .ToListAsync();
            var licenseIds = heldIds.Concat(assignedIds).Concat(grantedIds).Distinct().ToList();

            var licenses = await _db.CompanyLicenses.AsNoTracking()
                .Include(l => l.CompanySubscription)
                .Where(l => licenseIds.Contains(l.Id)
                         && !l.IsDel
                         && l.CompanySubscription != null
                         && !l.CompanySubscription.IsDel
                         && (profileId == null || l.ProfileId == (uint)profileId.Value))
                .ToListAsync();

            // The licence's active purchased assignment (its licenseAssignmentId).
            var liveIds = licenses.Select(l => l.Id).ToList();
            var assignments = await _db.AssignedCompanyLicenses.AsNoTracking()
                .Where(a => liveIds.Contains(a.CompanyLicenseId) && a.IsActive && !a.IsDel)
                .ToListAsync();

            return licenses
                .Select(l =>
                {
                    var a = assignments.FirstOrDefault(x => x.CompanyLicenseId == l.Id);
                    return new { Candidate = new LicenceCandidate(l, l.CompanySubscription!, a?.Id ?? 0), AssignedAt = a?.AssignedAt };
                })
                .OrderBy(x => x.AssignedAt ?? DateTime.MaxValue)
                .ThenBy(x => x.Candidate.License.Id)
                .Select(x => x.Candidate)
                .ToList();
        }

        /// <summary>
        /// A Light / Pro switch: the licence the manager left is cancelled at the end of the
        /// period already paid (no refund, no further renewal), exactly as POST
        /// license-assignments/cancel would. False when there is nothing to cancel: a trial,
        /// no Stripe subscription, or already cancelled / ended.
        /// </summary>
        public async Task<bool> CancelReplacedLicenceAsync(int licenseId, int actorUserId)
        {
            var licence = await _db.CompanyLicenses.AsNoTracking()
                .Include(l => l.CompanySubscription)
                .FirstOrDefaultAsync(l => l.Id == licenseId && !l.IsDel);
            var sub = licence?.CompanySubscription;
            if (licence == null || sub == null) return false;

            var candidate = new LicenceCandidate(licence, sub, 0);
            if (!candidate.IsRecurring || candidate.IsCancelled) return false;

            await CancelLicenceAtPeriodEndAsync(licence.Id, sub.Id, actorUserId);
            return true;
        }

        private async Task CancelLicenceAtPeriodEndAsync(int licenseId, int subscriptionId, int actorUserId)
        {
            var now = DateTime.UtcNow;

            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                if (_db.Database.ProviderName?.Contains("Npgsql", StringComparison.Ordinal) == true)
                    await _db.Database.ExecuteSqlInterpolatedAsync(
                        $"SELECT pg_advisory_xact_lock({LicenseCancelLockNamespace}, {subscriptionId})");

                // Re-read under the lock: a concurrent request may have already
                // cancelled this licence or the whole subscription.
                var license = await _db.CompanyLicenses.FirstAsync(l => l.Id == licenseId);
                var sub = await _db.CompanySubscriptions.FirstAsync(s => s.Id == subscriptionId);
                if (license.CancelAtPeriodEnd || sub.CancelAtPeriodEnd)
                {
                    await tx.RollbackAsync();
                    return;
                }

                var otherRenewingSeats = await _db.CompanyLicenses.CountAsync(l =>
                    l.CompanySubscriptionId == subscriptionId
                    && l.Id != licenseId
                    && !l.IsDel
                    && l.LicenseType != "Trial"
                    && !l.CancelAtPeriodEnd);

                if (otherRenewingSeats == 0)
                {
                    // Last renewing seat → stop the whole subscription at period end.
                    sub.CancelAtPeriodEnd = true;
                    sub.CanceledAt = now;
                    await _subscriptionRepo.UpdateAsync(sub);

                    await _stripeService.CancelSubscriptionAsync(sub.StripeSubscriptionId!, atPeriodEnd: true);
                }
                else
                {
                    var quantity = await _stripeService.GetSubscriptionQuantityAsync(sub.StripeSubscriptionId!);
                    if (quantity <= 1)
                        throw new InvalidOperationException(ResponseMessages.Errors.LicenseInBundledPlan);

                    license.CancelAtPeriodEnd = true;
                    license.CanceledAt = now;
                    license.UpdatedBy = actorUserId;
                    license.UpdatedAt = now;
                    await AddLicenseAuditAsync(license, now);

                    // One seat less from the next renewal on.
                    sub.TeamCount = Math.Max(0, sub.TeamCount - 1);
                    sub.MaxTeamsAllowed = Math.Max(0, sub.MaxTeamsAllowed - 1);
                    sub.MaxPeopleAllowed = sub.MaxTeamsAllowed * MaxPeoplePerTeam;
                    await _subscriptionRepo.UpdateAsync(sub);

                    // Stripe last: when it fails, the rollback leaves nothing behind.
                    await _stripeService.SetSubscriptionQuantityAsync(sub.StripeSubscriptionId!, quantity - 1);
                }

                await tx.CommitAsync();

                _logger.LogInformation(
                    "[CancelLicense] LicenseId={LicenseId} SubId={SubId} StripeSubId={StripeSubId} WholeSubscription={Whole} ActorUserId={Actor}; access ends {End:O}",
                    licenseId, subscriptionId, sub.StripeSubscriptionId, otherRenewingSeats == 0, actorUserId, license.ExpiryDate ?? sub.CurrentPeriodEnd);
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        private async Task AddLicenseAuditAsync(CompanyLicense l, DateTime now)
        {
            var version = await _db.CompanyLicenseAuditLogs
                .Where(a => a.Id == l.Id)
                .MaxAsync(a => (int?)a.VersionNumber) ?? 0;

            _db.CompanyLicenseAuditLogs.Add(new CompanyLicenseAuditLog
            {
                Id = l.Id,
                ProfileId = l.ProfileId,
                LicenseKey = l.LicenseKey,
                LicenseType = l.LicenseType,
                CompanySubscriptionId = l.CompanySubscriptionId,
                PlanId = l.PlanId,
                ExpiryDate = l.ExpiryDate,
                Notes = l.Notes,
                CreatedBy = l.CreatedBy,
                UpdatedBy = l.UpdatedBy,
                CreatedAt = l.CreatedAt,
                UpdatedAt = l.UpdatedAt,
                IsDel = l.IsDel,
                LoggedAt = now,
                VersionNumber = version + 1,
                OperationType = "UPDATE"
            });
            await _db.SaveChangesAsync();
        }

        private static CancelLicenseResultDto BuildCancelLicenseResult(
            CompanyLicense license, CompanySubscription sub, int assignmentId, int userId)
        {
            var now = DateTime.UtcNow;
            var periodEnd = license.ExpiryDate ?? (sub.CurrentPeriodEnd == default ? (DateTime?)null : sub.CurrentPeriodEnd);
            var periodEndUtc = periodEnd.HasValue ? DateTime.SpecifyKind(periodEnd.Value, DateTimeKind.Utc) : (DateTime?)null;
            bool live = !license.IsDel && (!periodEndUtc.HasValue || periodEndUtc.Value > now);
            bool cancelled = license.CancelAtPeriodEnd || sub.CancelAtPeriodEnd;

            return new CancelLicenseResultDto
            {
                LicenseAssignmentId = assignmentId,
                CompanyLicenseId = license.Id,
                UserId = userId,
                Status = live ? "ACTIVE" : "EXPIRED",
                CancelAtPeriodEnd = cancelled,
                PeriodEndAt = periodEndUtc,
                RenewsLabel = BuildRenewsLabel(periodEndUtc, cancelled, live),
                CanCancel = live && !cancelled,
                CanReactivate = live && cancelled
            };
        }

        // "Renews 9 October 2026" / "Cancelled · ends 9 October 2026" /
        // "Cancelled · ended 9 October 2026".
        private static string BuildRenewsLabel(DateTime? periodEnd, bool cancelled, bool live)
        {
            var prefix = "Renews";
            if (cancelled)
                prefix = live ? "Cancelled · ends" : "Cancelled · ended";

            if (!periodEnd.HasValue)
                return cancelled ? "Cancelled" : prefix;

            return $"{prefix} {periodEnd.Value.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)}";
        }
    }
}
