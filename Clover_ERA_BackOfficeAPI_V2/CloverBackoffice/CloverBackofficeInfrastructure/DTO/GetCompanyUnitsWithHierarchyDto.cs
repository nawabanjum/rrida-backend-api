using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class GetCompanyUnitsWithHierarchyDto
    {
        public int ProfileId { get; set; }
        public string CompanyName { get; set; }
        public TeamStructureSummaryDto Summary { get; set; }
        public List<BusinessUnitTeamDto> Units { get; set; }

        // Only populated for Team Manager or Partner Company Admin — full company structure (limited view)
        public List<BusinessUnitTeamDto>? OverallTeamStructure { get; set; }
    }

    public class TeamStructureSummaryDto
    {
        public int TotalTeams { get; set; }
        public int NeedsAttentionCount { get; set; }
        public int RespondingCount { get; set; }
        public int NoManagerCount { get; set; }
        public int NoActionHubUpdateCount { get; set; }
    }

    public class BusinessUnitTeamDto
    {
        public uint BusinessUnitId { get; set; }
        public string BusinessUnitName { get; set; }
        public string BusinessUnitShortCode { get; set; }

        // Number of people in the team, as captured on create / edit. Null for teams
        // created before the field existed.
        public int? TeamSize { get; set; }

        public string ManagerName { get; set; }
        public int? ManagerId { get; set; }
        public bool? IsReadOnly { get; set; }
        public DateTime? LastActionHubUpdatedAt { get; set; }
        public TrendChartDto TrendChart { get; set; }
        public DateTime? LastFeedbackAt { get; set; }
        public DateTime? LastResponseAt { get; set; }
        public bool HasManager { get; set; }
        public string TeamStatus { get; set; }
        public int TotalResponses { get; set; }

        // True when the team has no ACTIVE license. License-dependent metrics
        // (trend chart, last response, last action-hub update, totals) are then
        // suppressed and the UI should show an "Unlicensed" indicator instead.
        public bool IsUnlicensed { get; set; }

        // True only when this unit held a license that has ended. A unit that
        // never had one is IsUnlicensed but NOT expired. Paired with LicenseMessage
        // so the frontend can show a clear banner without re-deriving the state.
        public bool IsLicenseExpired { get; set; }

        // Human-readable license state for the UI: "License is active.",
        // "The license for this team has expired." or "No license assigned to this team."
        public string LicenseMessage { get; set; }

        // Subscription banner for the team, e.g. "Monthly - Renews August 2, 2026"
        // or "Yearly - Renews June 2, 2027". Derived from THIS team's own assigned
        // license — its cadence (yearly when the license spans ~a year, even on a
        // monthly base plan) and its own expiry date. Null when the team holds no
        // license or its subscription won't renew.
        public string? SubscriptionMessage { get; set; }

        // This unit's own license details (start/expiry dates, status, etc.).
        // Always present; License.HasLicense is false when the unit holds no
        // license.
        public UnitLicenseDto? License { get; set; }

        public List<BusinessUnitTeamDto> SubUnit { get; set; } = new();
    }

    // License information for a single business unit.
    public class UnitLicenseDto
    {
        // False when the unit currently holds no license — the remaining fields
        // are then left at their defaults (Status = "Inactive").
        public bool HasLicense { get; set; }

        public DateTime? StartDate { get; set; }         // when the license was assigned to this unit
        public DateTime? ExpiryDate { get; set; }        // license expiry / renewal date (grace honoured)

        public string Status { get; set; } = "Inactive"; // "Active" | "Inactive"
        public bool IsExpired { get; set; }
        public int? DaysRemaining { get; set; }          // days until expiry (0 when expired)

        // True when this unit holds no purchased license of its own and its
        // coverage comes from a complimentary grant (a same-manager team riding
        // another team's purchased license).
        public bool IsComplimentary { get; set; }

        // The business unit that owns the purchased license this complimentary
        // coverage rides on (Team A). Populated ONLY when IsComplimentary is
        // true; null otherwise. Lets the UI tie a complimentary team back to the
        // exact paid team, instead of guessing when a manager holds several
        // licensed teams.
        public uint? ParentBusinessUnitId { get; set; }
    }

    public class TeamManagerDto
    {
        public int Id { get; set; }
        public string FullName { get; set; }
        public UserGroup_Dto Group { get; set; }
    }

    public class UserGroup_Dto
    {
        public int UserGroupId { get; set; }
        public string UserGroupName { get; set; }
    }
    public class TeamManager
    {
        public string ManagerName { get; set; }
        public int ManagerId { get; set; }
    }
}
