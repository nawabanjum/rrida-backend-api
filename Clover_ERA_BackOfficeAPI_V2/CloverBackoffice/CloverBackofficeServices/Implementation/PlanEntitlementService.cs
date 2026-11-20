using CloverBackoffice.Components.Enums;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.DTO.PlanManagement;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Infrastructure.Models.PlanManagement;
using CloverBackoffice.Repository.Implementation;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Services.Implementation
{
    // What a user's plan gives them, read from the plan catalogue. Read-only: nothing is
    // enforced from here yet.
    //
    // Who is gated: only a user with the Team Manager role. RRIDA Admin, Profile Admin and
    // Portfolio Manager without it are never limited by a plan (every feature allowed).
    //
    // Which licence: the live licence (not deleted, not expired) assigned to the manager
    // (HELD); else a live licence on one of their teams, purchased or complimentary (TEAM).
    //
    // Which plan: the licence's ServicePlanId. A licence minted before plans existed has none
    // and is read as the legacy plan (code "pro": every existing licence was sold at the Pro
    // price). A Team Manager with no live licence is not limited by a plan (as before plans).
    //
    // Usage (UTC calendar month): ERAN_COACH = chat messages the user sent, ERAN_CONVERSATIONS
    // = conversations the user started; MANAGERS_AND_TEAMS = teams the manager holds now.
    //
    // Team capacity: the plan's MANAGERS_AND_TEAMS limit (3 when the plan has no value for it,
    // as today) plus RRIDA Admin extensions (profile-wide + this licence) when the plan allows
    // extension.
    public class PlanEntitlementService : IPlanEntitlementService
    {
        public const string LegacyPlanCode = LicenseTeamLimits.LegacyPlanCode;
        public const string HeldSource = "HELD";
        public const string TeamSource = "TEAM";

        public const string TeamsFeatureKey = LicenseTeamLimits.TeamsFeatureKey;
        public const string EranChatsFeatureKey = "ERAN_COACH";
        public const string EranConversationsFeatureKey = "ERAN_CONVERSATIONS";

        private readonly ApplicationDbContext _context;
        private readonly IUserRoleAssignmentService _roles;
        private readonly TimeProvider _clock;

        public PlanEntitlementService(ApplicationDbContext context, IUserRoleAssignmentService roles, TimeProvider? clock = null)
        {
            _context = context;
            _roles = roles;
            _clock = clock ?? TimeProvider.System;
        }

        private DateTime Now => _clock.GetUtcNow().UtcDateTime;

        // ── Access-checked read ──────────────────────────────────────────────

        public async Task<PlanManagementResult<PlanEntitlementsDto>> GetForUserAsync(int userId, int actorUserId, CancellationToken ct = default)
        {
            var target = await _context.Users.AsNoTracking()
                .Where(u => u.UserId == userId && !u.IsDel)
                .Select(u => new { u.ProfileId })
                .FirstOrDefaultAsync(ct);
            if (target == null)
                return PlanManagementResult<PlanEntitlementsDto>.Fail(PlanManagementError.NotFound, ResponseMessages.Errors.EntitlementUserNotFound);

            if (actorUserId != userId)
            {
                var actorRoles = await _roles.GetRoleIdsAsync(actorUserId, ct);
                var allowed = actorRoles.Contains((int)UserGroups.RridaAdmin);
                if (!allowed && actorRoles.Contains((int)UserGroups.ProfileAdmin))
                {
                    var actorProfile = await _context.Users.AsNoTracking()
                        .Where(u => u.UserId == actorUserId && !u.IsDel)
                        .Select(u => u.ProfileId)
                        .FirstOrDefaultAsync(ct);
                    allowed = actorProfile is > 0 && actorProfile == target.ProfileId;
                }
                if (!allowed)
                    return PlanManagementResult<PlanEntitlementsDto>.Fail(PlanManagementError.Forbidden, ResponseMessages.Errors.EntitlementDenied);
            }

            return PlanManagementResult<PlanEntitlementsDto>.Ok((await GetForUserAsync(userId, ct))!);
        }

        // ── Checks for later enforcement ─────────────────────────────────────

        public async Task<bool> HasFeatureAsync(int userId, string featureKey, CancellationToken ct = default)
        {
            var e = await GetForUserAsync(userId, ct);
            if (e == null) return false;
            if (!e.IsPlanGated) return true;
            var f = e.Features.FirstOrDefault(x => string.Equals(x.Key, featureKey, StringComparison.OrdinalIgnoreCase));
            return f is { IsEnabled: true } && (f.Remaining == null || f.Remaining > 0);
        }

        public async Task<FeatureEntitlementDto?> GetFeatureAsync(int userId, string featureKey, CancellationToken ct = default)
        {
            var e = await GetForUserAsync(userId, ct);
            return e?.Features.FirstOrDefault(x => string.Equals(x.Key, featureKey, StringComparison.OrdinalIgnoreCase));
        }

        // ── Resolution ───────────────────────────────────────────────────────

        public async Task<PlanEntitlementsDto?> GetForUserAsync(int userId, CancellationToken ct = default)
        {
            var user = await _context.Users.AsNoTracking()
                .Where(u => u.UserId == userId && !u.IsDel)
                .Select(u => new { u.UserId, u.ProfileId })
                .FirstOrDefaultAsync(ct);
            if (user == null) return null;

            var now = Now;
            var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var features = await _context.PlanFeatures.AsNoTracking()
                .Where(f => f.IsActive)
                .OrderBy(f => f.SortOrder).ThenBy(f => f.Id)
                .ToListAsync(ct);

            var dto = new PlanEntitlementsDto { UserId = userId, ProfileId = user.ProfileId, UsagePeriodStart = monthStart };

            var roles = await _roles.GetRoleIdsAsync(userId, ct);
            if (!roles.Contains((int)UserGroups.TeamManager))
            {
                dto.IsPlanGated = false;
                dto.Features = features.Select(Ungated).ToList();
                return dto;
            }

            var (license, source) = await FindLicenseAsync(userId, now, ct);
            if (license != null)
            {
                dto.LicenseId = license.Id;
                dto.LicenseKey = license.LicenseKey;
                dto.LicenseType = license.LicenseType;
                dto.LicenseExpiryDate = license.ExpiryDate;
                dto.LicenseSource = source;
            }

            // The terms the licence was bought on (its plan version; none = version 1), so a
            // later edit of the plan never changes what this customer gets until migrated.
            var plan = license == null ? null
                : (await _context.GetLicenceTermsAsync(new[] { license.Id }, ct)).GetValueOrDefault(license.Id);
            if (plan != null)
            {
                dto.PlanId = plan.PlanId;
                dto.PlanCode = plan.PlanCode;
                dto.PlanName = plan.PlanName;
                dto.PlanVersionNumber = plan.VersionNumber;
                dto.IsLegacyPlanDefault = plan.IsLegacyPlan;
            }

            var teamsUsed = await _context.CountManagerTeamsAsync(userId, 0, ct);

            // No live licence (expired, a licence-exempt team, a manager not yet given one) or
            // no catalogue plan for it: not limited by a plan, exactly as before plans existed.
            // Their team capacity is still what the team checks allow.
            if (plan == null)
            {
                dto.IsPlanGated = false;
                dto.Features = features.Select(Ungated).ToList();
                dto.TeamCapacity = await TeamCapacityAsync(null, false, null, user.ProfileId, teamsUsed, ct);
                return dto;
            }

            dto.IsPlanGated = true;
            var cells = plan.Terms.Features
                .GroupBy(f => f.FeatureId)
                .ToDictionary(g => g.Key, g =>
                {
                    var f = g.First();
                    return new ServicePlanFeature
                    {
                        PlanFeatureId = f.FeatureId, IsIncluded = f.IsIncluded, LimitValue = f.LimitValue,
                        DisplayText = f.DisplayText, SubText = f.SubText
                    };
                });
            var usage = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                [TeamsFeatureKey] = teamsUsed,
                [EranChatsFeatureKey] = await _context.EranCoachMessages.AsNoTracking()
                    .CountAsync(m => m.Role == "user" && m.CreatedAt >= monthStart
                                     && _context.EranCoachConversations.Any(c => c.Id == m.ConversationId && c.UserId == userId), ct),
                [EranConversationsFeatureKey] = await _context.EranCoachConversations.AsNoTracking()
                    .CountAsync(c => c.UserId == userId && c.CreatedAt >= monthStart, ct)
            };

            dto.Features = features.Select(f => Gated(f, cells.GetValueOrDefault(f.Id), usage)).ToList();

            var teamsFeature = features.FirstOrDefault(f => f.Key == TeamsFeatureKey);
            var teamsCell = teamsFeature == null ? null : cells.GetValueOrDefault(teamsFeature.Id);
            dto.TeamCapacity = await TeamCapacityAsync(teamsCell, plan.Terms.AllowsTeamLimitExtension, license, user.ProfileId, teamsUsed, ct);
            if (dto.TeamCapacity.TotalCapacity is int cap && teamsUsed > cap)
            {
                // Over the plan's limit (e.g. converted to Light with 3 teams): teams are kept,
                // new ones are refused by the team checks. Said, never silently trimmed.
                dto.TeamCapacity.IsOverLimit = true;
                dto.TeamCapacity.Message = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    ResponseMessages.Errors.PlanTeamsOverLimit, teamsUsed, plan.PlanName, cap);
            }

            // The teams row reports the same capacity the team checks enforce (extensions included).
            var teamsRow = dto.Features.FirstOrDefault(f => f.Key == TeamsFeatureKey);
            if (teamsRow != null)
            {
                teamsRow.IsEnabled = dto.TeamCapacity.TotalCapacity is null or > 0;
                teamsRow.Limit = dto.TeamCapacity.TotalCapacity;
                teamsRow.IsUnlimited = dto.TeamCapacity.TotalCapacity == null;
                teamsRow.Used = teamsUsed;
                teamsRow.Remaining = dto.TeamCapacity.Remaining;
            }
            return dto;
        }

        /// <summary>HELD first (newest), else a licence on one of the manager's teams.</summary>
        private async Task<(CompanyLicense? License, string? Source)> FindLicenseAsync(int userId, DateTime now, CancellationToken ct)
        {
            var live = _context.CompanyLicenses.AsNoTracking()
                .Where(l => !l.IsDel && (l.ExpiryDate == null || l.ExpiryDate > now));

            var held = await live.Where(l => l.AssignedUserId == userId)
                .OrderByDescending(l => l.Id)
                .FirstOrDefaultAsync(ct);
            if (held != null) return (held, HeldSource);

            var teamIds = _context.ManagerTeamIds(userId);
            var onTeam = await live
                .Where(l => _context.AssignedCompanyLicenses.Any(a =>
                                a.CompanyLicenseId == l.Id && a.IsActive && !a.IsDel && teamIds.Contains(a.BusinessUnitId))
                            || _context.ComplimentaryLicenseGrants.Any(g =>
                                g.CompanyLicenseId == l.Id && g.IsActive && !g.IsDel && teamIds.Contains(g.ComplimentaryBusinessUnitId)))
                .OrderByDescending(l => l.Id)
                .FirstOrDefaultAsync(ct);
            return onTeam != null ? (onTeam, TeamSource) : (null, null);
        }

        /// <summary>
        /// Base = the MANAGERS_AND_TEAMS value (not included → 0, null limit → unlimited); no
        /// value stored (or the row is inactive) → today's 3.
        /// </summary>
        private async Task<TeamCapacityDto> TeamCapacityAsync(ServicePlanFeature? teamsCell, bool allowsExtension, CompanyLicense? license,
            int? profileId, int teamsUsed, CancellationToken ct)
        {
            int? baseCapacity = teamsCell == null
                ? LicenseTeamLimits.MaxTeamsPerLicense
                : teamsCell.IsIncluded ? teamsCell.LimitValue : 0;

            // With a licence the total is the licence's limit, the same number every team
            // check uses (ManagerTeamLimitQueries): plan limit + extensions where allowed.
            int? total = baseCapacity;
            if (profileId is > 0)
            {
                var limits = await _context.GetProfileTeamLimitsAsync((uint)profileId.Value, ct);
                if (license != null)
                {
                    var limit = limits.LimitOf(license.Id);
                    total = limit >= LicenseTeamLimits.Unlimited ? null : limit;
                }
                else
                {
                    // No live licence: the team checks allow the profile's teams per licence
                    // (ManagerTeamLimitAsync), so that is what is shown.
                    total = limits.TeamsPerLicense;
                    baseCapacity = total;
                }
            }

            var extended = total != null && baseCapacity != null ? Math.Max(0, total.Value - baseCapacity.Value) : 0;
            return new TeamCapacityDto
            {
                DefaultCapacity = baseCapacity,
                ExtendedCapacity = extended,
                TotalCapacity = total,
                TeamsUsed = teamsUsed,
                Remaining = total == null ? null : Math.Max(0, total.Value - teamsUsed),
                CanBeExtended = allowsExtension
            };
        }

        // ── Mapping ──────────────────────────────────────────────────────────

        private static FeatureEntitlementDto Ungated(PlanFeature f) => new()
        {
            Key = f.Key,
            Title = f.Title,
            ValueType = f.ValueType,
            IsEnabled = true,
            IsUnlimited = f.ValueType == PlanFeatureValueTypes.Limit,
            LimitUnit = f.LimitUnit,
            LimitPeriod = f.LimitPeriod
        };

        private static FeatureEntitlementDto Gated(PlanFeature f, ServicePlanFeature? cell, IReadOnlyDictionary<string, int> usage)
        {
            var enabled = cell?.IsIncluded == true;
            var isLimit = f.ValueType == PlanFeatureValueTypes.Limit;
            int? limit = enabled && isLimit ? cell!.LimitValue : null;
            int? used = isLimit && usage.TryGetValue(f.Key, out var u) ? u : null;

            int? remaining = null;
            if (isLimit && !enabled) remaining = 0;
            else if (limit != null && used != null) remaining = Math.Max(0, limit.Value - used.Value);

            return new FeatureEntitlementDto
            {
                Key = f.Key,
                Title = f.Title,
                ValueType = f.ValueType,
                IsEnabled = enabled,
                Limit = limit,
                IsUnlimited = enabled && isLimit && limit == null,
                LimitUnit = f.LimitUnit,
                LimitPeriod = f.LimitPeriod,
                Used = used,
                Remaining = remaining,
                Display = PlanManagementService.CellDisplay(f, cell),
                SubText = cell?.SubText
            };
        }
    }
}
