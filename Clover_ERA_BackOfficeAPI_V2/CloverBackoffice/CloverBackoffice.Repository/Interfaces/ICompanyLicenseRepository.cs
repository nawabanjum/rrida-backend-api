using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;

namespace CloverBackoffice.Repository.Interfaces
{
    public interface ICompanyLicenseRepository
    {
        Task<CompanyLicense?> GetByIdAsync(int id);
        Task<List<CompanyLicense>> GetByCompanyAsync(uint profileId);
        Task<AssignedCompanyLicense?> GetActiveAssignmentAsync(int companyLicenseId);
        Task<List<AssignedCompanyLicense>> GetAssignmentHistoryAsync(int companyLicenseId);
        Task<bool> BusinessUnitExistsForCompanyAsync(uint profileId, uint businessUnitId);

        Task<AssignedCompanyLicense> AssignAsync(int companyLicenseId, uint businessUnitId, int? actorUserId);
        Task<bool> UnassignAsync(int companyLicenseId, int? actorUserId);

        // Atomic from→to move. Verifies the active row matches fromBusinessUnitId
        // before closing it and opening the new one. Throws InvalidOperationException
        // when the source doesn't match — the caller must refresh and retry.
        Task<AssignedCompanyLicense> ReassignAsync(int companyLicenseId, uint fromBusinessUnitId, uint toBusinessUnitId, int? actorUserId);

        // Resolve the license currently active on a unit (null if none).
        Task<AssignedCompanyLicense?> GetActiveAssignmentForUnitAsync(uint businessUnitId);

        // A license row of the company that is not held by a live Team Manager
        // and not on a live team, or null. Used for team coverage only — whether a
        // licence is AVAILABLE is decided by GetLicenseUsageAsync.
        Task<int?> FindAvailableLicenseIdAsync(uint profileId);

        // Licence usage: consumed = live licences held by a live Team Manager or
        // actively on a live team, each counted once. See LicenseUsage.
        Task<LicenseUsage> GetLicenseUsageAsync(uint profileId);

        // Live licences per plan catalogue plan (Light, Pro): purchased, consumed, available.
        Task<List<CloverBackoffice.Infrastructure.DTO.PlanManagement.PlanLicenseCountDto>> GetPlanLicenseCountsAsync(uint profileId);

        // The live licence a Team Manager holds (stamped with them, else on one of
        // their teams), or null.
        Task<int?> FindUserLicenseIdAsync(int userId);

        // Gives a Team Manager exactly one licence (their own, else a free one)
        // under the per-profile licence lock. Null when the user is not a Team
        // Manager of the profile or no licence is free.
        // planCode: the plan the admin chose (Light, Pro); null = the only free plan, none when mixed.
        Task<int?> AssignLicenseToUserAsync(uint profileId, int userId, int? actorUserId, string? planCode = null);

        // Stamps (userId) or clears (null) a licence's Team Manager holder.
        Task SetLicenseHolderAsync(int companyLicenseId, int? userId, int? actorUserId);

        // A free purchased (non-trial) licence of the profile, or null. Same
        // "free" rule as FindAvailableLicenseIdAsync.
        Task<int?> FindAvailablePaidLicenseIdAsync(uint profileId);

        // Ends a trial licence because its Team Manager was given a purchased
        // licence: team assignment closed, grants revoked, holder cleared, row
        // expired now and marked TerminatedAt / ReplacedByLicenseId. Call inside
        // the caller's transaction.
        Task TerminateTrialLicenseAsync(int trialLicenseId, int replacedByLicenseId, int? actorUserId);

        // Gives a team that has no live licence its coverage row, so licence-gated
        // reads (dashboards) see it. Does not consume a licence. Preference:
        //   1. complimentary grant from the licence of a team with the same manager;
        //   2. the licence the team's manager holds with no team yet;
        //   3. a free licence row (only when allowFreeLicense).
        // Returns false when neither is possible. Runs under the per-profile
        // licence lock; joins the caller's transaction when one is open.
        Task<bool> EnsureTeamCoverageAsync(uint businessUnitId, int? actorUserId, bool allowFreeLicense = true);

