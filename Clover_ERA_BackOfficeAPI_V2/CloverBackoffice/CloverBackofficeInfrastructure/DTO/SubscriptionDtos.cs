using System.ComponentModel.DataAnnotations;
using CloverBackoffice.Infrastructure.Models;

namespace CloverBackoffice.Infrastructure.DTO
{
    // POST /api/subscription/purchase-additional-teams
    //
    // Buys N extra licenses for a company that already has an active paid
    // subscription. The endpoint returns a Stripe one-time payment URL.
    // When the webhook fires (checkout.session.completed with op=addLicenses),
    // the server mints N CompanyLicense rows on the company's existing
    // subscription, leaves them UNASSIGNED, and bumps the subscription's
    // TeamCount / MaxTeamsAllowed / MaxPeopleAllowed.
    //
    // UnitId is captured for context/audit only — the company admin assigns
    // the newly-minted licenses to units afterwards via the existing
    // /api/license-assignments flow.
    public class PurchaseAdditionalTeamsRequestDto
    {
        [Required] public uint ProfileId { get; set; }
        [Required] public uint UnitId { get; set; }
        [Required] public int AdditionalTeamsCount { get; set; }
    }

    public class PurchaseAdditionalTeamsResponseDto
    {
        public string SessionUrl { get; set; } = string.Empty;
        public int AdditionalTeamsCount { get; set; }
    }


    public class CurrentSubscriptionDto
    {
        public int Id { get; set; }
        public string BillingCycle { get; set; } = string.Empty;       // "Monthly" or "Annual"

        // Live flag: true when the subscription is currently active (Active and
        // NOT scheduled to cancel); false once it is canceled / expired / ended.
        // A canceled subscription keeps its Status=Active during the wind-down, so
        // this also checks the cancel flag — not just Status.
        public bool Status { get; set; }
        public int TeamCount { get; set; }
        public int MaxTeamsAllowed { get; set; }
        public int MaxPeopleAllowed { get; set; }
        public decimal MonthlyTotal { get; set; }
        public decimal AnnualTotal { get; set; }
        public decimal PricePerTeam { get; set; }
        public DateTime? TrialEndDate { get; set; }
        public DateTime CurrentPeriodEnd { get; set; }                 // = renewal date (also the "access ends" date once canceled)
        public bool CancelAtPeriodEnd { get; set; }
        public DateTime? CanceledAt { get; set; }                      // when cancellation was requested; null when not canceled
        public bool IsTrialUsed { get; set; }
        public decimal? LifetimeDiscountPercent { get; set; }
    }

    public class StartTrialRequestDto
    {
        public int TeamCount { get; set; } = 1;
        public BillingCycle BillingCycle { get; set; }
    }

    public class CheckoutRequestDto
    {
        public int TeamCount { get; set; } = 1;
        public BillingCycle BillingCycle { get; set; }
        public string? PromoCode { get; set; }

        // Optional. The profile the licences are bought for; null/0 = the
        // logged-in account's own profile. Another profile is allowed only for a
        // RRIDA Admin (403 otherwise).
        public int? ProfileId { get; set; }

        // Optional. The plan (api/plans: Light, Pro) the licences are bought on: TeamCount
        // picks its price tier and the licences give their Team Managers that plan. Send
        // planId or planCode; neither = the legacy flat-price checkout.
        public int? PlanId { get; set; }
        public string? PlanCode { get; set; }
    }

    public class CheckoutResponseDto
    {
        public string SessionUrl { get; set; } = string.Empty;
    }

    public class AddTeamsRequestDto
    {
        public int NewTeamCount { get; set; }
    }

    public class ChangeBillingCycleRequestDto
    {
        public BillingCycle BillingCycle { get; set; }
    }

    public class PriceCalculationDto
    {
        public int TeamCount { get; set; }
        public decimal PricePerTeam { get; set; }
        public decimal MonthlyTotal { get; set; }
        public decimal AnnualTotal { get; set; }
        public string TierName { get; set; } = string.Empty;
        public int SavingsPercent { get; set; }
    }

