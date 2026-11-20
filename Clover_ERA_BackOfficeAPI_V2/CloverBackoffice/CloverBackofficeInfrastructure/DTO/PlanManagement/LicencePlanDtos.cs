using System.Collections.Generic;

namespace CloverBackoffice.Infrastructure.DTO.PlanManagement
{
    /// <summary>Licences of one plan in a profile: purchased (live), consumed, available.</summary>
    public class PlanLicenseCountDto
    {
        public int ServicePlanId { get; set; }
        public string PlanCode { get; set; } = string.Empty;
        public string PlanName { get; set; } = string.Empty;
        public int TotalLicenses { get; set; }
        public int ConsumedLicenses { get; set; }
        public int AvailableLicenses { get; set; }
    }

    /// <summary>
    /// What a Team Manager's plan gives them, in short, for licence rows: enabled feature keys,
    /// Eran limits with this month's usage. The full picture is GET api/entitlements?userId=.
    /// </summary>
    public class PlanEntitlementSummaryDto
    {
        public List<string> EnabledFeatures { get; set; } = new();
        public UsageLimitDto EranChats { get; set; } = new();
        public UsageLimitDto EranConversations { get; set; } = new();
    }

    public class UsageLimitDto
    {
        public bool IsEnabled { get; set; }

        /// <summary>Null with IsUnlimited.</summary>
        public int? Limit { get; set; }
        public bool IsUnlimited { get; set; }
        public int Used { get; set; }

        /// <summary>Null when unlimited.</summary>
        public int? Remaining { get; set; }
    }
}
