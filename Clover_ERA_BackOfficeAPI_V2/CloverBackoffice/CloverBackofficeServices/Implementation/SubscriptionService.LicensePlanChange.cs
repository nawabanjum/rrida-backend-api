using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Stripe;
using Stripe.Checkout;

namespace CloverBackoffice.Services.Implementation
{
    // Per-licence billing-cycle change through Stripe Checkout
    // (POST /api/license/change).
    //
    // A Stripe subscription bills every seat on one interval, so a licence that
    // changes cycle has to leave its subscription. The flow:
    //   1. StartLicensePlanChangeAsync validates the licence and opens a checkout
    //      for a NEW one-seat subscription on the requested cycle. Nothing in our
    //      data or in the existing Stripe subscription changes here — a cancelled
    //      or abandoned checkout leaves everything as it was.
    //   2. checkout.session.completed (or, as a fallback, the new subscription's
    //      customer.subscription.updated) runs ApplyLicencePlanChangeAsync: a new
    //      CompanySubscription row for the new Stripe subscription, the licence
    //      moved onto it (same row, so its team assignment and complimentary
    //      grants stay), and the seat taken out of the old subscription with a
    //      credit for its unused time — quantity −1 with proration, or, when it
    //      was the last renewing seat, the old subscription ended now with
    //      proration (credit to the customer's balance).
    //
    // The checkout carries op=licencePlanChange + the licence context on both the
    // session and the Stripe subscription metadata, so either event can apply it.
    public partial class SubscriptionService
    {
        private const string LicencePlanChangeOp = "licencePlanChange";

        // First key of pg_advisory_xact_lock(int, int); the second is the licence
        // id. Starting and applying plan changes of one licence queue here, so
        // two clicks never open two checkouts and two completions never move the
        // licence twice.
        private const int LicensePlanChangeLockNamespace = 16024;

