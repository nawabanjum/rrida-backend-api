using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace CloverBackoffice.Infrastructure.DTO
{
    // Serialized as a string in JSON ("Assign" / "Reassign" / "Remove" /
    // "AssignComplimentary") via JsonStringEnumConverter so callers don't have
    // to send integers.
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum LicenseAssignmentAction
    {
        Assign,
        Reassign,
        Remove,

        // Grant a purchased license to an additional team that shares a manager
        // with the team already holding the license (a license covers at most
        // LicenseTeamLimits.MaxTeamsPerLicense teams). Does not consume a
        // purchased slot. See ComplimentaryLicenseGrant.
        AssignComplimentary
    }

    public class AssignCompanyLicenseDto
    {
        [Required] public int CompanyLicenseId { get; set; }
        [Required] public uint BusinessUnitId { get; set; }
    }

    public class UnassignCompanyLicenseDto
    {
        [Required] public int CompanyLicenseId { get; set; }
    }

    // Request for the standalone complimentary-assignment endpoint
    // (POST /api/complimentary-license-assignments). The caller supplies ONLY
    // the target team — the server resolves which purchased license to ride on
    // by finding a same-manager sibling team that already holds one. No
    // CompanyLicenseId is needed, so complimentary coverage can be (re)granted
    // without touching the purchased assignment.
    public class AutoComplimentaryLicenseRequestDto
    {
        [Required] public uint BusinessUnitId { get; set; }
    }

    // Request for POST /api/license-assignments/cancel, /api/subscription/cancel
    // and /api/subscription/reactivate: the Team Manager whose licence should
    // stop (or start again) renewing at the end of its current period.
    // ProfileId is optional: when sent, the user must belong to it.
    public class CancelLicenseRequestDto
    {
        [Required] public int? UserID { get; set; }
        public int? ProfileId { get; set; }
    }

    // Response of POST /api/license-assignments/cancel, /api/subscription/cancel
    // and /api/subscription/reactivate.
    public class CancelLicenseResultDto
    {
        // AssignedCompanyLicenses.Id — the licence's active team assignment.
        public int LicenseAssignmentId { get; set; }
        public int CompanyLicenseId { get; set; }
        public int UserId { get; set; }

        // "ACTIVE" until PeriodEndAt, "EXPIRED" after.
        public string Status { get; set; } = string.Empty;
        public bool CancelAtPeriodEnd { get; set; }
        public DateTime? PeriodEndAt { get; set; }

        // e.g. "Cancelled · ends 9 October 2026" or "Renews 9 October 2026".
        public string RenewsLabel { get; set; } = string.Empty;
        public bool CanCancel { get; set; }
        public bool CanReactivate { get; set; }
    }

    // Request for POST /api/license/change: move a Team Manager's licence to
    // another billing cycle ("monthly" / "annual") through Stripe Checkout.
    public class LicensePlanChangeRequestDto
    {
        [Required] public int? UserId { get; set; }
        [Required] public string? BillingCycle { get; set; }
    }

    // Response of POST /api/license/change. Nothing changes until checkout
    // completes; the checkout.session.completed webhook applies the change.
    public class LicensePlanChangeResultDto
    {
        public string CheckoutUrl { get; set; } = string.Empty;
        public bool RequiresCheckout { get; set; }
    }

    // Internal resolution result: the purchased license (and the team that
    // holds it) that a target team can ride on for complimentary coverage,
    // plus the shared manager that makes them eligible.
    public class ComplimentaryLicenseSourceDto
    {
        public int CompanyLicenseId { get; set; }
        public uint SourceBusinessUnitId { get; set; }
        public int ManagerUserId { get; set; }
    }

    public class CompanyLicenseDto
    {
        public int Id { get; set; }
        public uint ProfileId { get; set; }
        public string LicenseKey { get; set; } = string.Empty;
        public string LicenseType { get; set; } = string.Empty;
        public int? CompanySubscriptionId { get; set; }
        public int? PlanId { get; set; }
        public uint? CurrentlyAssignedBusinessUnitId { get; set; }
        public string? CurrentlyAssignedBusinessUnitName { get; set; }

        // Complimentary (same-manager) coverage. The single fields hold the
        // earliest active grant and are kept for existing clients; the list holds
        // every complimentary team (up to MaxTeams - 1). Does not affect purchased
        // counts.
        public uint? ComplimentaryBusinessUnitId { get; set; }
        public string? ComplimentaryBusinessUnitName { get; set; }
        public List<ComplimentaryTeamDto> ComplimentaryTeams { get; set; } = new();

        // Teams this licence covers now (purchased team + complimentary teams),
        // the per-licence maximum, and the display form, e.g. "2 of 3".
        public int TeamsUsed { get; set; }
        public int MaxTeams { get; set; } = LicenseTeamLimits.MaxTeamsPerLicense;
        public string TeamUsage { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    // A profile's licence usage. One licence = one Team Manager, who may manage up
    // to LicenseTeamLimits.MaxTeamsPerLicense teams. A licence counts as consumed
    // once it is held by a live Team Manager or is on a live team (counted once).
    // Creating a Team Manager auto-assigns a free licence when one exists.
    public class LicenseUsage
    {
        // Live licences: not deleted, not expired.
        public int TotalLicenses { get; init; }

        // Live licences held by a live Team Manager or actively on a live team.
        public int AssignedLicenses { get; init; }

        // Team Managers of the profile (not deleted, invited ones included).
        public int TotalTeamManagers { get; init; }

        // Live teams of the profile.
        public int TotalTeams { get; init; }

        // Licences in use = licences assigned (to a Team Manager or a team).
        public int ConsumedLicenses => AssignedLicenses;

        public int AvailableLicenses => Math.Max(0, TotalLicenses - ConsumedLicenses);

        // Teams each licence allows: 3, plus any profile-level extension.
        public int TeamsPerLicense { get; init; } = LicenseTeamLimits.MaxTeamsPerLicense;

        // Extra teams from licence-level extensions on the live licences.
        public int ExtraLicenseTeams { get; init; }

        // Every licence allows one Team Manager with up to TeamsPerLicense teams
        // (3 unless a RRIDA Admin extended the profile or licence).
        public int MaxTeams => TotalLicenses * TeamsPerLicense + ExtraLicenseTeams;
    }

    // Profile-level licence availability, checked before Add User. Separate from
    // the manager licence team limit (ManagerTeamUsageDto).
    public class LicenseAvailabilityDto
    {
        public uint ProfileId { get; set; }

        // Live licences: not deleted, not expired.
        public int TotalLicenses { get; set; }

        // One licence per Team Manager; creating teams does not consume one.
        public int ConsumedLicenses { get; set; }

        public int AvailableLicenses { get; set; }

        // Same as AvailableLicenses: total - consumed, never below 0.
        public int RemainingLicenses { get; set; }

        public int TotalTeamManagers { get; set; }

        public int TotalTeams { get; set; }

        // Teams per licence (3) and the profile's team capacity (licences x 3).
        public int MaxTeamsPerLicense { get; set; } = LicenseTeamLimits.MaxTeamsPerLicense;
        public int MaxTeams { get; set; }

        // True when AvailableLicenses is 0 — the frontend shows Buy Additional Licences.
        public bool IsLicenseLimitReached { get; set; }

        // Stripe Checkout URL for one additional licence. Only set when the limit
        // is reached; null if the checkout could not be created.
        public string? BuyLicenseCheckoutUrl { get; set; }

        // The same counts per plan (Light, Pro): purchased, consumed, available.
        public List<CloverBackoffice.Infrastructure.DTO.PlanManagement.PlanLicenseCountDto> Plans { get; set; } = new();
    }

    // One Team Manager's usage of the manager licence team limit.
    public class ManagerTeamUsageDto
    {
        public int UserId { get; set; }
        public string? Name { get; set; }
        public string? Email { get; set; }
        public int TeamsUsed { get; set; }
        public int MaxTeams { get; set; } = LicenseTeamLimits.MaxTeamsPerLicense;
        public string TeamUsage { get; set; } = string.Empty;   // "2 of 3"
        public bool LimitReached { get; set; }
        public List<ManagerTeamDto> Teams { get; set; } = new();
    }

    // One Team Manager's licence row for GET /api/license-assignments
    // (1 licence = 1 Team Manager, up to 3 teams).
    public class ManagerLicenseDto
    {
        public int ManagerId { get; set; }
        public string? ManagerName { get; set; }
        public int TeamsUsed { get; set; }
        public int TeamsLimit { get; set; } = LicenseTeamLimits.MaxTeamsPerLicense;

        // "Monthly" / "Annual" / "Trial"; null when no licence is found.
        public string? Plan { get; set; }

        // yyyy-MM-dd: licence ExpiryDate, else the subscription's period end.
        public string? RenewalDate { get; set; }

        // ACTIVE / CANCELLED (cancel scheduled, live until RenewalDate) /
        // EXPIRED / NO_LICENSE.
        public string Status { get; set; } = string.Empty;

        public int? CompanyLicenseId { get; set; }
        public string? LicenseKey { get; set; }

        // False for a Portfolio Manager listed so a Team Manager licence can be
        // assigned to them (Status NO_LICENSE); assigning it makes them one.
        public bool IsTeamManager { get; set; } = true;
        public bool IsPortfolioManager { get; set; }

        // Plan catalogue (api/plans): the plan this licence gives; a licence from before plans
        // reads as Pro (IsLegacyPlan).
        public int? ServicePlanId { get; set; }
        public string? ServicePlanCode { get; set; }
        public string? ServicePlanName { get; set; }
        public bool IsLegacyPlan { get; set; }

        // Team capacity: the plan's teams and what a RRIDA Admin added (TeamsLimit = both).
        public int? DefaultTeamCapacity { get; set; }
        public int ExtendedTeamCapacity { get; set; }

        // Price per manager the licence was bought at, for its billing cycle (null for trial).
        public decimal? PricePerManager { get; set; }

        // The manager runs more teams than the licence allows (TeamsUsed > TeamsLimit). Existing
        // teams are kept; no new team can be added until under the limit. TeamLimitMessage says so.
        public bool IsOverTeamLimit { get; set; }
        public string? TeamLimitMessage { get; set; }

        // Enabled features and Eran limits / usage this month. Null with no licence.
        public CloverBackoffice.Infrastructure.DTO.PlanManagement.PlanEntitlementSummaryDto? Entitlements { get; set; }
    }

    // Request for POST /api/license-assignments/extend-team-limit (RRIDA Admin only).
    //   * ProfileId only                       → PROFILE: every Team Manager licence
    //                                            of the profile allows AdditionalTeams more.
    //   * ProfileId + ManagerId and/or
    //     CompanyLicenseId                     → LICENSE: only that manager's licence
    //                                            allows AdditionalTeams more.
    public class ExtendTeamLimitRequestDto
    {
        public uint? ProfileId { get; set; }
        public int? ManagerId { get; set; }
        public int? CompanyLicenseId { get; set; }
        public int? AdditionalTeams { get; set; }
        public string? Notes { get; set; }
    }

    // Response of POST /api/license-assignments/extend-team-limit.
    public class TeamLimitExtensionResultDto
    {
        // TeamLimitExtensions.Id — the audit row of this extension.
        public int ExtensionId { get; set; }

        // PROFILE or LICENSE.
        public string Scope { get; set; } = string.Empty;

        public uint ProfileId { get; set; }
        public int? ManagerId { get; set; }
        public string? ManagerName { get; set; }
        public int? CompanyLicenseId { get; set; }
        public string? LicenseKey { get; set; }

        public int AdditionalTeams { get; set; }

        // Teams allowed before / after: per licence for PROFILE, for the licence for LICENSE.
        public int PreviousLimit { get; set; }
        public int NewLimit { get; set; }

        // Profile picture after the extension: teams per licence, team capacity
        // (all live licences) and teams in use.
        public int TeamsPerLicense { get; set; }
        public int MaxTeams { get; set; }
        public int TotalTeams { get; set; }

        public string? Notes { get; set; }
        public int ExtendedByUserId { get; set; }
        public DateTime ExtendedAt { get; set; }

        // Licence details after the extension (same rows as GET /api/license-assignments):
        // every Team Manager for PROFILE, the extended manager's row for LICENSE.
        public List<ManagerLicenseDto> Licenses { get; set; } = new();
    }

    public class ManagerTeamDto
    {
        public uint UnitId { get; set; }
        public string? Name { get; set; }
    }

    public class ComplimentaryTeamDto
    {
        public uint BusinessUnitId { get; set; }
        public string? BusinessUnitName { get; set; }
        public int ManagerUserId { get; set; }
        public DateTime GrantedAt { get; set; }
    }

    public class CompanyLicenseAssignmentHistoryDto
    {
        public int AssignmentId { get; set; }
        public uint BusinessUnitId { get; set; }
        public string? BusinessUnitName { get; set; }
        public DateTime AssignedAt { get; set; }
        public DateTime? UnassignedAt { get; set; }
        public bool IsActive { get; set; }
    }

    // Unified lifecycle request. Action selects the operation; the other
    // fields are required per the table below.
    //
    //   Action    | Required fields            | Behavior
    //   ----------+----------------------------+----------------------------
    //   Assign    | ManagerId,                 | Gives the licence to a Team
    //   (manager) | CompanyLicenseId optional  | Manager (1 licence = 1 TM, up
    //             |                            | to 3 teams). Without a licence
    //             |                            | id the manager's current one,
    //             |                            | else a free one, is used.
    //             |                            | Example: { "action": "Assign",
    //             |                            |   "companyLicenseId": 93,
    //             |                            |   "managerId": 263 }
    //   Assign    | CompanyLicenseId,          | Pins license to the unit.
    //             | BusinessUnitId             | Idempotent when already on
    //             |                            | that unit. Rejects when
    //             |                            | already on a different
    //             |                            | unit — use Reassign instead.
    //   Reassign  | CompanyLicenseId,          | Atomic from→to move. From
    //             | FromBusinessUnitId,        | must match current active
    //             | ToBusinessUnitId           | assignee, otherwise reject.
    //   Remove    | CompanyLicenseId OR        | Closes the active assignment.
    //             | BusinessUnitId             | License id wins if both sent.
    //             |                            | Also revokes a complimentary
    //             |                            | grant when BusinessUnitId is a
    //             |                            | complimentary beneficiary, and
    //             |                            | cascades to any dependent grant
    //             |                            | when the primary is removed.
    //   AssignComp-| CompanyLicenseId,         | Grants the (already purchased-
    //   limentary  | BusinessUnitId (Team B)   | assigned) license to an extra
    //             |                            | team (max 3 teams per license)
    //             |                            | that shares a manager with
    //             |                            | the primary team. No purchased
    //             |                            | slot consumed.
    public class LicenseAssignmentRequestDto
    {
        [Required] public LicenseAssignmentAction Action { get; set; }
        public int? CompanyLicenseId { get; set; }

        // Assign only: the Team Manager (Users.UserId) receiving the licence.
        // When sent, it is used instead of BusinessUnitId.
        public int? ManagerId { get; set; }

        // Assign by managerId only: the plan (Light, Pro) of the licence to give. A free
        // licence of that plan is used; a manager holding a licence on another plan moves to
        // it (the old licence is unassigned and free again). Send planId or planCode.
        public int? PlanId { get; set; }
        public string? PlanCode { get; set; }

        public uint? BusinessUnitId { get; set; }
        public uint? FromBusinessUnitId { get; set; }
        public uint? ToBusinessUnitId { get; set; }
    }

    // Response for lifecycle ops. Snapshots the license + its new active
    // assignment (null on unassign) so the client doesn't need a follow-up
    // GET to refresh state.
    public class LicenseAssignmentResultDto
    {
        public int CompanyLicenseId { get; set; }
        public string LicenseKey { get; set; } = string.Empty;
        public uint ProfileId { get; set; }
        public uint? CurrentBusinessUnitId { get; set; }
        public string? CurrentBusinessUnitName { get; set; }
        public DateTime? AssignedAt { get; set; }
        public DateTime? PreviousUnassignedAt { get; set; }
        public uint? PreviousBusinessUnitId { get; set; }
        public string Operation { get; set; } = string.Empty; // Assign | Reassign | Remove | AssignComplimentary

        // Populated only for AssignComplimentary results. IsComplimentary
        // distinguishes a free grant from a purchased assignment in audit/UX.
        public bool IsComplimentary { get; set; }
        public uint? ComplimentaryBusinessUnitId { get; set; }
        public string? ComplimentaryBusinessUnitName { get; set; }
        public int? ManagerUserId { get; set; }

        // Assign by managerId: true when the user was a Portfolio Manager and
        // this call also made them a Team Manager.
        public bool TeamManagerRoleAssigned { get; set; }

        // Assign by managerId: true when the manager held no licence before this
        // call and was given a free one.
        public bool LicenseNewlyAssigned { get; set; }

        // Assign by managerId: true when the manager was on a trial licence and
        // this call ended that trial and gave them the purchased licence.
        public bool TrialConverted { get; set; }

        // The trial licence ended by this call (only when TrialConverted).
        public int? TerminatedTrialLicenseId { get; set; }

        // Plan catalogue plan the assigned licence gives (Light, Pro); a licence from before
        // plans reads as Pro.
        public int? ServicePlanId { get; set; }
        public string? ServicePlanName { get; set; }

        // Assign by managerId: true when the manager moved to a licence on another plan
        // (Light <-> Pro); their previous licence was unassigned and is free again.
        public bool PlanSwitched { get; set; }
        public int? ReplacedLicenseId { get; set; }

        // Plan switch: the replaced licence's billing was cancelled at the end of its paid
        // period (no refund, no renewal). False with a message when it could not be (it then
        // stays renewing until cancelled with POST license-assignments/cancel); false without
        // one when there was nothing to cancel (trial, or not billed by Stripe).
        public bool ReplacedLicenseCancelled { get; set; }
        public string? ReplacedLicenseCancelMessage { get; set; }

        // Licence team usage after the operation, e.g. "3 of 3".
        public int TeamsUsed { get; set; }
        public int MaxTeams { get; set; } = LicenseTeamLimits.MaxTeamsPerLicense;
        public string TeamUsage { get; set; } = string.Empty;
    }
}
