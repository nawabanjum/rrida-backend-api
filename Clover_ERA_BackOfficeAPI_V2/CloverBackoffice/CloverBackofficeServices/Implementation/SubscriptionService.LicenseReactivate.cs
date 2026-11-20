using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CloverBackoffice.Services.Implementation
{
    // Per-licence reactivation (POST /api/subscription/reactivate) — the exact
    // reverse of CancelLicenseAsync, allowed while the licence's paid period is
    // still running. Nothing is charged now: the current period is already paid.
    //   * the licence was cancelled on its own (CompanyLicenses.CancelAtPeriodEnd)
    //     → its seat goes back on the Stripe subscription (quantity +1, no
    //     proration), so it is billed again from the next renewal;
    //   * the whole subscription is set to cancel (the licence was its last
    //     renewing seat, or it was cancelled in Stripe) → Stripe renews again
    //     with ONE seat — this licence. Any other seat still renewing when the
    //     subscription was cancelled keeps its cancellation as a licence flag,
    //     so reactivating one Team Manager never restarts billing for another.
    public partial class SubscriptionService
    {
        public async Task<CancelLicenseResultDto> ReactivateLicenseAsync(int userId, User actor, int? profileId = null)
        {
            profileId = await ResolveLicenceHolderProfileAsync(userId, actor, adminsOnly: true, profileId);

            var candidates = await GetUserLicenceCandidatesAsync(userId, profileId);
            if (candidates.Count == 0)
                throw new InvalidOperationException(ResponseMessages.Errors.NoActiveLicenseForUser);

            var now = DateTime.UtcNow;
            var target = candidates.FirstOrDefault(c => c.IsRecurring && c.IsCancelled && IsStillReactivatable(c, now));
            if (target == null)
            {
                // Already renewing: answer with that licence again (idempotent).
                var renewing = candidates.FirstOrDefault(c => c.IsRecurring && !c.IsCancelled);
                if (renewing != null)
                    return BuildCancelLicenseResult(renewing.License, renewing.Subscription, renewing.AssignmentId, userId);

                throw new InvalidOperationException(candidates.Any(c => c.IsRecurring)
                    ? ResponseMessages.Errors.LicenseAlreadyEnded
                    : ResponseMessages.Errors.LicenseHasNoRecurringBillingToReactivate);
            }

            await ReactivateLicenceAsync(target.License.Id, target.Subscription.Id, actor.UserId);

            var license = await _db.CompanyLicenses.AsNoTracking().FirstAsync(l => l.Id == target.License.Id);
            var sub = await _db.CompanySubscriptions.AsNoTracking().FirstAsync(s => s.Id == target.Subscription.Id);
            return BuildCancelLicenseResult(license, sub, target.AssignmentId, userId);
        }

        // A cancellation can only be undone while the Stripe subscription still
        // exists and the licence's paid period has not ended. After that the
        // seat is gone and has to be bought again through checkout.
        private static bool IsStillReactivatable(LicenceCandidate c, DateTime now)
        {
            if (!IsSubscriptionStillRunning(c.Subscription, now))
                return false;
            var end = LicenceEnd(c.License, c.Subscription);
            return !end.HasValue || end.Value > now;
        }

        private static bool IsSubscriptionStillRunning(CompanySubscription sub, DateTime now)
            => !string.IsNullOrEmpty(sub.StripeSubscriptionId)
               && sub.Status != SubscriptionStatus.Expired
               && sub.Status != SubscriptionStatus.Canceled
               && (sub.CurrentPeriodEnd == default
                   || DateTime.SpecifyKind(sub.CurrentPeriodEnd, DateTimeKind.Utc) > now);

        private static DateTime? LicenceEnd(CompanyLicense license, CompanySubscription sub)
        {
            var end = license.ExpiryDate ?? (sub.CurrentPeriodEnd == default ? (DateTime?)null : sub.CurrentPeriodEnd);
            return end.HasValue ? DateTime.SpecifyKind(end.Value, DateTimeKind.Utc) : null;
        }

        private async Task ReactivateLicenceAsync(int licenseId, int subscriptionId, int actorUserId)
        {
            var now = DateTime.UtcNow;

            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                // Same lock as cancel: a cancel and a reactivate on one
                // subscription can never interleave their quantity changes.
                if (_db.Database.ProviderName?.Contains("Npgsql", StringComparison.Ordinal) == true)
                    await _db.Database.ExecuteSqlInterpolatedAsync(
                        $"SELECT pg_advisory_xact_lock({LicenseCancelLockNamespace}, {subscriptionId})");

                // Re-read under the lock: a concurrent request may have already
                // reactivated it.
                var license = await _db.CompanyLicenses.FirstAsync(l => l.Id == licenseId);
                var sub = await _db.CompanySubscriptions.FirstAsync(s => s.Id == subscriptionId);
                if (!license.CancelAtPeriodEnd && !sub.CancelAtPeriodEnd)
                {
                    await tx.RollbackAsync();
                    return;
                }

                if (!IsSubscriptionStillRunning(sub, now))
                    throw new InvalidOperationException(ResponseMessages.Errors.LicenseAlreadyEnded);

                bool wholeSubscription = sub.CancelAtPeriodEnd;
                if (wholeSubscription)
                {
                    // Seats still renewing when the subscription was cancelled
                    // stay cancelled — now as their own licence flag.
                    var others = await _db.CompanyLicenses
                        .Where(l => l.CompanySubscriptionId == subscriptionId
                                    && l.Id != licenseId
                                    && !l.IsDel
                                    && l.LicenseType != "Trial"
                                    && !l.CancelAtPeriodEnd)
                        .ToListAsync();
                    foreach (var other in others)
                    {
                        other.CancelAtPeriodEnd = true;
                        other.CanceledAt = sub.CanceledAt ?? now;
                        other.UpdatedBy = actorUserId;
                        other.UpdatedAt = now;
                        await AddLicenseAuditAsync(other, now);
                    }

                    ClearLicenceCancellation(license, actorUserId, now);
                    await AddLicenseAuditAsync(license, now);

                    // Only this licence renews from now on.
                    sub.CancelAtPeriodEnd = false;
                    sub.CanceledAt = null;
                    sub.TeamCount = 1;
                    sub.MaxTeamsAllowed = 1;
                    sub.MaxPeopleAllowed = MaxPeoplePerTeam;
                    sub.UpdatedAt = now;
                    await _subscriptionRepo.UpdateAsync(sub);

                    // Stripe last, in ONE update (un-cancel + quantity), so a
                    // failure leaves neither side half-changed.
                    await _stripeService.ReactivateSubscriptionAsync(sub.StripeSubscriptionId!, quantity: 1);
                }
                else
                {
                    var quantity = await _stripeService.GetSubscriptionQuantityAsync(sub.StripeSubscriptionId!);

                    ClearLicenceCancellation(license, actorUserId, now);
                    await AddLicenseAuditAsync(license, now);

                    // One seat more from the next renewal on.
                    sub.TeamCount += 1;
                    sub.MaxTeamsAllowed += 1;
                    sub.MaxPeopleAllowed = sub.MaxTeamsAllowed * MaxPeoplePerTeam;
                    sub.UpdatedAt = now;
                    await _subscriptionRepo.UpdateAsync(sub);

                    // No proration: the seat's current period is already paid.
                    await _stripeService.SetSubscriptionQuantityAsync(sub.StripeSubscriptionId!, quantity + 1);
                }

                await tx.CommitAsync();

                _logger.LogInformation(
                    "[ReactivateLicense] LicenseId={LicenseId} SubId={SubId} StripeSubId={StripeSubId} WholeSubscription={Whole} ActorUserId={Actor}; renews {End:O}",
                    licenseId, subscriptionId, sub.StripeSubscriptionId, wholeSubscription, actorUserId, license.ExpiryDate ?? sub.CurrentPeriodEnd);
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        private static void ClearLicenceCancellation(CompanyLicense license, int actorUserId, DateTime now)
        {
            license.CancelAtPeriodEnd = false;
            license.CanceledAt = null;
            license.UpdatedBy = actorUserId;
            license.UpdatedAt = now;
        }
    }
}
