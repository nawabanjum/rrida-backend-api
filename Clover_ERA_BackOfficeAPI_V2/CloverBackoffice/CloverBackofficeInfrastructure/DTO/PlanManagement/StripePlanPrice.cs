namespace CloverBackoffice.Infrastructure.DTO.PlanManagement
{
    /// <summary>
    /// The price a plan checkout charges per unit (manager), already for the billing cycle
    /// (monthly price, or the annual price as is: no multiplier). Sent to Stripe as an inline
    /// recurring price, so a price changed in the plan catalogue needs no Stripe catalogue change.
    /// </summary>
    public sealed record StripePlanPrice(decimal UnitAmount, string Currency, string ProductName);
}
