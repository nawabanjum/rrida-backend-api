using CloverBackoffice.Infrastructure.DTO;

namespace CloverBackoffice.Services.Interfaces
{
    public interface ICompanyLicenseService
    {
        Task<List<CompanyLicenseDto>> GetByCompanyAsync(uint profileId);

        // Licence details of a profile for the caller, one row per Team Manager:
        // teams used, plan, renewal date and status. Throws
        // UnauthorizedAccessException for another profile.
        Task<List<ManagerLicenseDto>> GetLicenseDetailsAsync(uint profileId, int? actorUserId);

        // Team usage of the profile's Team Managers against the manager licence
        // team limit, e.g. "2 of 3". userId narrows it to one manager.
        Task<List<ManagerTeamUsageDto>> GetManagerTeamUsageAsync(uint profileId, int? userId, int? actorUserId);

        // RRIDA Admin only: raise the team limit of a profile (every licence) or of
        // one manager's licence by AdditionalTeams, and audit it (TeamLimitExtensions).
        // UnauthorizedAccessException for any other caller; InvalidOperationException
        // for an invalid request.
        Task<TeamLimitExtensionResultDto> ExtendTeamLimitAsync(ExtendTeamLimitRequestDto dto, int actorUserId);
        Task<List<CompanyLicenseAssignmentHistoryDto>> GetHistoryAsync(int companyLicenseId);
        Task<CompanyLicenseDto> AssignAsync(AssignCompanyLicenseDto dto, int? actorUserId);
        Task<bool> UnassignAsync(int companyLicenseId, int? actorUserId);

        // Release every license tied to a unit (called when a team is deleted):
        // closes its purchased assignment so the slot frees up for reassignment,
        // and revokes any complimentary grant the unit benefits from or provides.
        // No-op when the unit holds nothing. Returns the number of releases done.
        Task<int> ReleaseLicensesForUnitAsync(uint unitId, int? actorUserId);

        // Clears the user as holder of every licence stamped with them. Team
        // coverage is ReleaseLicensesForUnitAsync's. Returns the number cleared.
        Task<int> ReleaseLicensesHeldByUserAsync(int userId, int? actorUserId);

        // Single-entry lifecycle dispatcher used by /api/license-assignments.
        // Action ∈ { "Assign", "Reassign", "Remove", "AssignComplimentary" }.
        Task<LicenseAssignmentResultDto> ExecuteLifecycleAsync(LicenseAssignmentRequestDto dto, int? actorUserId);

        // Takes back a Portfolio Manager -> Team Manager promotion made by
        // Assign-by-managerId when the step after it (a team move) fails.
        Task RevertTeamManagerPromotionAsync(int userId, int companyLicenseId, bool releaseLicense, int? actorUserId);

        // Standalone complimentary assignment used by
        // /api/complimentary-license-assignments. Given ONLY the target team, it
        // finds a same-manager sibling team that already holds an active
        // purchased license and grants the target complimentary coverage from it.
        // Independent of the purchased assign flow, so complimentary coverage can
        // be re-granted after it was removed without re-touching the purchased
        // license. Throws InvalidOperationException with a specific message when
        // the team has no manager or no eligible purchased license is found.
        Task<LicenseAssignmentResultDto> AutoAssignComplimentaryAsync(uint targetUnitId, int? actorUserId);

        // Re-evaluate complimentary grants touching a unit and revoke any whose
        // two teams no longer share a manager. Call after a manager assign/
        // unassign. Returns the number of grants revoked.
        Task<int> ReconcileGrantsForUnitAsync(uint unitId, int? actorUserId);

        // Safety-net sweep across all active grants. Returns the number revoked.
        Task<int> ReconcileAllStaleGrantsAsync(int? actorUserId);

        // A license row of the company not held by a Team Manager and not on a
        // live team, or null. Coverage only — availability is GetLicenseUsageAsync.
        Task<int?> FindAvailableLicenseIdAsync(uint profileId);

        // Licence usage: total, assigned (consumed) and available licences.
        Task<LicenseUsage> GetLicenseUsageAsync(uint profileId);

        // Auto-assigns one licence to a new Team Manager: the one they already
        // hold, else a free one. Null when the user is not a Team Manager (no
        // licence needed) or none is free (user is still created, unlicensed).
        // planCode: the plan the admin chose (Light, Pro); null = the only free plan, none when mixed.
        Task<int?> AssignLicenseToUserAsync(uint profileId, int userId, int? actorUserId, string? planCode = null);

        // Gives an uncovered team its licence coverage row without consuming a
        // licence. See ICompanyLicenseRepository.EnsureTeamCoverageAsync.
        Task<bool> EnsureTeamCoverageAsync(uint unitId, int? actorUserId, bool allowFreeLicense = true);

        // True when the unit already holds a live license, purchased or complimentary.
        Task<bool> UnitHasLiveLicenseAsync(uint unitId);
    }
}
