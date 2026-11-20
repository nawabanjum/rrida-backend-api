namespace CloverBackoffice.Infrastructure.Models
{
    public enum PaymentStatus
    {
        Pending = 1,              // Payment initiated, not yet confirmed
        Succeeded = 2,            // Payment successful
        Failed = 3,               // Payment failed
        Refunded = 4,             // Payment refunded
        AwaitingVerification = 5  // Invoice sent, waiting for Clover ERA Admin to verify
    }
}
