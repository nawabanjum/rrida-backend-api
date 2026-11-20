using System.Text.Json.Serialization;

namespace CloverBackoffice.Infrastructure.Models
{
    // Bind tolerantly from request bodies: a client may send the cycle as a JSON
    // number (1 / 2), a numeric string ("1" / "2"), or the name ("Monthly" /
    // "Annual"). The default System.Text.Json enum binder ONLY accepts a raw
    // number, so a string-encoded value (how JS <select>/enum dropdowns emit it)
    // failed to bind — leaving BillingCycle at its 0 default and, via the
    // interval ternary, silently billing MONTHLY regardless of the value sent.
    // JsonStringEnumConverter accepts all three forms. EF Core column storage is
    // unaffected (it maps the enum to int through its own value conversion, not
    // System.Text.Json).
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum BillingCycle
    {
        Monthly = 1,
        Annual = 2
    }
}