        // True when the unit holds a live license, purchased or complimentary.
        Task<bool> UnitHasLiveLicenseAsync(uint businessUnitId);

        // ---------- complimentary (same-manager) grants ----------

        // The active complimentary grants on a license, oldest first. A license
        // has at most LicenseTeamLimits.MaxComplimentaryTeamsPerLicense new grants.
        Task<List<ComplimentaryLicenseGrant>> GetActiveComplimentaryGrantsForLicenseAsync(int companyLicenseId);

        // The active grant that gives a unit complimentary coverage (null if the
        // unit is not a complimentary beneficiary).
        Task<ComplimentaryLicenseGrant?> GetActiveComplimentaryGrantForUnitAsync(uint businessUnitId);

        // All active complimentary grants across a set of licenses — used by read
        // paths to surface complimentary coverage without N+1 lookups.
        Task<List<ComplimentaryLicenseGrant>> GetActiveComplimentaryGrantsForLicensesAsync(IEnumerable<int> companyLicenseIds);

        // All active complimentary grants owned by a company (resolved via the
        // backing license's ProfileId). Feeds the subscription/team read paths so
        // complimentary beneficiaries are surfaced in one query.
        Task<List<ComplimentaryLicenseGrant>> GetActiveComplimentaryGrantsByCompanyAsync(uint profileId);

        // A user actively assigned (UserTeams, IsUnAssigned=false) to BOTH units,
        // or null when the two units share no manager. Used to enforce the
        // same-manager rule and to record ManagerUserId on the grant.
        Task<int?> GetSharedManagerUserIdAsync(uint unitA, uint unitB);

        // True when the unit has at least one active manager (a user assigned via
        // UserTeams with IsUnAssigned=false). Lets the standalone complimentary
        // endpoint tell "team has no manager" apart from "no same-manager sibling
        // owns a purchased license" when it can't resolve a source.
        Task<bool> UnitHasActiveManagerAsync(uint businessUnitId);

        // Resolves a purchased license the target unit can ride on for
        // complimentary coverage: a DIFFERENT team that (a) shares an active
        // manager with the target and (b) holds an active purchased assignment on
        // a license that still has a free team slot (the teams-per-license
        // limit). Returns null when no such source exists. Prefers
        // the most-recently-assigned shared manager/team so the pick matches the
        // team-hierarchy UI.
        Task<ComplimentaryLicenseSourceDto?> FindPurchasedComplimentarySourceForUnitAsync(uint targetUnitId);

        // Inserts a complimentary grant + audit row inside a transaction. Locks
        // the license and re-counts its active grants first, so concurrent
        // requests cannot push it past the teams-per-license limit.
        // Throws InvalidOperationException when the license is full.
        Task<ComplimentaryLicenseGrant> AddComplimentaryGrantAsync(
            int companyLicenseId, uint primaryBusinessUnitId, uint complimentaryBusinessUnitId,
            int managerUserId, int? actorUserId);

        // Closes an active complimentary grant + writes an audit row. Returns
        // false when there was nothing active to revoke.
        Task<bool> RevokeComplimentaryGrantAsync(int grantId, int? actorUserId);

        // Revokes every active grant that involves `unitId` (as primary OR
        // beneficiary) whose two teams no longer share a manager. Called right
        // after a manager assign/unassign so stale complimentary coverage is
        // dropped immediately. Returns the number of grants revoked.
        Task<int> RevokeStaleGrantsForUnitAsync(uint unitId, int? actorUserId);

        // Safety-net sweep: revokes ALL active grants whose two teams no longer
        // share a manager. Backs up the event-driven path (catches direct DB
        // edits or any manager-mutation path that skipped reconciliation).
        // Returns the number of grants revoked.
        Task<int> RevokeAllStaleGrantsAsync(int? actorUserId);
    }
}
