using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace CloverBackoffice.Services.Implementation
{
    public class StripeService : IStripeService
    {
        private readonly StripeSettings _settings;
        private readonly ILogger<StripeService> _logger;

        public StripeService(IOptions<StripeSettings> settings, ILogger<StripeService> logger)
        {
            _settings = settings.Value;
            _logger = logger;
            StripeConfiguration.ApiKey = _settings.SecretKey;
        }

        public async Task<string> GetOrCreateCustomerAsync(uint profileId, string email, string name)
        {
            var service = new CustomerService();

            // Reuse an existing Stripe customer ONLY when it belongs to THIS
            // company (metadata.profileId match). Matching on email alone made
            // two companies that share an email (common in testing) collapse
            // onto ONE Stripe customer; webhook payment resolution then attributed
            // that customer's payments to the wrong company (e.g. company 195's
            // checkout writing Payment rows under company 227). Search by email,
            // then require the profileId metadata to match before reusing.
            var existing = await service.ListAsync(new CustomerListOptions
            {
                Email = email,
                Limit = 100
            });

            // With an EnvironmentTag the customer must also belong to THIS
            // environment: Dev and Pre-Prod can share a Stripe account and hold
            // the same profile id, and another environment's customer would route
            // our payments to its webhook.
            var match = existing.Data.FirstOrDefault(c =>
                c.Metadata != null &&
                c.Metadata.TryGetValue("companyId", out var cid) &&
                cid == profileId.ToString() &&
                IsThisEnvironment(c.Metadata));
            if (match != null)
                return match.Id;

            // No customer for THIS company yet — create a dedicated one so every
            // company maps 1:1 to its own Stripe customer and payments can never
            // cross-attribute.
            var metadata = new Dictionary<string, string>
            {
                { "companyId", profileId.ToString() }
            };
            AddEnvironmentTag(metadata);

            var customer = await service.CreateAsync(new CustomerCreateOptions
            {
                Email = email,
                Name = name,
                Metadata = metadata
            });

            return customer.Id;
        }

        public async Task<string> CreateCheckoutSessionAsync(string customerId, string priceId, int trialDays, string successUrl, string cancelUrl, uint profileId, int planId)
        {
            var sessionService = new SessionService();

            var options = new SessionCreateOptions
            {
                Customer = customerId,
                Mode = "subscription",
                LineItems = new List<SessionLineItemOptions>
                {
                    new SessionLineItemOptions
                    {
                        Price = priceId,
                        Quantity = 1
                    }
                },
                SubscriptionData = trialDays > 0
                    ? new SessionSubscriptionDataOptions { TrialPeriodDays = trialDays }
                    : null,
                SuccessUrl = successUrl,
                CancelUrl = cancelUrl,
                // Nothing is discounted server-side here, so the customer always
                // gets the "Add promotion code" field for Dashboard-managed codes.
                AllowPromotionCodes = true,
                Metadata = new Dictionary<string, string>
                {
                    { "companyId", profileId.ToString() },
                    { "planId", planId.ToString() }
                }
            };

            var session = await sessionService.CreateAsync(options);
            return session.Url;
        }

        // Hours an ad-hoc promo coupon stays redeemable. Matches Stripe's own
        // 24h Checkout Session expiry, so a coupon can never outlive the session
        // it was minted for and be reused from a stale link.
        private const int CouponRedeemWindowHours = 24;

        public async Task<string> CreateDynamicCheckoutSessionAsync(
            string customerId, int teamCount, decimal pricePerTeamCents,
            BillingCycle billingCycle, int trialDays,
            string successUrl, string cancelUrl, uint profileId,
            decimal? discountPercent = null,
            bool isLifetimeDiscount = false,
            string? promoCodeText = null,
            int? promoCodeId = null,
            IDictionary<string, string>? extraMetadata = null,
            CloverBackoffice.Infrastructure.DTO.PlanManagement.StripePlanPrice? planPrice = null)
        {
            var sessionService = new SessionService();
            await EnsureCustomerTaggedAsync(customerId);

            var interval = billingCycle == BillingCycle.Monthly ? "month" : "year";
            // For annual: charge yearly at the monthly rate × AnnualPriceMultiplier
            // per team (e.g. ×12 → $295 → $3540/yr). Kept in config so the add-on
            // flow charges the identical yearly amount.
            var unitAmount = billingCycle == BillingCycle.Monthly
                ? (long)(pricePerTeamCents * 100)                              // $295 → 29500 cents
                : (long)(pricePerTeamCents * _settings.AnnualPriceMultiplier * 100); // $295 × 12 = $3540 → 354000 cents

            // Plan checkout (plan catalogue): the plan's tier price, inline, for the cycle as is
            // (no multiplier). Otherwise a configured catalog price wins, else an inline price.
            var priceId = ConfiguredPriceId(billingCycle);
            var lineItem = planPrice != null
                ? new SessionLineItemOptions
                {
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = planPrice.Currency.ToLowerInvariant(),
                        UnitAmount = (long)decimal.Round(planPrice.UnitAmount * 100, 0, MidpointRounding.AwayFromZero),
                        Recurring = new SessionLineItemPriceDataRecurringOptions { Interval = interval },
                        ProductData = new SessionLineItemPriceDataProductDataOptions { Name = planPrice.ProductName }
                    },
                    Quantity = teamCount
                }
                : !string.IsNullOrWhiteSpace(priceId)
                ? new SessionLineItemOptions
                {
                    Price = priceId,
                    Quantity = teamCount
                }
                : new SessionLineItemOptions
                {
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = "usd",
                        UnitAmount = unitAmount,
                        Recurring = new SessionLineItemPriceDataRecurringOptions
                        {
                            Interval = interval
                        },
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = $"CloverERA Subscription - {teamCount} Team(s)"
                        }
                    },
                    Quantity = teamCount
                };

            var options = new SessionCreateOptions
            {
                Customer = customerId,
                Mode = "subscription",
                LineItems = new List<SessionLineItemOptions> { lineItem },
                SubscriptionData = trialDays > 0
                    ? new SessionSubscriptionDataOptions { TrialPeriodDays = trialDays }
                    : null,
                SuccessUrl = successUrl,
                CancelUrl = cancelUrl,
                Metadata = new Dictionary<string, string>
                {
                    { "companyId", profileId.ToString() },
                    { "teamCount", teamCount.ToString() },
                    // Purchased cycle flows back on checkout.session.completed so
                    // the webhook stamps the sub's BillingCycle + period end (and
                    // thus license expiry) to match what was actually bought,
                    // instead of defaulting to Monthly.
                    { "billingCycle", ((int)billingCycle).ToString() }
                }
            };

            // The promo code id rides on the session so the webhook can record
            // the redemption against whichever CompanySubscription it ends up
            // creating. Under subscription-per-purchase the row doesn't exist
            // yet at this point, so the sub's own PromoCodeId can't carry it.
            if (promoCodeId.HasValue)
                options.Metadata["promoCodeId"] = promoCodeId.Value.ToString();

            // The tag goes on the subscription too, so its later events
            // (customer.subscription.*) can be told apart by environment.
            if (!string.IsNullOrWhiteSpace(_settings.EnvironmentTag))
            {
                AddEnvironmentTag(options.Metadata);
                options.SubscriptionData ??= new SessionSubscriptionDataOptions();
                options.SubscriptionData.Metadata ??= new Dictionary<string, string>();
                AddEnvironmentTag(options.SubscriptionData.Metadata);
            }

            if (extraMetadata != null && extraMetadata.Count > 0)
            {
                options.SubscriptionData ??= new SessionSubscriptionDataOptions();
                options.SubscriptionData.Metadata ??= new Dictionary<string, string>();
                foreach (var kv in extraMetadata)
                {
                    options.Metadata[kv.Key] = kv.Value;
                    options.SubscriptionData.Metadata[kv.Key] = kv.Value;
                }
            }

            // Apply promo code discount as a Stripe Coupon.
            //
            // NOTE the price above is the FULL amount — the discount is never
            // subtracted client-side. Stripe resolves this coupon and computes
            // amount_subtotal / total_details.amount_discount / amount_total
            // itself, which is what the hosted page renders and what the webhook
            // reads back. Stripe is the sole arithmetic authority.
            if (discountPercent.HasValue && discountPercent.Value > 0)
            {
                // "20" not "20.00" — this string is customer-facing on the
                // Stripe checkout page.
                var label = discountPercent.Value.ToString("0.##");

                var couponService = new CouponService();
                var coupon = await couponService.CreateAsync(new CouponCreateOptions
                {
                    PercentOff = discountPercent.Value,
                    // forever → every invoice on the subscription this coupon
                    // lands on. once → its first invoice only.
                    Duration = isLifetimeDiscount ? "forever" : "once",
                    Name = isLifetimeDiscount
                        ? $"Lifetime {label}% off"
                        : $"Promo {label}% off",
                    // One checkout, one redemption, short window. Without these
                    // every abandoned session leaves a permanently redeemable
                    // coupon behind in the Stripe account.
                    MaxRedemptions = 1,
                    RedeemBy = DateTime.UtcNow.AddHours(CouponRedeemWindowHours),
                    Metadata = new Dictionary<string, string>
                    {
                        { "companyId", profileId.ToString() },
                        { "promoCode", promoCodeText ?? string.Empty },
                        { "isLifetime", isLifetimeDiscount ? "true" : "false" }
                    }
                });

                options.Discounts = new List<SessionDiscountOptions>
                {
                    new SessionDiscountOptions { Coupon = coupon.Id }
                };
            }
            else
            {
                // No app-side promo on this checkout, so hand the field to the
                // customer: the hosted page renders "Add promotion code" and any
                // active promotion code from the Stripe Dashboard can be typed in.
                // Entering one is optional — an empty field checks out at full price.
                //
                // Stripe rejects a session that sets both `discounts` and
                // `allow_promotion_codes` ("You may only specify one of these
                // parameters"), which is why this is an else and not
                // unconditional. When our own promo already discounted the
                // session the customer cannot stack a second code on top.
                options.AllowPromotionCodes = true;
            }

            var session = await sessionService.CreateAsync(options);
            return session.Url;
        }

        // One-time payment session (mode=payment, no recurrence). Used by
        // /api/subscription/purchase-additional-teams to charge a flat amount
        // for N extra licenses. Metadata flows back unchanged on the
        // checkout.session.completed webhook so the handler can mint the
        // matching CompanyLicense rows.
        public async Task<string> CreateOneTimePaymentSessionAsync(
            string customerId, long unitAmountCents, string productName,
            string successUrl, string cancelUrl,
            IDictionary<string, string> metadata)
        {
            var sessionService = new SessionService();
            await EnsureCustomerTaggedAsync(customerId);
            var options = new SessionCreateOptions
            {
                Customer = customerId,
                Mode = "payment",
                LineItems = new List<SessionLineItemOptions>
                {
                    new SessionLineItemOptions
                    {
                        PriceData = new SessionLineItemPriceDataOptions
                        {
                            Currency = "usd",
                            UnitAmount = unitAmountCents,
                            ProductData = new SessionLineItemPriceDataProductDataOptions
                            {
                                Name = productName
                            }
                        },
                        Quantity = 1
                    }
                },
                SuccessUrl = successUrl,
                CancelUrl = cancelUrl,
                AllowPromotionCodes = true,
                Metadata = new Dictionary<string, string>(metadata)
            };
            AddEnvironmentTag(options.Metadata);
            var session = await sessionService.CreateAsync(options);
            return session.Url;
        }

        public async Task UpdateSubscriptionPriceAsync(
            string stripeSubscriptionId, int newTeamCount,
            decimal pricePerTeamCents, BillingCycle billingCycle)
        {
            var service = new Stripe.SubscriptionService();
            var subscription = await service.GetAsync(stripeSubscriptionId);
            var itemId = subscription.Items.Data[0].Id;

            var interval = billingCycle == BillingCycle.Monthly ? "month" : "year";
            var unitAmount = billingCycle == BillingCycle.Monthly
                ? (long)(pricePerTeamCents * 100)
                : (long)(pricePerTeamCents * _settings.AnnualPriceMultiplier * 100);

            var newPriceId = ConfiguredPriceId(billingCycle);
            if (string.IsNullOrWhiteSpace(newPriceId))
                newPriceId = (await CreateActivePrice(unitAmount, interval, newTeamCount)).Id;

            await service.UpdateAsync(stripeSubscriptionId, new SubscriptionUpdateOptions
            {
                Items = new List<SubscriptionItemOptions>
                {
                    new SubscriptionItemOptions
                    {
                        Id = itemId,
                        Price = newPriceId,
                        Quantity = newTeamCount
                    }
                },
                ProrationBehavior = "create_prorations"
            });
        }

        public async Task UpdateSubscriptionPriceNoProrateAsync(
            string stripeSubscriptionId, int newTeamCount,
            decimal pricePerTeamCents, BillingCycle billingCycle)
        {
            var service = new Stripe.SubscriptionService();
            var subscription = await service.GetAsync(stripeSubscriptionId);
            var itemId = subscription.Items.Data[0].Id;

            var interval = billingCycle == BillingCycle.Monthly ? "month" : "year";
            var unitAmount = billingCycle == BillingCycle.Monthly
                ? (long)(pricePerTeamCents * 100)
                : (long)(pricePerTeamCents * _settings.AnnualPriceMultiplier * 100);

            var newPriceId = ConfiguredPriceId(billingCycle);
            if (string.IsNullOrWhiteSpace(newPriceId))
                newPriceId = (await CreateActivePrice(unitAmount, interval, newTeamCount)).Id;

            await service.UpdateAsync(stripeSubscriptionId, new SubscriptionUpdateOptions
            {
                Items = new List<SubscriptionItemOptions>
                {
                    new SubscriptionItemOptions
                    {
                        Id = itemId,
                        Price = newPriceId,
                        Quantity = newTeamCount
                    }
                },
                ProrationBehavior = "none"
            });
        }

        // Names the promotion code a customer typed on the hosted checkout page,
        // so the redemption can be attributed on the Payment row instead of
        // showing up as an unexplained discount.
        //
        // Two round-trips, both unavoidable:
        //
        //   1. The Session on a webhook payload carries only total_details'
        //      amounts — the per-discount `breakdown` is an expandable field and
        //      arrives absent, so the session has to be re-fetched asking for it.
        //   2. The breakdown yields a promo_… id, not the code text, so the
        //      PromotionCode itself is fetched to read `code`.
        //
        // Expanding straight to the code (total_details.breakdown.discounts.
        // discount.promotion_code) would exceed Stripe's 4-level expansion depth,
        // hence the second call rather than a deeper Expand.
        //
        // Returns null when the session carried no promotion code — including the
        // case where OUR own ad-hoc coupon applied the discount, since a bare
        // coupon has no promotion code attached. Best-effort by contract: a blank
        // id or any Stripe error yields null, because the caller is a webhook
        // handler and a webhook must not fail over promo bookkeeping.
        public async Task<string?> GetSessionPromotionCodeAsync(string? sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                return null;

            try
            {
                var full = await new SessionService().GetAsync(sessionId, new SessionGetOptions
                {
                    Expand = new List<string> { "total_details.breakdown" }
                });

                // Checkout permits at most one discount per session, so the first
                // breakdown entry carrying a promotion code is the one applied.
                var promoId = full?.TotalDetails?.Breakdown?.Discounts?
                    .Select(d => d.Discount?.PromotionCodeId)
                    .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));

                if (string.IsNullOrWhiteSpace(promoId))
                    return null;

                var promo = await new Stripe.PromotionCodeService().GetAsync(promoId);
                return promo?.Code;
            }
            catch (StripeException ex)
            {
                _logger.LogWarning(ex,
                    "GetSessionPromotionCodeAsync - could not resolve the promotion code on session {SessionId}; the discount will be recorded without a code.",
                    sessionId);
                return null;
            }
        }

        // Metadata key marking the environment that owns a Stripe object.
        public const string EnvironmentMetadataKey = "rridaEnv";

        private string? ConfiguredPriceId(BillingCycle billingCycle)
        {
            var id = billingCycle == BillingCycle.Annual ? _settings.AnnualPriceId : _settings.MonthlyPriceId;
            return string.IsNullOrWhiteSpace(id) ? null : id.Trim();
        }

        private void AddEnvironmentTag(IDictionary<string, string> metadata)
        {
            if (!string.IsNullOrWhiteSpace(_settings.EnvironmentTag))
                metadata[EnvironmentMetadataKey] = _settings.EnvironmentTag;
        }

        // True when no tag is configured, or the metadata carries our tag.
        private bool IsThisEnvironment(IDictionary<string, string>? metadata)
        {
            if (string.IsNullOrWhiteSpace(_settings.EnvironmentTag))
                return true;
            return metadata != null &&
                   metadata.TryGetValue(EnvironmentMetadataKey, out var tag) &&
                   tag == _settings.EnvironmentTag;
        }

        // Add-on, renewal and plan-change checkouts reuse the customer id stored
        // on the subscription, which may predate EnvironmentTag. Tag it now so
        // its invoice events (resolved via the customer) reach this environment.
        // Never retags a customer that already belongs to another environment.
        private async Task EnsureCustomerTaggedAsync(string customerId)
        {
            if (string.IsNullOrWhiteSpace(_settings.EnvironmentTag) || string.IsNullOrWhiteSpace(customerId))
                return;

            try
            {
                var service = new CustomerService();
                var customer = await service.GetAsync(customerId);
                if (customer?.Metadata != null &&
                    customer.Metadata.TryGetValue(EnvironmentMetadataKey, out var tag))
                {
                    if (tag != _settings.EnvironmentTag)
                        _logger.LogWarning(
                            "Stripe customer {CustomerId} is tagged {Tag}, not {Expected}; its invoice events will be ignored here.",
                            customerId, tag, _settings.EnvironmentTag);
                    return;
                }

                var metadata = new Dictionary<string, string>();
                AddEnvironmentTag(metadata);
                await service.UpdateAsync(customerId, new CustomerUpdateOptions { Metadata = metadata });
            }
            catch (StripeException ex)
            {
                _logger.LogWarning(ex, "EnsureCustomerTaggedAsync failed for {CustomerId}", customerId);
            }
        }

        public async Task<string?> GetCustomerEnvironmentTagAsync(string? customerId)
        {
            if (string.IsNullOrEmpty(customerId))
                return null;

            try
            {
                var customer = await new CustomerService().GetAsync(customerId);
                return customer?.Metadata != null &&
                       customer.Metadata.TryGetValue(EnvironmentMetadataKey, out var tag)
                    ? tag
                    : null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GetCustomerEnvironmentTagAsync failed for {CustomerId}", customerId);
                return null;
            }
        }

        public async Task<string?> GetInvoiceCustomerIdAsync(string? invoiceId)
        {
            if (string.IsNullOrEmpty(invoiceId))
                return null;

            try
            {
                return (await new Stripe.InvoiceService().GetAsync(invoiceId))?.CustomerId;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GetInvoiceCustomerIdAsync failed for {InvoiceId}", invoiceId);
                return null;
            }
        }

        private static async Task<Price> CreateActivePrice(long unitAmount, string interval, int teamCount)
        {
            // Create a new active product + price to avoid "inactive product" errors
            var productService = new ProductService();
            var product = await productService.CreateAsync(new ProductCreateOptions
            {
                Name = $"CloverERA Subscription - {teamCount} Team(s)",
                Active = true
            });

            var priceService = new PriceService();
            return await priceService.CreateAsync(new PriceCreateOptions
            {
                Currency = "usd",
                UnitAmount = unitAmount,
                Recurring = new PriceRecurringOptions { Interval = interval },
                Product = product.Id
            });
        }

        public async Task UpdateSubscriptionAsync(string stripeSubscriptionId, string newPriceId)
        {
            var service = new Stripe.SubscriptionService();
            var subscription = await service.GetAsync(stripeSubscriptionId);

            var itemId = subscription.Items.Data[0].Id;

            await service.UpdateAsync(stripeSubscriptionId, new SubscriptionUpdateOptions
            {
                Items = new List<SubscriptionItemOptions>
                {
                    new SubscriptionItemOptions
                    {
                        Id = itemId,
                        Price = newPriceId
                    }
                },
                ProrationBehavior = "create_prorations"
            });
        }

        public async Task CancelSubscriptionAsync(string stripeSubscriptionId, bool atPeriodEnd = true)
        {
            var service = new Stripe.SubscriptionService();
            await service.UpdateAsync(stripeSubscriptionId, new SubscriptionUpdateOptions
            {
                CancelAtPeriodEnd = atPeriodEnd
            });
        }

        // Clears cancel_at_period_end. With a quantity, the seat count is set in
        // the same update (no proration), so both change together or not at all.
        public async Task ReactivateSubscriptionAsync(string stripeSubscriptionId, long? quantity = null)
        {
            var service = new Stripe.SubscriptionService();
            var options = new SubscriptionUpdateOptions { CancelAtPeriodEnd = false };

            if (quantity.HasValue)
            {
                var subscription = await service.GetAsync(stripeSubscriptionId);
                options.Items = new List<SubscriptionItemOptions>
                {
                    new SubscriptionItemOptions
                    {
                        Id = subscription.Items.Data[0].Id,
                        Quantity = quantity.Value
                    }
                };
                options.ProrationBehavior = "none";
            }

            await service.UpdateAsync(stripeSubscriptionId, options);
        }

        public async Task<long> GetSubscriptionQuantityAsync(string stripeSubscriptionId)
        {
            var subscription = await new Stripe.SubscriptionService().GetAsync(stripeSubscriptionId);
            return subscription.Items?.Data?.FirstOrDefault()?.Quantity ?? 0;
        }

        public async Task SetSubscriptionQuantityAsync(string stripeSubscriptionId, long quantity, bool prorate = false)
        {
            var service = new Stripe.SubscriptionService();
            var subscription = await service.GetAsync(stripeSubscriptionId);

            await service.UpdateAsync(stripeSubscriptionId, new SubscriptionUpdateOptions
            {
                Items = new List<SubscriptionItemOptions>
                {
                    new SubscriptionItemOptions
                    {
                        Id = subscription.Items.Data[0].Id,
                        Quantity = quantity
                    }
                },
                ProrationBehavior = prorate ? "create_prorations" : "none"
            });
        }

        public async Task CancelSubscriptionNowAsync(string stripeSubscriptionId, bool prorate)
        {
            await new Stripe.SubscriptionService().CancelAsync(stripeSubscriptionId, new SubscriptionCancelOptions
            {
                Prorate = prorate,
                // Bill the proration straight away so the credit lands on the
                // customer's balance instead of waiting on a pending item.
                InvoiceNow = prorate
            });
        }

        public async Task<List<StripeCheckoutSessionInfo>> ListOpenCheckoutSessionsAsync(string customerId)
        {
            if (string.IsNullOrEmpty(customerId))
                return new List<StripeCheckoutSessionInfo>();

            var sessions = await new SessionService().ListAsync(new SessionListOptions
            {
                Customer = customerId,
                Status = "open",
                Limit = 100
            });

            return sessions.Data
                .Select(s => new StripeCheckoutSessionInfo
                {
                    Id = s.Id,
                    PaymentStatus = s.PaymentStatus,
                    Status = s.Status,
                    AmountTotal = s.AmountTotal,
                    Currency = s.Currency,
                    Url = s.Url,
                    Metadata = s.Metadata != null
                        ? new Dictionary<string, string>(s.Metadata)
                        : new Dictionary<string, string>()
                })
                .ToList();
        }

        public async Task ExpireCheckoutSessionAsync(string sessionId)
        {
            await new SessionService().ExpireAsync(sessionId);
        }

        // Fetches the card's last 4 digits and brand from a PaymentIntent's
        // charge. Card data never reaches our server (Stripe Checkout hosts the
        // form), so we read it back post-payment from payment_method_details.
        // Returns (null, null) if unavailable (non-card method, missing PI).
        public async Task<CardDetails> GetCardDetailsAsync(string? paymentIntentId)
        {
            if (string.IsNullOrEmpty(paymentIntentId))
                return CardDetails.Empty;

            try
            {
                var service = new PaymentIntentService();
                var pi = await service.GetAsync(paymentIntentId, new PaymentIntentGetOptions
                {
                    Expand = new List<string> { "latest_charge" }
                });

                return FromCharge(pi.LatestCharge);
            }
            catch
            {
                // Best-effort: a stale/invalid PaymentIntent id must not break
                // the webhook handler. Card details are non-critical metadata.
                return CardDetails.Empty;
            }
        }

        // Map a Charge's card (the card actually swiped) into CardDetails. The
        // charge also carries the payment-method id we persist. Returns Empty
        // when the charge has no card (e.g. non-card method).
        private static CardDetails FromCharge(Stripe.Charge? charge)
        {
            var card = charge?.PaymentMethodDetails?.Card;
            if (card == null) return CardDetails.Empty;
            return new CardDetails
            {
                CardholderName = charge!.BillingDetails?.Name,
                Last4 = card.Last4,
                Brand = card.Brand,
                ExpMonth = card.ExpMonth > 0 ? (int?)card.ExpMonth : null,
                ExpYear = card.ExpYear > 0 ? (int?)card.ExpYear : null,
                Funding = card.Funding,
                Country = card.Country,
                PaymentMethodId = charge.PaymentMethod
            };
        }

        // Map a stored/attached PaymentMethod (subscription or customer default,
        // or a saved card) into CardDetails.
        private static CardDetails FromPaymentMethod(Stripe.PaymentMethod? pm)
        {
            var card = pm?.Card;
            if (card == null) return CardDetails.Empty;
            return new CardDetails
            {
                CardholderName = pm!.BillingDetails?.Name,
                Last4 = card.Last4,
                Brand = card.Brand,
                ExpMonth = card.ExpMonth > 0 ? (int?)card.ExpMonth : null,
                ExpYear = card.ExpYear > 0 ? (int?)card.ExpYear : null,
                Funding = card.Funding,
                Country = card.Country,
                PaymentMethodId = pm.Id
            };
        }

        // Authoritative "which card was actually used" resolver. Tries the most
        // precise source first and falls through to the card on file. Every
        // source is wrapped so one failing Stripe call can't break the webhook,
        // and the source that succeeded (or the final miss) is logged so the
        // live environment shows exactly where the card came from.
        //
        // Order:
        //   1. PaymentIntent → latest charge (the exact charge for this payment).
        //   2. The customer's charges, matched to this invoice when known, else
        //      the most recent paid charge. charge.payment_method_details.card is
        //      the real card swiped and is unaffected by the Basil API dropping
        //      Invoice.payment_intent / Invoice.charge.
        //   3. Subscription default payment method.
        //   4. Customer default payment method, then the customer's saved card.
        public async Task<CardDetails> GetCardUsedAsync(
            string? paymentIntentId, string? invoiceId, string? subscriptionId, string? customerId)
        {
            // 1. PaymentIntent → latest charge.
            if (!string.IsNullOrEmpty(paymentIntentId))
            {
                var details = await GetCardDetailsAsync(paymentIntentId);
                if (details.HasCard)
                {
                    _logger.LogInformation("Card resolved from PaymentIntent {PaymentIntentId}: {Brand} ****{Last4}", paymentIntentId, details.Brand, details.Last4);
                    return details;
                }
            }

            // 2. The actual charge on the customer (the card that was swiped).
            if (!string.IsNullOrEmpty(customerId))
            {
                try
                {
                    var charges = await new ChargeService().ListAsync(new ChargeListOptions
                    {
                        Customer = customerId,
                        Limit = 10
                    });

                    var charge = (!string.IsNullOrEmpty(invoiceId)
                            ? charges.Data.FirstOrDefault(c => c.InvoiceId == invoiceId && c.PaymentMethodDetails?.Card != null)
                            : null)
                        ?? charges.Data.FirstOrDefault(c => c.Paid && c.PaymentMethodDetails?.Card != null)
                        ?? charges.Data.FirstOrDefault(c => c.PaymentMethodDetails?.Card != null);

                    var details = FromCharge(charge);
                    if (details.HasCard)
                    {
                        _logger.LogInformation("Card resolved from Charge {ChargeId} (invoice {InvoiceId}): {Brand} ****{Last4}", charge!.Id, charge.InvoiceId, details.Brand, details.Last4);
                        return details;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "GetCardUsedAsync - charge lookup failed for customer {CustomerId}", customerId);
                }
            }

            // 3. Subscription's default payment method.
            if (!string.IsNullOrEmpty(subscriptionId))
            {
                try
                {
                    var sub = await new Stripe.SubscriptionService().GetAsync(subscriptionId, new SubscriptionGetOptions
                    {
                        Expand = new List<string> { "default_payment_method" }
                    });

                    var details = FromPaymentMethod(sub.DefaultPaymentMethod);
                    if (details.HasCard)
                    {
                        _logger.LogInformation("Card resolved from Subscription {SubscriptionId} default payment method: {Brand} ****{Last4}", subscriptionId, details.Brand, details.Last4);
                        return details;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "GetCardUsedAsync - subscription PM lookup failed for {SubscriptionId}", subscriptionId);
                }
            }

            // 4. Customer's default payment method, then any saved card.
            if (!string.IsNullOrEmpty(customerId))
            {
                try
                {
                    var customer = await new CustomerService().GetAsync(customerId, new CustomerGetOptions
                    {
                        Expand = new List<string> { "invoice_settings.default_payment_method" }
                    });

                    var details = FromPaymentMethod(customer.InvoiceSettings?.DefaultPaymentMethod);
                    if (details.HasCard)
                    {
                        _logger.LogInformation("Card resolved from Customer {CustomerId} default payment method: {Brand} ****{Last4}", customerId, details.Brand, details.Last4);
                        return details;
                    }

                    var pms = await new PaymentMethodService().ListAsync(new PaymentMethodListOptions
                    {
                        Customer = customerId,
                        Type = "card",
                        Limit = 1
                    });
                    var savedDetails = FromPaymentMethod(pms.Data.FirstOrDefault());
                    if (savedDetails.HasCard)
                    {
                        _logger.LogInformation("Card resolved from Customer {CustomerId} saved payment method: {Brand} ****{Last4}", customerId, savedDetails.Brand, savedDetails.Last4);
                        return savedDetails;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "GetCardUsedAsync - customer PM lookup failed for {CustomerId}", customerId);
                }
            }

            _logger.LogWarning(
                "GetCardUsedAsync - NO card found. paymentIntentId={PaymentIntentId}, invoiceId={InvoiceId}, subscriptionId={SubscriptionId}, customerId={CustomerId}",
                paymentIntentId, invoiceId, subscriptionId, customerId);
            return CardDetails.Empty;
        }

        // Returns the Stripe Product ID backing a subscription's first line
        // item (e.g. prod_UbutUvwjA3w8cB), or null if it can't be resolved.
        public async Task<string?> GetProductIdForSubscriptionAsync(string? stripeSubscriptionId)
        {
            if (string.IsNullOrEmpty(stripeSubscriptionId))
                return null;

            var service = new Stripe.SubscriptionService();
            var sub = await service.GetAsync(stripeSubscriptionId, new SubscriptionGetOptions
            {
                Expand = new List<string> { "items.data.price" }
            });

            return sub.Items?.Data?.FirstOrDefault()?.Price?.ProductId;
        }

        // Maps the subscription's actual recurring interval to our BillingCycle.
        // This is the source of truth for what Stripe will charge on renewal, so
        // the webhook can set BillingCycle correctly even if the checkout
        // metadata is missing. Best-effort: any failure returns null so the
        // caller can fall back.
        public async Task<BillingCycle?> GetSubscriptionBillingCycleAsync(string? stripeSubscriptionId)
        {
            if (string.IsNullOrEmpty(stripeSubscriptionId))
                return null;

            try
            {
                var service = new Stripe.SubscriptionService();
                var sub = await service.GetAsync(stripeSubscriptionId, new SubscriptionGetOptions
                {
                    Expand = new List<string> { "items.data.price" }
                });

                var interval = sub.Items?.Data?.FirstOrDefault()?.Price?.Recurring?.Interval;
                return interval switch
                {
                    "year" => BillingCycle.Annual,
                    "month" => BillingCycle.Monthly,
                    _ => (BillingCycle?)null
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GetSubscriptionBillingCycleAsync failed for {SubId}", stripeSubscriptionId);
                return null;
            }
        }

        public object ConstructWebhookEvent(string json, string signature, string secret)
        {
            return EventUtility.ConstructEvent(json, signature, secret, throwOnApiVersionMismatch: false);
        }

        // Lists a customer's completed one-time "addLicenses" checkout sessions
        // that were actually paid. Used by the reconcile/backfill path to mint
        // licenses for charges whose checkout.session.completed webhook never
        // succeeded (e.g. a signature / API-version failure window). Capped at
        // Stripe's max page size of 100 — a company won't exceed that many
        // top-up purchases in practice.
        public async Task<List<StripeCheckoutSessionInfo>> ListPaidAddLicenseSessionsAsync(string customerId)
        {
            if (string.IsNullOrEmpty(customerId))
                return new List<StripeCheckoutSessionInfo>();

            var sessionService = new SessionService();
            var sessions = await sessionService.ListAsync(new SessionListOptions
            {
                Customer = customerId,
                Limit = 100
            });

            return sessions.Data
                .Where(s => s.Metadata != null
                         && s.Metadata.TryGetValue("op", out var op)
                         && string.Equals(op, "addLicenses", StringComparison.OrdinalIgnoreCase)
                         && string.Equals(s.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase))
                .Select(s => new StripeCheckoutSessionInfo
                {
                    Id = s.Id,
                    PaymentIntentId = s.PaymentIntentId,
                    InvoiceId = s.InvoiceId,
                    PaymentStatus = s.PaymentStatus,
                    Status = s.Status,
                    AmountTotal = s.AmountTotal,
                    Currency = s.Currency,
                    Metadata = s.Metadata != null
                        ? new Dictionary<string, string>(s.Metadata)
                        : new Dictionary<string, string>()
                })
                .ToList();
        }

        // Reads profileId from the Stripe customer's metadata (stamped by
        // GetOrCreateCustomerAsync). Best-effort: any failure returns null so a
        // webhook fallback that calls this never throws.
        public async Task<uint?> GetProfileIdForCustomerAsync(string? customerId)
        {
            if (string.IsNullOrEmpty(customerId))
                return null;

            try
            {
                var customer = await new CustomerService().GetAsync(customerId);
                if (customer?.Metadata != null &&
                    customer.Metadata.TryGetValue("companyId", out var cid) &&
                    uint.TryParse(cid, out var profileId))
                    return profileId;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GetCompanyIdForCustomerAsync failed for {CustomerId}", customerId);
            }
            return null;
        }
    }
}
