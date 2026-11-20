using System.Globalization;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Implementation;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Services.Implementation
{
    // Plan catalogue (ServicePlans) on Assign by managerId (POST api/license-assignments).
    //
    // Every licence gives one plan: its ServicePlanId, or the legacy plan (Pro) for a licence
    // from before plans. With planId / planCode the licence used is a free one of that plan.
    //
    // Switching (Light <-> Pro): a manager holding a licence on ANOTHER plan moves to the new
    // one in the same transaction - the old licence's team assignment and grants close, its
    // holder is cleared (it is free again, still paid until the Profile Admin cancels it) -
    // so the manager never has two plans at once. Same-plan replacement keeps the old rule
    // (remove first). A switch, or any assign of a plan licence, is refused while the
    // manager runs more teams than that plan allows.
    public partial class CompanyLicenseService
    {
        private sealed record PlanRef(int Id, string Code, string Name);

        /// <summary>The plan asked for on the request; null when none. Throws when unknown.</summary>
        private async Task<PlanRef?> ResolveRequestedPlanAsync(int? planId, string? planCode)
        {
            var code = string.IsNullOrWhiteSpace(planCode) ? null : planCode.Trim().ToLowerInvariant();
            if (planId is not > 0 && code == null) return null;

            return await _db.ServicePlans.AsNoTracking()
                       .Where(p => planId > 0 ? p.Id == planId : p.Code == code)
                       .Select(p => new PlanRef(p.Id, p.Code, p.Name))
                       .FirstOrDefaultAsync()
                   ?? throw new InvalidOperationException(ResponseMessages.Errors.PlanNotFound);
        }

        private Task<int?> LegacyPlanIdAsync() =>
            _db.ServicePlans.AsNoTracking()
                .Where(p => p.Code == PlanEntitlementService.LegacyPlanCode)
                .Select(p => (int?)p.Id)
                .FirstOrDefaultAsync();

        /// <summary>The plan a licence gives: its own, else the legacy plan.</summary>
        private async Task<int?> LicensePlanIdAsync(int licenseId)
        {
            var own = await _db.CompanyLicenses.AsNoTracking()
                .Where(l => l.Id == licenseId)
                .Select(l => l.ServicePlanId)
                .FirstOrDefaultAsync();
            return own ?? await LegacyPlanIdAsync();
        }

        /// <summary>
        /// The licence to give for a requested plan: the manager's current one when it is
        /// already on that plan (idempotent; not while on trial), else a free one of the plan.
        /// </summary>
        private async Task<int> PickLicenseForPlanAsync(uint profileId, PlanRef plan, int? currentLicenseId, bool onTrial)
        {
            if (currentLicenseId.HasValue && !onTrial && await LicensePlanIdAsync(currentLicenseId.Value) == plan.Id)
                return currentLicenseId.Value;

            return await FindFreeLicenseOfPlanAsync(profileId, plan.Id, paidOnly: onTrial)
                   ?? throw new InvalidOperationException(
                       string.Format(CultureInfo.InvariantCulture, ResponseMessages.Errors.PlanLicenceNotAvailable, plan.Name));
        }

        /// <summary>
        /// Free (as FindAvailableLicenseIdAsync: live, not held by a live Team Manager, not on a
        /// live team) and on the plan. paidOnly skips trial licences.
        /// </summary>
        private async Task<int?> FindFreeLicenseOfPlanAsync(uint profileId, int planId, bool paidOnly)
        {
            var now = DateTime.UtcNow;
            var legacyId = await LegacyPlanIdAsync();
            var teamManagers = _db.TeamManagersOf((int)profileId);
            return await _db.CompanyLicenses.AsNoTracking()
                .Where(l => l.ProfileId == profileId
                            && !l.IsDel
                            && (l.ExpiryDate == null || l.ExpiryDate > now)
                            && (l.ServicePlanId == planId || (l.ServicePlanId == null && legacyId == planId))
                            && (!paidOnly || l.LicenseType != TrialLicenseType)
                            && !(l.AssignedUserId != null && teamManagers.Any(u => u.UserId == l.AssignedUserId))
                            && !_db.AssignedCompanyLicenses.Any(a =>
                                a.CompanyLicenseId == l.Id && a.IsActive && !a.IsDel
                                && a.BusinessUnit != null && a.BusinessUnit.IsDel != true))
                .OrderBy(l => l.Id)
                .Select(l => (int?)l.Id)
                .FirstOrDefaultAsync();
        }

        /// <summary>True when the two licences give different plans (a Light <-> Pro move).</summary>
        private async Task<bool> IsPlanSwitchAsync(int currentLicenseId, int newLicenseId)
        {
            var from = await LicensePlanIdAsync(currentLicenseId);
            var to = await LicensePlanIdAsync(newLicenseId);
            return from.HasValue && to.HasValue && from != to;
        }

        /// <summary>
        /// Refuses a plan licence while the manager runs more teams than its plan allows (plan
        /// limit + RRIDA Admin extensions where the plan allows them). Legacy licences are not
        /// checked here unless switching.
        /// </summary>
        private async Task EnsurePlanFitsTeamsAsync(CompanyLicense license, bool switching, int managerId, int teamCount)
        {
            if (license.ServicePlanId == null && !switching) return;
            var planId = license.ServicePlanId ?? await LegacyPlanIdAsync();
            if (planId == null) return;

            var capacity = await _db.LicenseTeamLimitAsync(license.Id);
            if (teamCount <= capacity) return;

            var who = await _db.Users.AsNoTracking().Where(u => u.UserId == managerId).Select(u => u.Name).FirstOrDefaultAsync();
            var planName = await _db.ServicePlans.AsNoTracking().Where(p => p.Id == planId).Select(p => p.Name).FirstAsync();
            throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
                ResponseMessages.Errors.PlanSwitchTooManyTeams, who ?? $"Manager {managerId}", teamCount, planName, capacity));
        }

        /// <summary>
        /// Takes a licence off its manager: grants revoked, team assignment closed, holder
        /// cleared. The licence stays live and paid - free for someone else.
        /// </summary>
        private async Task ReleaseLicenseAsync(int licenseId, int? actorUserId)
        {
            foreach (var grant in await _repo.GetActiveComplimentaryGrantsForLicenseAsync(licenseId))
                await _repo.RevokeComplimentaryGrantAsync(grant.Id, actorUserId);
            await _repo.UnassignAsync(licenseId, actorUserId);
            await _repo.SetLicenseHolderAsync(licenseId, null, actorUserId);
        }

        /// <summary>
        /// GET api/license-assignments rows: each licence's plan and price per manager, the
        /// manager's team capacity (plan default + RRIDA Admin extension) and a short
        /// entitlement summary (enabled features, Eran limits and usage this month).
        /// </summary>
        private async Task StampManagerPlansAsync(List<ManagerLicenseDto> rows)
        {
            var info = await _db.GetLicencePlanInfoAsync(
                rows.Where(r => r.CompanyLicenseId is > 0).Select(r => r.CompanyLicenseId!.Value));

            foreach (var row in rows)
            {
                row.DefaultTeamCapacity = row.TeamsLimit;
                if (row.CompanyLicenseId is not int licenseId || !info.TryGetValue(licenseId, out var p)) continue;

                // Over the licence's limit (e.g. converted to Light with 3 teams): kept, flagged.
                if (row.TeamsUsed > row.TeamsLimit)
                {
                    row.IsOverTeamLimit = true;
                    row.TeamLimitMessage = string.Format(CultureInfo.InvariantCulture,
                        ResponseMessages.Errors.PlanTeamsOverLimit, row.TeamsUsed, p.PlanName, row.TeamsLimit);
                }

                row.ServicePlanId = p.PlanId;
                row.ServicePlanCode = p.PlanCode;
                row.ServicePlanName = p.PlanName;
                row.IsLegacyPlan = p.IsLegacyPlan;
                row.PricePerManager = p.PricePerManager;

                if (_entitlements == null || !row.IsTeamManager) continue;
                var e = await _entitlements.GetForUserAsync(row.ManagerId);
                if (e == null) continue;

                row.DefaultTeamCapacity = e.TeamCapacity.DefaultCapacity;
                row.ExtendedTeamCapacity = e.TeamCapacity.ExtendedCapacity;
                row.Entitlements = Summarise(e);
            }
        }

        public static Infrastructure.DTO.PlanManagement.PlanEntitlementSummaryDto Summarise(Infrastructure.DTO.PlanManagement.PlanEntitlementsDto e)
        {
            Infrastructure.DTO.PlanManagement.UsageLimitDto Usage(string key)
            {
                var f = e.Features.FirstOrDefault(x => x.Key == key);
                return f == null
                    ? new Infrastructure.DTO.PlanManagement.UsageLimitDto { IsEnabled = !e.IsPlanGated, IsUnlimited = !e.IsPlanGated }
                    : new Infrastructure.DTO.PlanManagement.UsageLimitDto
                    {
                        IsEnabled = f.IsEnabled,
                        Limit = f.Limit,
                        IsUnlimited = f.IsUnlimited,
                        Used = f.Used ?? 0,
                        Remaining = f.Remaining
                    };
            }

            return new Infrastructure.DTO.PlanManagement.PlanEntitlementSummaryDto
            {
                EnabledFeatures = e.Features.Where(f => f.IsEnabled).Select(f => f.Key).ToList(),
                EranChats = Usage(Infrastructure.DTO.PlanManagement.PlanFeatureKeys.EranCoach),
                EranConversations = Usage(Infrastructure.DTO.PlanManagement.PlanFeatureKeys.EranConversations)
            };
        }

        private async Task StampPlanAsync(LicenseAssignmentResultDto result, int licenseId, int? replacedLicenseId)
        {
            var planId = await LicensePlanIdAsync(licenseId);
            result.ServicePlanId = planId;
            result.ServicePlanName = planId == null ? null
                : await _db.ServicePlans.AsNoTracking().Where(p => p.Id == planId).Select(p => p.Name).FirstOrDefaultAsync();
            result.PlanSwitched = replacedLicenseId.HasValue;
            result.ReplacedLicenseId = replacedLicenseId;
        }
    }
}