    public class SubscriptionStatusDto
    {
        public uint ProfileId { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public uint? UnitId { get; set; }
        public string? UnitName { get; set; }

        public int SubscriptionId { get; set; }
        public string SubscriptionType { get; set; } = string.Empty;   // "Trial" | "Paid"
        public string Status { get; set; } = string.Empty;             // SubscriptionStatus enum string
        public string PlanName { get; set; } = string.Empty;

        public DateTime StartDate { get; set; }                        // purchase or trial start
        public DateTime ExpiryDate { get; set; }                       // RAW end-date, never includes grace
        public int? GracePeriod { get; set; }                          // days, copied from sub.GracePeriod
        public DateTime? EffectiveExpiryDate { get; set; }             // ExpiryDate + GracePeriod

        public int TotalDurationDays { get; set; }                     // total length of current period/trial
        public int DaysRemaining { get; set; }                         // grace-aware countdown
        public bool IsExpired { get; set; }                            // grace-aware

        // True when this unit's subscription is resolved from a complimentary
        // license grant (a same-manager team riding another team's purchased
        // license) rather than a purchased assignment of its own.
        public bool IsComplimentary { get; set; }

        // When the unit's license assignment was closed (set the moment the
        // license expired and was unassigned). Null while the license is live.
        public DateTime? UnassignedAt { get; set; }

        // Stripe checkout URL the frontend can open to renew this subscription.
        // Populated ONLY when IsExpired==true / DaysRemaining==0. Null otherwise,
        // and also null when Stripe is misconfigured / unreachable so a single
        // checkout outage can't break the status read.
        public string? RenewalUrl { get; set; }
    }

    // Top-level subscription-detail response. Two clearly-separated sections:
    //   * CompanyLicenses → roll-up of every license owned by the company
    //     (applied + unapplied), shown once at the company level.
    //   * Units            → one entry per business unit that actually holds at
    //     least one active license. Each unit lists ONLY the licenses pinned
    //     to that unit (no cross-unit duplication).
    public class CompanySubscriptionDetailDto
    {
        public uint ProfileId { get; set; }
        public string CompanyName { get; set; } = string.Empty;

        // Card on file for the company's subscription (brand + last four),
        // sourced from the most recent payment that carries card details. Null
        // when no card payment exists (e.g. trial-only or invoice-paid).
        public PaymentCardDto? Card { get; set; }

        public CompanyLicensePoolDto CompanyLicenses { get; set; } = new();
        public List<SubscriptionDetailDto> Units { get; set; } = new();

        // Set only by POST /api/subscription/apply: what the extension changed.
        public TrialExtensionResultDto? TrialExtension { get; set; }
    }

    // Card-on-file summary surfaced alongside subscription details. Both fields
    // come straight off the Payments table (CardBrand / CardLast4).
    public class PaymentCardDto
    {
        public string? CardholderName { get; set; } // name on the card
        public string? CardType { get; set; }      // card brand, e.g. "visa", "mastercard"
        public string? CardLast4 { get; set; }     // last four digits of the card
        public int? ExpMonth { get; set; }         // expiry month (1-12)
        public int? ExpYear { get; set; }          // expiry year (4-digit)
        public string? Funding { get; set; }       // credit | debit | prepaid | unknown
        public string? Country { get; set; }       // ISO-3166-1 alpha-2 issuing country
        public string? PaymentMethodId { get; set; } // Stripe pm_… id of the card used
    }

    public class CompanyLicensePoolDto
    {
        public int TotalCount { get; set; }
        public int AppliedCount { get; set; }
        public int UnappliedCount { get; set; }
        public List<SubscriptionLicenseDto> Licenses { get; set; } = new();

        // Per plan (Light, Pro): purchased (live), consumed, available.
        public List<CloverBackoffice.Infrastructure.DTO.PlanManagement.PlanLicenseCountDto> Plans { get; set; } = new();

