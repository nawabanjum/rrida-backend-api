namespace CloverBackoffice.Infrastructure.Models
{
    public enum PaymentMethodType
    {
        Card = 1,      // Credit/Debit card via Stripe
        Invoice = 2,   // Invoice with 30-day grace period
        Trial = 3      // Zero-amount marker row for trial activation (no money moves)
    }
}
