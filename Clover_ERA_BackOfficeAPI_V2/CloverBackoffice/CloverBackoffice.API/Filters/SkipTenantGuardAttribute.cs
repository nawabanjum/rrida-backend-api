using System;

namespace CloverBackoffice.API.Filters
{
    /// <summary>
    /// Opts an action (or a whole controller) out of <see cref="CompanyTenantGuardFilter"/>.
    /// Use ONLY where a company id in the request genuinely does not identify a
    /// tenant the caller must already belong to — e.g. registration flows that
    /// create a brand-new company, or provider webhooks. Every use is a hole in
    /// tenant isolation, so state the reason in a comment next to the attribute.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public sealed class SkipTenantGuardAttribute : Attribute
    {
    }
}