        // What the live paid licences cost per billing cycle: sum of PricePerManager.
        public decimal TotalMonthlyAmount { get; set; }
        public decimal TotalAnnualAmount { get; set; }
        public string Currency { get; set; } = "USD";
    }

    // Subscription + license info for a single business unit. Licenses[] is
    // strictly the licenses currently assigned to THIS unit — never the full
    // company set. ProfileId/Name are populated only when this DTO is returned
    // standalone (single-unit query); inside CompanySubscriptionDetailDto.Units
    // they're left blank because the wrapper already carries them.
    public class SubscriptionDetailDto
    {
        public uint? ProfileId { get; set; }
        public string? CompanyName { get; set; }
        public uint? UnitId { get; set; }
        public string? UnitName { get; set; }

        public bool HasSubscription { get; set; }
        public int? SubscriptionId { get; set; }
        public string? SubscriptionType { get; set; }                  // "Trial" | "Paid"
        public string? Status { get; set; }

        public DateTime? StartDate { get; set; }
        public DateTime? ExpiryDate { get; set; }                      // RAW end-date, never includes grace
        public int? GracePeriod { get; set; }                          // days, copied from sub.GracePeriod
        public DateTime? EffectiveExpiryDate { get; set; }             // ExpiryDate + GracePeriod (what DaysRemaining counts to)
        public int? TotalTrialDurationDays { get; set; }               // populated only for Trial
        public int? DaysRemaining { get; set; }                        // (EffectiveExpiryDate - now), grace honored
        public bool? IsExpired { get; set; }                           // now >= EffectiveExpiryDate

        public int AppliedLicenseCount { get; set; }

        // True when this unit holds NO purchased license of its own and its
        // coverage comes entirely from a complimentary grant. Counts
        // (AppliedLicenseCount) still reflect purchased licenses only.
        public bool IsComplimentary { get; set; }

        public List<SubscriptionLicenseDto> Licenses { get; set; } = new();
    }

    // Payload for POST /api/subscription/apply. Admin-only trial extension /
    // grace tool — it ONLY extends the window of a company that is currently on
    // a trial; it never converts a trial to a paid subscription. Two modes:
    //   * Company-wide — ProfileId only. The extension applies to every unit
    //                    under the company and to all of its licenses.
    //   * Single unit  — ProfileId + UnitId. The extension applies only to the
    //                    specified unit and the license(s) assigned to it.
    // Provide ExtendByDays (days added to the current trial end) or EndDate (new
    // trial end date), and/or GracePeriod (extra days). ExtendByDays is
    // company-wide only. Every call is recorded in TrialExtensionAuditLogs.
    public class ApplySubscriptionRequestDto
    {
        public uint? ProfileId { get; set; }
        public uint? UnitId { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }             // new trial end date — licenses extend to match
        public int? GracePeriod { get; set; }              // extra grace days on top of the trial end
        public int? ExtendByDays { get; set; }             // days added to the current trial end (from today if already ended)
        public string? Notes { get; set; }                 // optional reason, kept in the audit row
    }

    // What one trial extension changed. Returned on CompanySubscriptionDetailDto
    // by POST /api/subscription/apply; null on every other read.
    public class TrialExtensionResultDto
    {
        public int AuditId { get; set; }
        public string Scope { get; set; } = string.Empty;  // PROFILE | UNIT
        public uint? UnitId { get; set; }
        public int? DaysAdded { get; set; }
        public DateTime? PreviousTrialEndDate { get; set; }
        public DateTime? NewTrialEndDate { get; set; }
        public int? GracePeriod { get; set; }
        public DateTime? EffectiveExpiryDate { get; set; } // NewTrialEndDate + GracePeriod
        public int LicensesUpdated { get; set; }
        public string? Notes { get; set; }
        public int ExtendedByUserId { get; set; }
        public DateTime ExtendedAt { get; set; }
    }

