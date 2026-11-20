using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO.PlanManagement;
using CloverBackoffice.Infrastructure.Models.PlanManagement;
using CloverBackoffice.Repository.Implementation;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Services.Implementation
{
    // Plan versions: existing customers keep the terms they bought.
    //
    // Every change to a plan's terms (price tiers, feature values, extension rule, currency,
    // pricing unit) saves a new ServicePlanVersion with the terms frozen as JSON; a change that
    // leaves the terms as they were saves none. New purchases get the newest version; a licence
    // keeps its version until an admin migrates it (POST api/admin/plans/{id}/migrate-licences).
    public partial class PlanManagementService
    {
        /// <summary>
        /// Adds the plan's next version when its (tracked, tiers + features loaded) terms differ
        /// from the newest version. The caller saves.
        /// </summary>
        private async Task AddVersionIfChangedAsync(ServicePlan plan, int actorUserId, string reason, CancellationToken ct)
        {
            var keys = await _context.PlanFeatures.AsNoTracking().ToDictionaryAsync(f => f.Id, f => f.Key, ct);
            var terms = PlanTermsQueries.TermsOf(plan, keys).ToJson();

            var current = plan.Id == 0 ? null : await _context.CurrentVersionAsync(plan.Id, ct);
            if (current != null && current.Terms == terms) return;
            if (current != null && PlanTerms.FromJson(current.Terms).ToJson() == terms) return;

            _context.ServicePlanVersions.Add(new ServicePlanVersion
            {
                ServicePlan = plan,
                VersionNumber = (current?.VersionNumber ?? 0) + 1,
                Terms = terms,
                Reason = reason,
                CreatedBy = actorUserId,
                CreatedAt = Now
            });
        }

        public async Task<PlanManagementResult<PlanVersionListDto>> GetPlanVersionsAsync(int planId, int actorUserId, CancellationToken ct = default)
        {
            if (!await IsRridaAdminAsync(actorUserId, ct)) return Forbidden<PlanVersionListDto>();
            var plan = await _context.ServicePlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == planId, ct);
            if (plan == null) return Fail<PlanVersionListDto>(PlanManagementError.NotFound, ResponseMessages.Errors.PlanNotFound);

            var versions = await _context.ServicePlanVersions.AsNoTracking()
                .Where(v => v.ServicePlanId == planId)
                .OrderByDescending(v => v.VersionNumber)
                .ToListAsync(ct);
            var counts = await LicenceCountsByVersionAsync(plan, ct);

            var newest = versions.FirstOrDefault()?.VersionNumber ?? 0;
            return PlanManagementResult<PlanVersionListDto>.Ok(new PlanVersionListDto
            {
                PlanId = planId,
                CurrentVersionNumber = newest,
                Versions = versions.Select(v => new PlanVersionDto
                {
                    VersionId = v.Id,
                    VersionNumber = v.VersionNumber,
                    IsCurrent = v.VersionNumber == newest,
                    Reason = v.Reason,
                    CreatedBy = v.CreatedBy,
                    CreatedAt = v.CreatedAt,
                    LicenceCount = counts.GetValueOrDefault(v.VersionNumber),
                    Terms = PlanTerms.FromJson(v.Terms)
                }).ToList()
            });
        }

        /// <summary>
        /// Moves licences of the plan to its newest version (their customers get the plan's
        /// current terms). fromVersionNumber limits it to licences on that version; profileId to
        /// one company. Licences from before plans (Pro) are included for the Pro plan. The
        /// licences' subscriptions move too, so renewals and new seats follow.
        /// </summary>
        public async Task<PlanManagementResult<PlanMigrationResultDto>> MigrateLicencesAsync(int planId, MigratePlanLicencesDto? dto, int actorUserId, CancellationToken ct = default)
        {
            if (!await IsRridaAdminAsync(actorUserId, ct)) return Forbidden<PlanMigrationResultDto>();
            var reason = Clean(dto?.Reason);
            if (TooLong(reason, ServicePlanAuditLog.ReasonMaxLength))
                return BadRequest<PlanMigrationResultDto>(ResponseMessages.Errors.PlanReasonTooLong);

            var plan = await _context.ServicePlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == planId, ct);
            if (plan == null) return Fail<PlanMigrationResultDto>(PlanManagementError.NotFound, ResponseMessages.Errors.PlanNotFound);
            var target = await _context.CurrentVersionAsync(planId, ct);
            if (target == null) return BadRequest<PlanMigrationResultDto>(ResponseMessages.Errors.PlanHasNoVersion);

            var isLegacy = plan.Code == Infrastructure.DTO.LicenseTeamLimits.LegacyPlanCode;
            var q = _context.CompanyLicenses
                .Where(l => !l.IsDel && (l.ServicePlanId == planId || (isLegacy && l.ServicePlanId == null))
                            && (l.ServicePlanVersionId == null || l.ServicePlanVersionId != target.Id));
            if (dto?.ProfileId is > 0) q = q.Where(l => l.ProfileId == dto.ProfileId);
            var licences = await q.ToListAsync(ct);

            if (dto?.FromVersionNumber is int from)
            {
                var fromVersion = await _context.ServicePlanVersions.AsNoTracking()
                    .Where(v => v.ServicePlanId == planId && v.VersionNumber == from)
                    .Select(v => (int?)v.Id)
                    .FirstOrDefaultAsync(ct);
                if (fromVersion == null) return BadRequest<PlanMigrationResultDto>(ResponseMessages.Errors.PlanVersionNotFound);
                licences = licences.Where(l => l.ServicePlanVersionId == fromVersion || (from == 1 && l.ServicePlanVersionId == null)).ToList();
            }

            var now = Now;
            foreach (var l in licences)
            {
                l.ServicePlanId = planId;
                l.ServicePlanVersionId = target.Id;
                l.UpdatedBy = actorUserId;
                l.UpdatedAt = now;
            }

            var subIds = licences.Where(l => l.CompanySubscriptionId != null).Select(l => l.CompanySubscriptionId!.Value).Distinct().ToList();
            var subs = await _context.CompanySubscriptions.Where(s => subIds.Contains(s.Id)).ToListAsync(ct);
            foreach (var s in subs)
            {
                s.ServicePlanId = planId;
                s.ServicePlanVersionId = target.Id;
                s.UpdatedAt = now;
            }

            Audit(ServicePlanAuditActions.LicencesMigrated, actorUserId, planId, null,
                $"{{\"fromVersionNumber\":{(dto?.FromVersionNumber?.ToString() ?? "null")},\"profileId\":{(dto?.ProfileId?.ToString() ?? "null")}}}",
                $"{{\"toVersionNumber\":{target.VersionNumber},\"licences\":{licences.Count},\"subscriptions\":{subs.Count}}}",
                reason);
            await _context.SaveChangesAsync(ct);

            return PlanManagementResult<PlanMigrationResultDto>.Ok(new PlanMigrationResultDto
            {
                PlanId = planId,
                ToVersionNumber = target.VersionNumber,
                LicencesMigrated = licences.Count,
                SubscriptionsMigrated = subs.Count
            });
        }

        /// <summary>Live licences per version number of the plan (no version = version 1).</summary>
        private async Task<Dictionary<int, int>> LicenceCountsByVersionAsync(ServicePlan plan, CancellationToken ct)
        {
            var isLegacy = plan.Code == Infrastructure.DTO.LicenseTeamLimits.LegacyPlanCode;
            var now = DateTime.UtcNow;
            var rows = await _context.CompanyLicenses.AsNoTracking()
                .Where(l => !l.IsDel && (l.ExpiryDate == null || l.ExpiryDate > now)
                            && (l.ServicePlanId == plan.Id || (isLegacy && l.ServicePlanId == null)))
                .GroupBy(l => l.ServicePlanVersionId)
                .Select(g => new { VersionId = g.Key, Count = g.Count() })
                .ToListAsync(ct);
            var numbers = await _context.ServicePlanVersions.AsNoTracking()
                .Where(v => v.ServicePlanId == plan.Id)
                .ToDictionaryAsync(v => v.Id, v => v.VersionNumber, ct);

            var result = new Dictionary<int, int>();
            foreach (var r in rows)
            {
                var number = r.VersionId != null && numbers.TryGetValue(r.VersionId.Value, out var n) ? n : 1;
                result[number] = result.GetValueOrDefault(number) + r.Count;
            }
            return result;
        }
    }
}
