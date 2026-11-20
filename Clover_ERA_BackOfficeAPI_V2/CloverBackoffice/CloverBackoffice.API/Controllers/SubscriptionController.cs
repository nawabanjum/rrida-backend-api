using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CloverBackoffice.API.Controllers
{
    [Route("api/subscription")]
    [ApiController]
    [Authorize]
    public class SubscriptionController : ControllerBase
    {
        private readonly ISubscriptionService _subscriptionService;
        private readonly ITokenReaderService _tokenReader;

        public SubscriptionController(ISubscriptionService subscriptionService, ITokenReaderService tokenReader)
        {
            _subscriptionService = subscriptionService;
            _tokenReader = tokenReader;
        }

        // Confirms the caller is allowed to read/manage the License module for
        // `profileId`. Clovera (platform) admins may touch any company; every
        // other role — Company admin, Partner admin, unit lead, … — is confined
        // to its OWN company. This is the gate that stops a Partner admin from
        // selecting a child company in the "Access License" dropdown and reading
        // or managing that child's licenses. Returns an error IActionResult when
        // access is denied, or null when the caller is authorized.
        private async Task<IActionResult?> EnsureCompanyAccessAsync(uint profileId)
        {
            var user = await _tokenReader.GetCurrentUserAsync();
            if (user == null)
                return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.Invalidtoken));

            // RRIDA Admins are the only role permitted to inspect an arbitrary
            // profile's licenses.
            if (user.UserGroupId == (int)CloverBackoffice.Components.Enums.UserGroups.RridaAdmin)
                return null;

            // Anyone else may only touch the profile they belong to.
            if (user.ProfileId.HasValue && (uint)user.ProfileId.Value == profileId)
                return null;

            return StatusCode(403, ApiResponseHandler.Fail<string>(ResponseMessages.Errors.LicenseModuleAccessDenied));
        }

        //[HttpGet("current")]
        //public async Task<IActionResult> GetCurrent()
        //{
        //    try
        //    {
        //        var user = await _tokenReader.GetCurrentUserAsync();
        //        if (user == null)
        //            return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.Invalidtoken));

        //        if (user.ProfileId == null)
        //            return Ok(ApiResponseHandler.Fail<string>("No company linked to this account."));

        //        var sub = await _subscriptionService.GetCurrentSubscriptionAsync((uint)user.ProfileId.Value);
        //        if (sub == null)
        //            return Ok(ApiResponseHandler.Fail<string>("No subscription found."));

        //        return Ok(ApiResponseHandler.Success(sub, "Subscription retrieved successfully."));
        //    }
        //    catch (Exception ex)
        //    {
        //        return Ok(ApiResponseHandler.Fail<string>($"{ResponseMessages.Errors.ExceptionMsg} :: {ex.GetBaseException().Message}"));
        //    }
        //}

        //[HttpPost("start-trial")]
        //public async Task<IActionResult> StartTrial([FromBody] StartTrialRequestDto dto)
        //{
        //    try
        //    {
        //        var user = await _tokenReader.GetCurrentUserAsync();
        //        if (user == null)
        //            return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.Invalidtoken));

        //        if (user.ProfileId == null)
        //            return Ok(ApiResponseHandler.Fail<string>("No company linked to this account."));

        //        var result = await _subscriptionService.StartTrialAsync((uint)user.ProfileId.Value, dto.TeamCount, dto.BillingCycle);
        //        return Ok(ApiResponseHandler.Success(result, "Free trial started successfully."));
        //    }
        //    catch (InvalidOperationException ex)
        //    {
        //        return Ok(ApiResponseHandler.Fail<string>(ex.Message));
        //    }
        //    catch (Exception ex)
        //    {
        //        return Ok(ApiResponseHandler.Fail<string>($"{ResponseMessages.Errors.ExceptionMsg} :: {ex.GetBaseException().Message}"));
        //    }
        //}

        //   POST /api/subscription/checkout
        //   { "teamCount": 6, "billingCycle": 2, "planCode": "pro", "profileId": 119 }
        // planId / planCode (optional): buy licences on that plan (api/plans). teamCount picks
        // its price tier (GET api/plans/price shows the same amount) and the licences minted by
        // the webhook carry the plan. Without a plan: the legacy flat-price checkout.
        [HttpPost("checkout")]
        public async Task<IActionResult> Checkout([FromBody] CheckoutRequestDto dto)
        {
            try
            {
                var user = await _tokenReader.GetCurrentUserAsync();
                if (user == null)
                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.Invalidtoken));

                // Which profile the licences are bought for: the ProfileId sent in
                // the body, else the logged-in account's own profile.
                uint profileId;
                if (dto.ProfileId is > 0)
                    profileId = (uint)dto.ProfileId.Value;
                else if (user.ProfileId != null)
                    profileId = (uint)user.ProfileId.Value;
                else
                    return Ok(ApiResponseHandler.Fail<string>("No company linked to this account."));

                // RRIDA Admin may buy for any profile; everyone else only for their own (403).
                var denied = await EnsureCompanyAccessAsync(profileId);
                if (denied != null)
                    return denied;

                // Buying for another profile: its Stripe customer carries that
                // profile's billing contact, not the caller's.
                var email = user.Username;
                var name = user.Name;
                if (user.ProfileId != (int)profileId)
                {
                    var contact = await _subscriptionService.GetProfileBillingContactAsync(profileId);
                    if (contact == null)
                        return Ok(ApiResponseHandler.Fail<string>($"Profile {profileId} not found."));
                    email = contact.Value.Email;
                    name = contact.Value.Name;
                }

                var promoCode = string.IsNullOrWhiteSpace(dto.PromoCode) ? null : dto.PromoCode.Trim();

                var result = await _subscriptionService.CreateCheckoutAsync(
                    profileId,
                    dto.TeamCount,
                    dto.BillingCycle,
                    email,
                    name,
                    promoCode,
                    dto.PlanId,
                    dto.PlanCode);

                return Ok(ApiResponseHandler.Success(result, "Checkout session created."));
            }
            catch (InvalidOperationException ex)
            {
                return Ok(ApiResponseHandler.Fail<string>(ex.Message));
            }
            catch (Exception ex)
            {
                return Ok(ApiResponseHandler.Fail<string>($"{ResponseMessages.Errors.ExceptionMsg} :: {ex.GetBaseException().Message}"));
            }
        }

        // GET /api/subscription/transaction/details?profileId=195[&unitId=7][&page=1&pageSize=10]
        //
        // Company- or unit-scoped transaction view. At least one of profileId /
        // unitId must be supplied; when only a unitId is given its owning
        // company is resolved. Returns the subscription details for the scope,
        // the paged payment history (each row with its card info), and the
        // scope's card-on-file. Reuses ISubscriptionService/IPaymentService
        // mapping so the transaction/card fields match /api/payment/history.
        [HttpGet("transaction/details")]
        public async Task<IActionResult> GetTransactionDetails(
            [FromQuery] uint? profileId,
            [FromQuery] uint? unitId,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] string? billingCycle = null)
        {
            try
            {
                bool hasCompany = profileId.HasValue && profileId.Value != 0;
                bool hasUnit = unitId.HasValue && unitId.Value != 0;

                // Validation rule: at least one of ProfileId / UnitId is required.
                if (!hasCompany && !hasUnit)
                    return Ok(ApiResponseHandler.Fail<string>("Provide ProfileId or UnitId."));

                // Optional billing-cycle filter. "Monthly" → only monthly licenses;
                // "Annually" (also "Annual"/"Yearly") → only annual licenses;
                // omitted/blank → no filter (current behavior). Anything else is a
                // bad request.
                BillingCycle? cycleFilter = null;
                if (!string.IsNullOrWhiteSpace(billingCycle))
                {
                    var bc = billingCycle.Trim();
                    if (bc.Equals("Monthly", StringComparison.OrdinalIgnoreCase))
                        cycleFilter = BillingCycle.Monthly;
                    else if (bc.Equals("Annually", StringComparison.OrdinalIgnoreCase)
                             || bc.Equals("Annual", StringComparison.OrdinalIgnoreCase)
                             || bc.Equals("Yearly", StringComparison.OrdinalIgnoreCase))
                        cycleFilter = BillingCycle.Annual;
                    else
                        return Ok(ApiResponseHandler.Fail<string>("Invalid billingCycle. Use 'Monthly' or 'Annually'."));
                }

                // Resolve the owning company (needed for authorization + scope).
                // When only a unit is supplied, derive its company.
                uint resolvedProfileId = hasCompany
                    ? profileId!.Value
                    : await _subscriptionService.GetProfileIdForUnitAsync(unitId!.Value);

                // Same company-access gate as the other subscription endpoints.
                var denied = await EnsureCompanyAccessAsync(resolvedProfileId);
                if (denied != null)
                    return denied;

                var result = await _subscriptionService.GetTransactionDetailsAsync(
                    resolvedProfileId,
                    hasUnit ? unitId : null,
                    page,
                    pageSize,
                    cycleFilter);

                return Ok(ApiResponseHandler.Success(result, "Transaction details retrieved."));
            }
            catch (InvalidOperationException ex)
            {
                return Ok(ApiResponseHandler.Fail<string>(ex.Message));
            }
            catch (Exception)
            {
                return Ok(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.ExceptionMsg));
            }
        }

        //[HttpPut("add-teams")]
        //public async Task<IActionResult> AddTeams([FromBody] AddTeamsRequestDto dto)
        //{
        //    try
        //    {
        //        var user = await _tokenReader.GetCurrentUserAsync();
        //        if (user == null)
        //            return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.Invalidtoken));

        //        if (user.ProfileId == null)
        //            return Ok(ApiResponseHandler.Fail<string>("No company linked to this account."));

        //        var result = await _subscriptionService.AddTeamsAsync((uint)user.ProfileId.Value, dto.NewTeamCount);
        //        return Ok(ApiResponseHandler.Success(result, "Subscription upgraded successfully."));
        //    }
        //    catch (InvalidOperationException ex)
        //    {
        //        return Ok(ApiResponseHandler.Fail<string>(ex.Message));
        //    }
        //    catch (Exception ex)
        //    {
        //        return Ok(ApiResponseHandler.Fail<string>($"{ResponseMessages.Errors.ExceptionMsg} :: {ex.GetBaseException().Message}"));
        //    }
        //}

        //[HttpPut("change-billing-cycle")]
        //public async Task<IActionResult> ChangeBillingCycle([FromBody] ChangeBillingCycleRequestDto dto)
        //{
        //    try
        //    {
        //        var user = await _tokenReader.GetCurrentUserAsync();
        //        if (user == null)
        //            return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.Invalidtoken));

        //        if (user.ProfileId == null)
        //            return Ok(ApiResponseHandler.Fail<string>("No company linked to this account."));

        //        var result = await _subscriptionService.ChangeBillingCycleAsync((uint)user.ProfileId.Value, dto.BillingCycle);
        //        return Ok(ApiResponseHandler.Success(result, "Billing cycle updated successfully."));
        //    }
        //    catch (InvalidOperationException ex)
        //    {
        //        return Ok(ApiResponseHandler.Fail<string>(ex.Message));
        //    }
        //    catch (Exception ex)
        //    {
        //        return Ok(ApiResponseHandler.Fail<string>($"{ResponseMessages.Errors.ExceptionMsg} :: {ex.GetBaseException().Message}"));
        //    }
        //}

        // POST /api/subscription/cancel
        //
        // Cancels ONE Team Manager's licence at the end of its paid period (same
        // as POST /api/license-assignments/cancel). The licence stays ACTIVE
        // until periodEndAt and does not renew; other Team Managers on the same
        // Stripe subscription keep renewing. Repeating the call returns the same
        // scheduled cancellation. RRIDA Admin any profile, Profile Admin own.
        //
        //   Body: { "userId": 10, "profileId": 69 }   (profileId optional)
        [HttpPost("cancel")]
        public Task<IActionResult> Cancel([FromBody] CancelLicenseRequestDto dto)
            => RunLicenceRenewalAsync(dto, (userId, actor, profileId) =>
                _subscriptionService.CancelLicenseAsync(userId, actor, profileId),
                ResponseMessages.Success.LicenseCancelScheduled);

        // POST /api/subscription/reactivate
        //
        // Undoes a cancellation while the licence's paid period is still running,
        // so the Team Manager's licence renews again. No charge now; the seat is
        // billed again from the next renewal. An ended licence must be bought
        // again through checkout. Same body and authorization as /cancel.
        //
        //   Body: { "userId": 10, "profileId": 69 }   (profileId optional)
        [HttpPost("reactivate")]
        public Task<IActionResult> Reactivate([FromBody] CancelLicenseRequestDto dto)
            => RunLicenceRenewalAsync(dto, (userId, actor, profileId) =>
                _subscriptionService.ReactivateLicenseAsync(userId, actor, profileId),
                ResponseMessages.Success.LicenseReactivated);

        private async Task<IActionResult> RunLicenceRenewalAsync(
            CancelLicenseRequestDto? dto,
            Func<int, User, int?, Task<CancelLicenseResultDto>> action,
            string successMessage)
        {
            try
            {
                if (dto?.UserID is null or <= 0)
                    return BadRequest(ApiResponseHandler.Fail<string>("UserId is required."));

                var actor = await _tokenReader.GetCurrentUserAsync();
                if (actor == null)
                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.Invalidtoken));

                var result = await action(dto.UserID.Value, actor, dto.ProfileId);
                return Ok(ApiResponseHandler.Success(result, successMessage));
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, ApiResponseHandler.Fail<string>(ex.Message));
            }
            catch (InvalidOperationException ex)
            {
                return Ok(ApiResponseHandler.Fail<string>(ex.Message));
            }
            catch (Exception ex)
            {
                return Ok(ApiResponseHandler.Fail<string>($"{ResponseMessages.Errors.ExceptionMsg} :: {ex.GetBaseException().Message}"));
            }
        }

        // POST /api/subscription/purchase-additional-teams
        //
        // Opens a NEW recurring (mode=subscription) Stripe checkout for N extra
        // teams and returns the SessionUrl the client opens. After Stripe confirms
        // payment, the checkout.session.completed webhook creates a brand-new
        // CompanySubscription (CreateAdditionalSubscriptionAsync) with its own
        // payment, its own UNASSIGNED license batch, and its own billing cycle /
        // renewal — fully independent of the company's other subscriptions, so it
        // can later be canceled on its own SubscriptionId. The admin then routes the
        // new licenses to units via /api/license-assignments.
        [HttpPost("purchase-additional-teams")]
        public async Task<IActionResult> PurchaseAdditionalTeams([FromBody] PurchaseAdditionalTeamsRequestDto dto)
        {
            try
            {
                if (dto == null || !ModelState.IsValid)
                    return Ok(ApiResponseHandler.Fail<string>("Invalid payload."));

                var purchaseDenied = await EnsureCompanyAccessAsync(dto.ProfileId);
                if (purchaseDenied != null)
                    return purchaseDenied;

                var actor = _tokenReader.GetUserIdFromToken();
                var result = await _subscriptionService.PurchaseAdditionalTeamsAsync(dto, actor);
                return Ok(ApiResponseHandler.Success(
                    result,
                    "Payment link generated. Complete the payment to add the requested licenses to your account."));
            }
            catch (InvalidOperationException ex)
            {
                return Ok(ApiResponseHandler.Fail<string>(ex.Message));
            }
            catch (Exception ex)
            {
                return Ok(ApiResponseHandler.Fail<string>($"{ResponseMessages.Errors.ExceptionMsg} :: {ex.GetBaseException().Message}"));
            }
        }

        // GET /api/subscription/detail?profileId=125
        //     → CompanySubscriptionDetailDto: company-level license pool +
        //       one entry per unit that holds an active license.
        // GET /api/subscription/detail?profileId=125&unitId=7
        //     → SubscriptionDetailDto: that unit's subscription + its own
        //       assigned licenses. No company-level summary, no other units.
        [HttpGet("detail")]
        public async Task<IActionResult> GetDetail([FromQuery] uint profileId, [FromQuery] uint? unitId = null)
        {
            try
            {
                if (profileId == 0)
                    return Ok(ApiResponseHandler.Fail<string>("ProfileId is required."));

                var detailDenied = await EnsureCompanyAccessAsync(profileId);
                if (detailDenied != null)
                    return detailDenied;

                if (unitId.HasValue && unitId.Value != 0)
                {
                    var single = await _subscriptionService.GetSubscriptionDetailAsync(profileId, unitId.Value);
                    return Ok(ApiResponseHandler.Success(single, "Subscription detail retrieved successfully."));
                }

                var list = await _subscriptionService.GetSubscriptionDetailsForAllUnitsAsync(profileId);
                return Ok(ApiResponseHandler.Success(list, "Unit subscription details retrieved successfully."));
            }
            catch (InvalidOperationException ex)
            {
                return Ok(ApiResponseHandler.Fail<string>(ex.Message));
            }
            catch (Exception ex)
            {
                return Ok(ApiResponseHandler.Fail<string>($"{ResponseMessages.Errors.ExceptionMsg} :: {ex.GetBaseException().Message}"));
            }
        }

        // POST /api/subscription/apply
        //
        // Admin-only trial extension / grace tool. Extends the trial window of a
        // company that is CURRENTLY on a trial — it never converts a trial to a
        // paid subscription. Two modes:
        //   * ProfileId only      → extend every unit + every license under the
        //                           company.
        //   * ProfileId + UnitId  → extend only that unit and the license(s)
        //                           assigned to it.
        // EndDate sets the new trial end date (licenses move with it); ExtendByDays
        // adds that many days to the current trial end instead (whole profile
        // only); GracePeriod adds extra days on top. Each call is audited in
        // TrialExtensionAuditLogs; the response carries trialExtension (old/new
        // end date, days, who, when).
        //
        //   POST /api/subscription/apply
        //   { "profileId": 69, "extendByDays": 14, "notes": "Sales asked for 2 more weeks" }
        [HttpPost("apply")]
        public async Task<IActionResult> Apply([FromBody] ApplySubscriptionRequestDto dto)
        {
            try
            {
                if (dto == null)
                    return Ok(ApiResponseHandler.Fail<string>("Request body is required."));

                // Trial extension is a RRIDA-Admin-only operation. A Profile Admin
                // or Manager must be blocked even though they hold a valid token.
                var actorUser = await _tokenReader.GetCurrentUserAsync();
                if (actorUser == null)
                    return Unauthorized(ApiResponseHandler.Fail<string>("Invalid or missing token."));

                if (actorUser.UserGroupId != (int)CloverBackoffice.Components.Enums.UserGroups.RridaAdmin)
                    return StatusCode(403, ApiResponseHandler.Fail<string>("Only a RRIDA Admin can extend a trial."));

                var result = await _subscriptionService.ApplySubscriptionAsync(dto, actorUser.UserId);
                return Ok(ApiResponseHandler.Success(result, "Trial extended successfully."));
            }
            catch (InvalidOperationException ex)
            {
                return Ok(ApiResponseHandler.Fail<string>(ex.Message));
            }
            catch (Exception ex)
            {
                return Ok(ApiResponseHandler.Fail<string>($"{ResponseMessages.Errors.ExceptionMsg} :: {ex.GetBaseException().Message}"));
            }
        }

        // POST /api/subscription/extend-license
        //
        // Clovera-admin-only license expiry extension. TRIAL COMPANIES ONLY — a
        // company that is on (or has ever converted to) a paid subscription is
        // rejected, matching the /apply trial-extension guard, so paid plans are
        // never extended for free. Extends license(s) by one billing cycle
        // (Monthly → +1 month, Annual → +1 year):
        //   * ProfileId only        → extend every license under the company. The
        //                             trial is converted to Paid and its licenses
        //                             promoted (Trial → Paid).
        //   * ProfileId + LicenseId → extend ONLY that license; the plan and all
        //                             other licenses stay unchanged.
        // Notes is an optional remark persisted on each extended license.
        [HttpPost("extend-license")]
        public async Task<IActionResult> ExtendLicense([FromBody] ExtendLicenseRequestDto dto)
        {
            try
            {
                if (dto == null)
                    return Ok(ApiResponseHandler.Fail<string>("Request body is required."));

                // License extension is a RRIDA-Admin-only operation. A Profile Admin
                // or Manager must be blocked even though they hold a valid token.
                var actorUser = await _tokenReader.GetCurrentUserAsync();
                if (actorUser == null)
                    return Unauthorized(ApiResponseHandler.Fail<string>("Invalid or missing token."));

                if (actorUser.UserGroupId != (int)CloverBackoffice.Components.Enums.UserGroups.RridaAdmin)
                    return StatusCode(403, ApiResponseHandler.Fail<string>("Only a RRIDA Admin can extend a license."));

                var result = await _subscriptionService.ExtendLicenseAsync(dto, actorUser.UserId);
                return Ok(ApiResponseHandler.Success(result, "License expiry extended successfully."));
            }
            catch (InvalidOperationException ex)
            {
                return Ok(ApiResponseHandler.Fail<string>(ex.Message));
            }
            catch (Exception ex)
            {
                return Ok(ApiResponseHandler.Fail<string>($"{ResponseMessages.Errors.ExceptionMsg} :: {ex.GetBaseException().Message}"));
            }
        }

        // POST /api/subscription/reconcile-add-licenses?profileId=154
        //
        // Clovera-admin-only recovery for additional-license (op=addLicenses)
        // top-ups whose Stripe checkout.session.completed webhook never reached
        // the mint — e.g. the signature / API-version failure window that leaves
        // NO trace in our DB (no license, no TeamCount bump, no Payment row).
        // Pulls the company's PAID addLicenses checkout sessions from Stripe and
        // mints any never processed. Idempotent: already-minted charges are
        // skipped, so it is safe to run repeatedly.
        [HttpPost("reconcile-add-licenses")]
        public async Task<IActionResult> ReconcileAddLicenses([FromQuery] uint profileId)
        {
            try
            {
                if (profileId == 0)
                    return Ok(ApiResponseHandler.Fail<string>("ProfileId is required."));

                // Recovery mints paid licenses — restrict to RRIDA Admins,
                // matching /apply and /extend-license.
                var actorUser = await _tokenReader.GetCurrentUserAsync();
                if (actorUser == null)
                    return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.Invalidtoken));

                if (actorUser.UserGroupId != (int)CloverBackoffice.Components.Enums.UserGroups.RridaAdmin)
                    return StatusCode(403, ApiResponseHandler.Fail<string>("Only a RRIDA Admin can reconcile licenses."));

                var result = await _subscriptionService.ReconcileAddLicensesAsync(profileId);
                return Ok(ApiResponseHandler.Success(result, "Add-license reconcile completed."));
            }
            catch (InvalidOperationException ex)
            {
                return Ok(ApiResponseHandler.Fail<string>(ex.Message));
            }
            catch (Exception ex)
            {
                return Ok(ApiResponseHandler.Fail<string>($"{ResponseMessages.Errors.ExceptionMsg} :: {ex.GetBaseException().Message}"));
            }
        }

        // POST /api/subscription/repair-renewal-dates
        //
        // Manual recovery for rows where Stripe payment completed
        // (StripeSubscriptionId is set) but CurrentPeriodEnd never rolled
        // forward — typically a webhook arriving before the new code was
        // deployed, or a Stripe API-version mismatch that produced
        // 1970-01-01 in the old handler. Sets CurrentPeriodStart = now and
        // CurrentPeriodEnd = now + 1 month (or +1 year for Annual), flips
        // Status=Active, and runs the license/assignment restore cascade.
        // Idempotent — safe to call repeatedly.
        //[HttpPost("repair-renewal-dates")]
        //public async Task<IActionResult> RepairRenewalDates()
        //{
        //    try
        //    {
        //        var user = await _tokenReader.GetCurrentUserAsync();
        //        if (user == null)
        //            return Unauthorized(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.Invalidtoken));

        //        if (user.ProfileId == null)
        //            return Ok(ApiResponseHandler.Fail<string>("No company linked to this account."));

        //        var result = await _subscriptionService.RepairRenewalDatesAsync((uint)user.ProfileId.Value);
        //        return Ok(ApiResponseHandler.Success(result, "Renewal dates repaired."));
        //    }
        //    catch (InvalidOperationException ex)
        //    {
        //        return Ok(ApiResponseHandler.Fail<string>(ex.Message));
        //    }
        //    catch (Exception ex)
        //    {
        //        return Ok(ApiResponseHandler.Fail<string>($"{ResponseMessages.Errors.ExceptionMsg} :: {ex.GetBaseException().Message}"));
        //    }
        //}
    }
}
