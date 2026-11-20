using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;

namespace CloverBackoffice.Services.Interfaces
{
    public interface ISubscriptionService
    {
        Task<CurrentSubscriptionDto?> GetCurrentSubscriptionAsync(uint profileId);
        Task<CurrentSubscriptionDto> StartTrialAsync(uint profileId, int teamCount, BillingCycle billingCycle);
        // Billing contact (email, name) for a profile's Stripe customer: its oldest
        // live Profile Admin, else its oldest live user. Null when the profile does
        // not exist or is deleted. Used when someone buys for another profile.
        Task<(string Email, string Name)?> GetProfileBillingContactAsync(uint profileId);

        // servicePlanId / servicePlanCode: buy licences on a plan catalogue plan (tier price by
        // quantity, plan stamped on the licences). Neither = legacy flat-price checkout.
        // Light / Pro switch: cancel the licence the manager left at the end of its paid period.
        // False when there is nothing to cancel (trial, no Stripe subscription, already cancelled).
        Task<bool> CancelReplacedLicenceAsync(int licenseId, int actorUserId);

        Task<CheckoutResponseDto> CreateCheckoutAsync(uint profileId, int teamCount, BillingCycle billingCycle, string companyEmail, string companyName, string? promoCode = null, int? servicePlanId = null, string? servicePlanCode = null);
        Task<CurrentSubscriptionDto> AddTeamsAsync(uint profileId, int newTeamCount);

        // Buys N extra licenses for a company that already has an active paid
        // subscription. Returns a Stripe one-time payment URL. The licenses
        // are minted by the checkout.session.completed webhook handler when
        // it sees metadata op=addLicenses; they land UNASSIGNED so the
        // company admin can route them via the existing license-apply flow.
        Task<PurchaseAdditionalTeamsResponseDto> PurchaseAdditionalTeamsAsync(PurchaseAdditionalTeamsRequestDto dto, int? actorUserId);
        Task<CurrentSubscriptionDto> ChangeBillingCycleAsync(uint profileId, BillingCycle newCycle);

        // Cancels ONE Team Manager's licence at the end of its current period.
        // The licence stays ACTIVE until then and does not renew. When other
        // renewing seats share its Stripe subscription, the quantity drops by one
        // (no proration); when it is the last renewing seat, the whole
        // subscription is set to cancel_at_period_end. Idempotent. Throws
        // UnauthorizedAccessException unless the actor is a RRIDA Admin or a
        // Profile Admin of the user's profile, InvalidOperationException when the
        // user has no active licence or is not in profileId (when sent).
        Task<CancelLicenseResultDto> CancelLicenseAsync(int userId, User actor, int? profileId = null);

        // Undoes CancelLicenseAsync while the licence's paid period is still
        // running: the seat goes back on its Stripe subscription (quantity +1, or
        // the subscription's cancel is cleared with this licence as its only
        // renewing seat), no charge now. Idempotent. Same authorization and
        // errors as CancelLicenseAsync; an ended licence must be bought again.
        Task<CancelLicenseResultDto> ReactivateLicenseAsync(int userId, User actor, int? profileId = null);

        // Starts a billing-cycle change of ONE Team Manager's licence: validates
        // it and returns a Stripe Checkout URL for a new 1-seat subscription on
        // the requested cycle. Nothing changes until checkout completes — the
        // webhook then moves the licence onto the new subscription and takes the
        // seat out of the old one with a credit for its unused time. Reuses an
        // open checkout for the same change; expires one for a different cycle.
        Task<LicensePlanChangeResultDto> StartLicensePlanChangeAsync(int userId, BillingCycle billingCycle, User actor);
        Task HandleWebhookEventAsync(object stripeEvent);
        Task<SubscriptionStatusDto> GetSubscriptionStatusAsync(uint profileId, uint? unitId);
        Task<List<SubscriptionStatusDto>> GetSubscriptionsForAllUnitsAsync(uint profileId);
        Task<SubscriptionDetailDto> GetSubscriptionDetailAsync(uint profileId, uint unitId);
        Task<CompanySubscriptionDetailDto> GetSubscriptionDetailsForAllUnitsAsync(uint profileId);

        // Resolves the owning company of a unit. Used by transaction/details to
        // authorize + scope when only a UnitId is supplied. Throws when the unit
        // doesn't exist.
        Task<uint> GetProfileIdForUnitAsync(uint unitId);

        // Company- or unit-scoped transaction view: subscription details + paged
        // payment history (with card info per row) + the scope's card-on-file.
        // unitId == null → company scope; unitId set → that unit's scope.
        // billingCycle, when supplied, filters the returned licenses to just that
        // cadence (Monthly / Annual); null keeps every license (current behavior).
        Task<TransactionDetailsDto> GetTransactionDetailsAsync(uint profileId, uint? unitId, int page, int pageSize, BillingCycle? billingCycle = null);
        Task<CompanySubscriptionDetailDto> ApplySubscriptionAsync(ApplySubscriptionRequestDto dto, int? actorUserId);

        // Clovera-admin-only license expiry extension. Extends license(s) by one
        // billing cycle (Monthly → +1 month, Annual → +1 year) from their current
        // expiry (or from now if already lapsed):
        //   * ProfileId only        → extend every license under the company. A
        //                             Trial company is converted to Paid and its
        //                             licenses promoted (Trial → Paid).
        //   * ProfileId + LicenseId → extend ONLY that license; the plan and all
        //                             other licenses stay unchanged.
        // The optional Notes remark is stamped on each extended license.
        Task<CompanySubscriptionDetailDto> ExtendLicenseAsync(ExtendLicenseRequestDto dto, int? actorUserId);

        // Manual repair: force a paid subscription's CurrentPeriodEnd to
        // now + 1 month (or +1 year for Annual) when a webhook race or a
        // stale deployment left the row stuck at a past date. Idempotent,
        // safe to call multiple times. Returns the resulting status DTO so
        // callers can verify the new dates.
        Task<CurrentSubscriptionDto> RepairRenewalDatesAsync(uint profileId);

        // Clovera-admin-only recovery for additional-license (op=addLicenses)
        // top-ups whose Stripe checkout.session.completed webhook never reached
        // the mint (e.g. a signature / API-version failure window left no DB
        // trace). Pulls the company's PAID addLicenses checkout sessions from
        // Stripe and re-drives the idempotent mint for any never processed.
        // Safe to run repeatedly — already-minted charges are skipped.
        Task<AddLicenseReconcileResultDto> ReconcileAddLicensesAsync(uint profileId);
    }
}
