namespace CloverBackoffice.Infrastructure.Models
{
    public enum SubscriptionStatus
    {
        Trial = 1,       // 14-day free trial active
        Active = 2,      // Paid and active
        PastDue = 3,     // Payment failed, grace period
        Canceled = 4,    // User canceled (may still be active until period end)
        Expired = 5,     // Trial ended without payment / subscription ended
        Paused = 6       // Temporarily paused (admin action)
    }
}