        public async Task<LicensePlanChangeResultDto> StartLicensePlanChangeAsync(int userId, BillingCycle billingCycle, User actor)
        {
            if (!Enum.IsDefined(typeof(BillingCycle), billingCycle))
                throw new InvalidOperationException(ResponseMessages.Errors.InvalidLicenseBillingCycle);

            var profileId = await ResolveLicenceHolderProfileAsync(userId, actor);

            var candidates = await GetUserLicenceCandidatesAsync(userId, profileId);
            if (candidates.Count == 0)
                throw new InvalidOperationException(ResponseMessages.Errors.NoActiveLicenseForUser);

            var licence = candidates.FirstOrDefault(c => c.IsRecurring && !c.IsCancelled);
            if (licence == null)
                throw new InvalidOperationException(candidates.Any(c => c.IsRecurring && c.IsCancelled)
                    ? ResponseMessages.Errors.LicenseCancelledNoPlanChange
                    : ResponseMessages.Errors.LicenseHasNoRecurringBilling);

            var sub = licence.Subscription;
            if (sub.BillingCycle == billingCycle)
                throw new InvalidOperationException(string.Format(
                    ResponseMessages.Errors.LicenseAlreadyOnBillingCycle, billingCycle.ToString().ToLowerInvariant()));

            if (string.IsNullOrEmpty(sub.StripeCustomerId))
                throw new InvalidOperationException(ResponseMessages.Errors.NoStripeCustomer);

            // The seat must be separable from its subscription: when other seats
            // renew on it, its Stripe quantity has to be able to drop by one.
            var otherRenewingSeats = await CountOtherRenewingSeatsAsync(sub.Id, licence.License.Id);
            if (otherRenewingSeats > 0
                && await _stripeService.GetSubscriptionQuantityAsync(sub.StripeSubscriptionId!) <= 1)
                throw new InvalidOperationException(ResponseMessages.Errors.LicensePlanChangeBundled);

            var licenseId = licence.License.Id;
            var cycleValue = ((int)billingCycle).ToString();

            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                await LockLicencePlanChangeAsync(licenseId);

                // Duplicate / conflict guard, read from Stripe itself: an open
                // checkout for the same change is handed out again; one for the
                // other cycle is expired so the two can never both complete.
                var open = (await _stripeService.ListOpenCheckoutSessionsAsync(sub.StripeCustomerId!))
                    .Where(s => IsLicencePlanChange(s.Metadata)
                             && s.Metadata.TryGetValue("companyLicenseId", out var lid)
                             && lid == licenseId.ToString())
                    .ToList();

                var same = open.FirstOrDefault(s =>
                    s.Metadata.TryGetValue("billingCycle", out var c) && c == cycleValue
                    && !string.IsNullOrEmpty(s.Url));
                if (same != null)
                {
                    await tx.CommitAsync();
                    return new LicensePlanChangeResultDto { CheckoutUrl = same.Url!, RequiresCheckout = true };
                }

                foreach (var stale in open)
                {
                    try { await _stripeService.ExpireCheckoutSessionAsync(stale.Id); }
                    catch (StripeException ex)
                    {
                        // No longer open: it is being completed right now.
                        _logger.LogWarning(ex, "[LicensePlanChange] Could not expire session {SessionId} for LicenseId={LicenseId}", stale.Id, licenseId);
                        throw new InvalidOperationException(ResponseMessages.Errors.LicensePlanChangeInProgress);
                    }
                }

                // Same price and standing lifetime discount as any other purchase. A licence on a
                // catalogue plan (Light, Pro) is priced by its plan for one manager on the new
                // cycle, and keeps its plan on the new subscription; a pre-plan licence keeps the
                // legacy price.
                var discount = await GetCompanyLifetimeDiscountAsync(sub.ProfileId);
                // The licence's own plan and version (pre-plan: Pro version 1), so the change
                // keeps the terms it was bought on.
                var planCheckout = await QuotePlanCheckoutAsync(licence.License.ServicePlanId, null, 1, billingCycle,
                    licence.License.ServicePlanVersionId, keepTerms: true);
                var changeMetadata = new Dictionary<string, string>
                {
                    { "op", LicencePlanChangeOp },
                    { "companyId", sub.ProfileId.ToString() },
                    { "billingCycle", cycleValue },
                    { "companyLicenseId", licenseId.ToString() },
                    { "fromSubscriptionId", sub.Id.ToString() },
                    { "userId", userId.ToString() },
                    { "actorUserId", actor.UserId.ToString() }
                };
                foreach (var kv in planCheckout?.Metadata ?? new Dictionary<string, string>())
                    changeMetadata[kv.Key] = kv.Value;

                var url = await _stripeService.CreateDynamicCheckoutSessionAsync(
                    sub.StripeCustomerId!, teamCount: 1, _stripeSettings.PricePerLicenseMonthly, billingCycle,
                    trialDays: 0,
                    successUrl: _stripeSettings.SuccessUrl,
                    cancelUrl: _stripeSettings.CancelUrl,
                    sub.ProfileId,
                    discountPercent: discount,
                    isLifetimeDiscount: discount.HasValue,
                    // Also stamped on the Stripe subscription, so its own events
                    // carry everything ApplyLicencePlanChangeAsync needs.
                    extraMetadata: changeMetadata,
                    planPrice: planCheckout?.Price);

                await tx.CommitAsync();

                _logger.LogInformation(
                    "[LicensePlanChange] Checkout opened. LicenseId={LicenseId} UserId={UserId} FromSubId={SubId} {From}→{To} ActorUserId={Actor}",
                    licenseId, userId, sub.Id, sub.BillingCycle, billingCycle, actor.UserId);

                return new LicensePlanChangeResultDto { CheckoutUrl = url, RequiresCheckout = true };
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        private static bool IsLicencePlanChange(IDictionary<string, string>? metadata)
            => metadata != null
               && metadata.TryGetValue("op", out var op)
               && string.Equals(op, LicencePlanChangeOp, StringComparison.OrdinalIgnoreCase);

        private async Task LockLicencePlanChangeAsync(int licenseId)
        {
            if (_db.Database.ProviderName?.Contains("Npgsql", StringComparison.Ordinal) == true)
                await _db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock({LicensePlanChangeLockNamespace}, {licenseId})");
        }

        // Paid seats on the subscription, other than licenseId, that still renew.
        private Task<int> CountOtherRenewingSeatsAsync(int subscriptionId, int licenseId)
            => _db.CompanyLicenses.CountAsync(l =>
                l.CompanySubscriptionId == subscriptionId
                && l.Id != licenseId
                && !l.IsDel
                && l.LicenseType != "Trial"
                && !l.CancelAtPeriodEnd);

        // checkout.session.completed for op=licencePlanChange.
        private async Task HandleLicencePlanChangePaidAsync(Session session)
        {
            if (string.IsNullOrEmpty(session.SubscriptionId)
                || !(string.Equals(session.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(session.PaymentStatus, "no_payment_required", StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogWarning(
                    "[LicensePlanChange] Session {SessionId} completed without payment (status {Status}); nothing applied.",
                    session.Id, session.PaymentStatus);
                return;
            }

            var newSub = await ApplyLicencePlanChangeAsync(session.Metadata, session.SubscriptionId, session.CustomerId);
            if (newSub == null) return;

            var card = await _stripeService.GetCardUsedAsync(
                session.PaymentIntentId, session.InvoiceId, session.SubscriptionId, session.CustomerId);
            var (subtotal, discount) = ReadSessionDiscount(session);
            var discountCode = discount.HasValue
                ? await ResolveDiscountCodeAsync(session, ParsePromoCodeIdMetadata(session.Metadata))
                : null;

            await UpsertSubscriptionPaymentAsync(
                newSub.ProfileId, newSub.Id, session.InvoiceId, session.PaymentIntentId,
                session.AmountTotal.HasValue ? session.AmountTotal.Value / 100m : 0m,
                (session.Currency ?? "usd").ToLowerInvariant(), card,
                $"License plan change - {newSub.BillingCycle}",
                subtotal, discount, discountCode);
        }

        // Applies a paid plan change. Idempotent on the new Stripe subscription
        // id: a re-delivered event returns the row already created. Returns the
        // new CompanySubscription, or null when nothing was applied.
        private async Task<CompanySubscription?> ApplyLicencePlanChangeAsync(
            IDictionary<string, string>? metadata, string stripeSubscriptionId, string? customerId)
        {
            if (metadata == null
                || !metadata.TryGetValue("companyLicenseId", out var lidStr) || !int.TryParse(lidStr, out var licenseId)
                || !metadata.TryGetValue("fromSubscriptionId", out var sidStr) || !int.TryParse(sidStr, out var fromSubId)
                || !metadata.TryGetValue("companyId", out var pidStr) || !uint.TryParse(pidStr, out var profileId))
            {
                _logger.LogWarning("[LicensePlanChange] Missing licence context on StripeSub {StripeSubId}; nothing applied.", stripeSubscriptionId);
                return null;
            }

            var cycle = ParseBillingCycleMetadata(metadata)
                        ?? await _stripeService.GetSubscriptionBillingCycleAsync(stripeSubscriptionId)
                        ?? BillingCycle.Monthly;
            int? actorUserId = metadata.TryGetValue("actorUserId", out var aStr) && int.TryParse(aStr, out var a) ? a : null;

            var already = await _subscriptionRepo.GetByStripeSubscriptionIdAsync(stripeSubscriptionId);
            if (already != null) return already;

            // Network lookups before the transaction.
            string? productId = null;
            try { productId = await _stripeService.GetProductIdForSubscriptionAsync(stripeSubscriptionId); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[LicensePlanChange] GetProductIdForSubscriptionAsync failed for {StripeSubId}; continuing.", stripeSubscriptionId);
            }
            var lifetime = await GetCompanyLifetimeDiscountAsync(profileId);
            var plan = await _db.Plans
                .Where(p => p.IsActive && p.MaxTeams == 1)
                .OrderBy(p => p.SortOrder)
                .FirstOrDefaultAsync();

            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                await LockLicencePlanChangeAsync(licenseId);

                // Re-check under the lock — a concurrent delivery may have won.
                already = await _subscriptionRepo.GetByStripeSubscriptionIdAsync(stripeSubscriptionId);
                if (already != null)
                {
                    await tx.CommitAsync();
                    return already;
                }

                var licence = await _db.CompanyLicenses
                    .FirstOrDefaultAsync(l => l.Id == licenseId && l.ProfileId == profileId);
                var oldSub = await _db.CompanySubscriptions
                    .FirstOrDefaultAsync(s => s.Id == fromSubId && s.ProfileId == profileId);

                // The licence left the old subscription since checkout opened
                // (another plan change won). Don't keep a second subscription for
                // the same seat: end it now and credit the payment back.
                if (licence == null || oldSub == null || licence.CompanySubscriptionId != oldSub.Id)
                {
                    await tx.RollbackAsync();
                    await _stripeService.CancelSubscriptionNowAsync(stripeSubscriptionId, prorate: true);
                    _logger.LogWarning(
                        "[LicensePlanChange] LicenseId={LicenseId} no longer on SubId={SubId}; ended duplicate StripeSub {StripeSubId} with credit.",
                        licenseId, fromSubId, stripeSubscriptionId);
                    return null;
                }

                var now = DateTime.UtcNow;
                var seatWasRenewing = !licence.CancelAtPeriodEnd
                    && !oldSub.CancelAtPeriodEnd
                    && oldSub.Status != SubscriptionStatus.Canceled
                    && oldSub.Status != SubscriptionStatus.Expired
                    && !string.IsNullOrEmpty(oldSub.StripeSubscriptionId);
                var otherRenewingSeats = await CountOtherRenewingSeatsAsync(oldSub.Id, licence.Id);

                var newSub = new CompanySubscription
                {
                    ProfileId = profileId,
                    PlanId = plan?.Id,
                    // The licence's catalogue plan and its price, when the change was priced by plan.
                    ServicePlanId = await ExistingServicePlanIdAsync(ParseServicePlanIdMetadata(metadata)),
                    ServicePlanVersionId = await ExistingServicePlanVersionIdAsync(metadata, ParseServicePlanIdMetadata(metadata)),
                    ServicePlanUnitPrice = ParseServicePlanUnitPriceMetadata(metadata),
                    TeamCount = 1,
                    MaxTeamsAllowed = 1,
                    MaxPeopleAllowed = MaxPeoplePerTeam,
                    BillingCycle = cycle,
                    Status = SubscriptionStatus.Active,
                    StripeCustomerId = customerId,
                    StripeSubscriptionId = stripeSubscriptionId,
                    StripeProductId = productId,
                    IsTrialUsed = true,
                    LifetimeDiscountPercent = lifetime,
                    CurrentPeriodStart = now,
                    CurrentPeriodEnd = cycle == BillingCycle.Annual ? now.AddYears(1) : now.AddMonths(1)
                };
                await _subscriptionRepo.CreateAsync(newSub);

                // Move the licence. Same row → its team assignment and
                // complimentary grants carry over untouched. CreatedAt is this
                // codebase's mint-time key (it ties a licence to the payment that
                // bought it and drives the monthly/yearly label), so it restarts
                // with the new purchase; the original stays in the audit log.
                licence.CompanySubscriptionId = newSub.Id;
                licence.PlanId = newSub.PlanId;
                licence.ExpiryDate = newSub.CurrentPeriodEnd;
                licence.CreatedAt = now;
                licence.CancelAtPeriodEnd = false;
                licence.CanceledAt = null;
                licence.UpdatedBy = actorUserId;
                licence.UpdatedAt = now;
                await AddLicenseAuditAsync(licence, now);

                // Take the seat out of the old subscription, crediting its unused
                // time. A seat that no longer renewed is already out of it.
                bool endOldNow = false;
                long? newQuantity = null;
                if (seatWasRenewing)
                {
                    if (otherRenewingSeats == 0)
                    {
                        endOldNow = true;
                        oldSub.CanceledAt = now;
                    }
                    else
                    {
                        var quantity = await _stripeService.GetSubscriptionQuantityAsync(oldSub.StripeSubscriptionId!);
                        if (quantity > 1)
                            newQuantity = quantity - 1;
                        else
                            _logger.LogError(
                                "[LicensePlanChange] Old StripeSub {StripeSubId} has quantity {Qty} with {Others} other renewing seats; seat NOT removed — fix billing manually.",
                                oldSub.StripeSubscriptionId, quantity, otherRenewingSeats);
                    }

                    if (endOldNow || newQuantity.HasValue)
                    {
                        oldSub.TeamCount = Math.Max(0, oldSub.TeamCount - 1);
                        oldSub.MaxTeamsAllowed = Math.Max(0, oldSub.MaxTeamsAllowed - 1);
                        oldSub.MaxPeopleAllowed = oldSub.MaxTeamsAllowed * MaxPeoplePerTeam;
                        await _subscriptionRepo.UpdateAsync(oldSub);
                    }
                }

                // Stripe last: when it fails, the rollback leaves nothing behind
                // and the webhook is retried.
                if (endOldNow)
                    await _stripeService.CancelSubscriptionNowAsync(oldSub.StripeSubscriptionId!, prorate: true);
                else if (newQuantity.HasValue)
                    await _stripeService.SetSubscriptionQuantityAsync(oldSub.StripeSubscriptionId!, newQuantity.Value, prorate: true);

                await tx.CommitAsync();

                _logger.LogInformation(
                    "[LicensePlanChange] Applied. LicenseId={LicenseId} SubId {FromSubId}→{NewSubId} (StripeSub {StripeSubId}) cycle {Cycle}; old sub ended now={EndedNow}, quantity→{Qty}.",
                    licenseId, fromSubId, newSub.Id, stripeSubscriptionId, cycle, endOldNow, newQuantity);

                return newSub;
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }
    }
}