    // Clovera-admin-only license expiry extension (POST /api/subscription/extend-license).
    //   * ProfileId only          → extend every license under the company by one
    //                               billing cycle. A company on a Trial is converted
    //                               to Paid and all its licenses promoted accordingly.
    //   * ProfileId + LicenseId   → extend ONLY that license by one billing cycle;
    //                               all other licenses (and the plan) are untouched.
    // ExpiryDate, when supplied, overrides the one-billing-cycle default and sets
    // the new expiry explicitly (authoritative — applied even if it shortens the
    // current window). Notes is an optional remark persisted on each extended row.
    public class ExtendLicenseRequestDto
    {
        public uint? ProfileId { get; set; }
        public int? LicenseId { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public string? Notes { get; set; }
    }

    public class SubscriptionLicenseDto
    {
        public int LicenseId { get; set; }
        public string LicenseKey { get; set; } = string.Empty;
        public string LicenseType { get; set; } = string.Empty;        // "Trial" | "Paid"
        public int? CompanySubscriptionId { get; set; }

        // Billing cadence of the license: "Trial" for a trial license, otherwise
        // "Monthly" | "Annual" (resolved from its subscription's cycle, with a
        // >180-day span override for a yearly seat minted on a monthly plan).
        public string BillingCycle { get; set; } = string.Empty;

        // Last four digits of the card used to pay for this license's
        // subscription (most-recent card payment on CompanySubscriptionId). Null
        // when no card payment exists (trial-only / invoice-paid).
        public string? CardLast4 { get; set; }

        public DateTime? ExpiryDate { get; set; }                      // this license's individual expiry
        public bool IsExpired { get; set; }                            // ExpiryDate has passed

        public string AssignmentStatus { get; set; } = string.Empty;   // "Applied" | "Not Applied" | "Complimentary"
        // Team Manager (Users.UserId) who holds the license — 1 license = 1 Team
        // Manager. Null when the license is not applied.
        public int? AssignedToManagerId { get; set; }
        public string? AssignedToManagerName { get; set; }

        // True when this row represents complimentary coverage rather than a
        // purchased assignment (does not consume a license slot).
        public bool IsComplimentary { get; set; }
        public DateTime? AssignedAt { get; set; }
        public DateTime? UnassignedAt { get; set; }                    // set when the assignment was closed (e.g. on expiry)

        // Plan catalogue (api/plans): the plan this licence gives; a licence from before plans
        // reads as Pro (IsLegacyPlan).
        public int? ServicePlanId { get; set; }
        public string? ServicePlanCode { get; set; }
        public string? ServicePlanName { get; set; }
        public bool IsLegacyPlan { get; set; }

        // Price per manager the licence was bought at, for BillingCycle (null for trial).
        public decimal? PricePerManager { get; set; }
    }

    // Lightweight projection of a Stripe Checkout Session, returned by
    // IStripeService so the service layer can reconcile paid sessions without a
    // hard dependency on Stripe SDK types (mirrors the CardDetails pattern).
    public class StripeCheckoutSessionInfo
    {
        public string Id { get; set; } = string.Empty;          // cs_...
        public string? PaymentIntentId { get; set; }            // pi_... (present for mode=payment)
        public string? InvoiceId { get; set; }
        public string? PaymentStatus { get; set; }              // "paid" | "unpaid" | "no_payment_required"
        public string? Status { get; set; }                     // "open" | "complete" | "expired"
        public long? AmountTotal { get; set; }                  // cents
        public string? Currency { get; set; }
        public string? Url { get; set; }                        // hosted checkout URL, set while open
        public Dictionary<string, string> Metadata { get; set; } = new();
    }

    // Result of POST /api/subscription/reconcile-add-licenses. Reports what the
    // Stripe-sourced backfill found and minted for a company.
    public class AddLicenseReconcileResultDto
    {
        public uint ProfileId { get; set; }
        public int PaidSessionsFound { get; set; }              // paid op=addLicenses sessions on the customer
        public int SessionsMinted { get; set; }                 // sessions that produced a fresh mint this run
        public int SessionsAlreadyProcessed { get; set; }       // skipped by the idempotency guard
        public int LicensesMinted { get; set; }                 // total CompanyLicense rows created this run
    }
}
