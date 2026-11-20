using CloverBackoffice.Components.Enums;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Implementation;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Services.Implementation
{
    public partial class CompanyLicenseService : ICompanyLicenseService
    {
        private readonly ICompanyLicenseRepository _repo;
        private readonly ApplicationDbContext _db;
        private readonly ILicensedPeriodService _licensedPeriod;

        // Per-profile licence lock, shared with Add User, create-manager and
        // team creation (CompanyLicenseRepository.ProfileLicenseLockKey).
        private const int ProfileLicenseLockKey = 16016;

        private const string TrialLicenseType = "Trial";

        private static readonly string InactiveStatus = ((int)UserStatus.Suspend).ToString();

        // Plan entitlements for the licence rows, resolved when first needed (null = rows
        // without them). Not a constructor dependency: PlanEntitlementService needs
        // IUserRoleAssignmentService, which needs this service, so taking it directly is a
        // circular dependency the container refuses at startup.
        private readonly IServiceProvider? _services;
        private IPlanEntitlementService? _entitlementsResolved;
        private IPlanEntitlementService? _entitlements =>
            _entitlementsResolved ??= _services?.GetService(typeof(IPlanEntitlementService)) as IPlanEntitlementService;

        public CompanyLicenseService(ICompanyLicenseRepository repo, ApplicationDbContext db, ILicensedPeriodService licensedPeriod,
            IServiceProvider? services = null)
        {
            _services = services;
            _repo = repo;
            _db = db;
            _licensedPeriod = licensedPeriod;
        }

        public async Task<List<ManagerLicenseDto>> GetLicenseDetailsAsync(uint profileId, int? actorUserId)
        {
            await AuthorizeActorForCompanyAsync(profileId, actorUserId);

            var pid = (int)profileId;
            var teamManager = (int)UserGroups.TeamManager;
            var now = DateTime.UtcNow;

            // Same Team Manager rule as Add User: UserRoles, legacy UserGroupId only
            // for a user with no UserRoles rows.
            var managers = await _db.Users.AsNoTracking()
                .Where(u => u.ProfileId == pid && !u.IsDel
                            && (u.Roles.Any(r => r.UserGroupId == teamManager)
                                || (!u.Roles.Any() && u.UserGroupId == teamManager)))
                .OrderBy(u => u.Name)
                .Select(u => new { u.UserId, u.Name })
                .ToListAsync();

            var licenses = await _db.CompanyLicenses.AsNoTracking()
                .Include(l => l.CompanySubscription)
                .Where(l => l.ProfileId == profileId && !l.IsDel)
                .ToListAsync();
            var licenseIds = licenses.Select(l => l.Id).ToList();

            var assignments = await _db.AssignedCompanyLicenses.AsNoTracking()
                .Where(a => licenseIds.Contains(a.CompanyLicenseId) && a.IsActive && !a.IsDel)
                .Select(a => new { a.CompanyLicenseId, UnitId = a.BusinessUnitId, a.AssignedAt })
                .ToListAsync();
            var grants = await _db.ComplimentaryLicenseGrants.AsNoTracking()
                .Where(g => licenseIds.Contains(g.CompanyLicenseId) && g.IsActive && !g.IsDel)
                .Select(g => new { g.CompanyLicenseId, UnitId = g.ComplimentaryBusinessUnitId })
                .ToListAsync();

            // Fallback for a manager whose teams hold no licence row (e.g. no teams
            // yet): the profile's newest subscription gives plan and renewal date.
            var latestSub = await _db.CompanySubscriptions.AsNoTracking()
                .Where(s => s.ProfileId == profileId && !s.IsDel)
                .OrderByDescending(s => s.CurrentPeriodEnd)
                .ThenByDescending(s => s.Id)
                .FirstOrDefaultAsync();

            // Teams limit per licence, after any RRIDA Admin extension.
            var limits = await _db.GetProfileTeamLimitsAsync(profileId);

            var result = new List<ManagerLicenseDto>();
            foreach (var m in managers)
            {
                var teamIds = await _db.ManagerTeamIds(m.UserId).ToListAsync();

                // The manager's licence: the one they hold (Profile Admin's trial,
                // auto-assigned at creation, or assigned by managerId), else the one
                // their teams ride on (same rule as POST cancel): live first, then
                // oldest assignment.
                var license = licenses
                    .Where(l => l.AssignedUserId == m.UserId)
                    .OrderBy(l => IsLicenseLive(l, now) ? 0 : 1)
                    .ThenBy(l => l.Id)
                    .FirstOrDefault()
                    ?? licenses
                    .Where(l => assignments.Any(a => a.CompanyLicenseId == l.Id && teamIds.Contains(a.UnitId))
                             || grants.Any(g => g.CompanyLicenseId == l.Id && teamIds.Contains(g.UnitId)))
                    .OrderBy(l => IsLicenseLive(l, now) ? 0 : 1)
                    .ThenBy(l => assignments.Where(a => a.CompanyLicenseId == l.Id)
                                            .Select(a => (DateTime?)a.AssignedAt).FirstOrDefault() ?? DateTime.MaxValue)
                    .ThenBy(l => l.Id)
                    .FirstOrDefault();

                var row = BuildManagerLicense(m.UserId, m.Name, teamIds.Count, license, latestSub, now);
                row.TeamsLimit = limits.LimitOf(license?.Id);
                result.Add(row);
            }

            // Portfolio Managers who are not Team Managers yet, and users with no role
            // at all (roles = []), so the Licences tab can give them a Team Manager
            // licence (which makes them one). They hold none, so NO_LICENSE; inactive
            // ones cannot take one and are left out.
            var portfolioManager = (int)UserGroups.PortfolioManager;
            var noRole = RoleHierarchy.NoRoleGroupId;
            var managerIds = managers.Select(m => m.UserId).ToList();
            var candidates = await _db.Users.AsNoTracking()
                .Where(u => u.ProfileId == pid && !u.IsDel && u.Status != InactiveStatus
                            && !managerIds.Contains(u.UserId)
                            && (u.Roles.Any(r => r.UserGroupId == portfolioManager)
                                || (!u.Roles.Any() && (u.UserGroupId == portfolioManager || u.UserGroupId == noRole))))
                .OrderBy(u => u.Name)
                .Select(u => new { u.UserId, u.Name, IsPortfolioManager = u.Roles.Any() || u.UserGroupId == portfolioManager })
                .ToListAsync();
            foreach (var p in candidates)
            {
                result.Add(new ManagerLicenseDto
                {
                    ManagerId = p.UserId,
                    ManagerName = p.Name,
                    TeamsUsed = await _db.ManagerTeamIds(p.UserId).CountAsync(),
                    TeamsLimit = limits.TeamsPerLicense,
                    Status = "NO_LICENSE",
                    IsTeamManager = false,
                    IsPortfolioManager = p.IsPortfolioManager
                });
            }

            // Plan catalogue: plan, price, team capacity and entitlements per licence row.
            await StampManagerPlansAsync(result);
            return result;
        }

        private static bool IsLicenseLive(Infrastructure.Models.CompanyLicense l, DateTime now)
        {
            var end = l.ExpiryDate ?? l.CompanySubscription?.CurrentPeriodEnd;
            return !end.HasValue || end.Value == default || DateTime.SpecifyKind(end.Value, DateTimeKind.Utc) > now;
        }

        private static ManagerLicenseDto BuildManagerLicense(
            int managerId, string? managerName, int teamsUsed,
            Infrastructure.Models.CompanyLicense? license,
            Infrastructure.Models.CompanySubscription? fallbackSub, DateTime now)
        {
            var dto = new ManagerLicenseDto
            {
                ManagerId = managerId,
                ManagerName = managerName,
                TeamsUsed = teamsUsed,
                CompanyLicenseId = license?.Id,
                LicenseKey = license?.LicenseKey
            };

            var sub = license?.CompanySubscription ?? fallbackSub;
            if (sub == null && license == null)
            {
                dto.Status = "NO_LICENSE";
                return dto;
            }

            bool trial = license != null
                ? license.LicenseType == "Trial"
                : sub!.Status == Infrastructure.Models.SubscriptionStatus.Trial;
            dto.Plan = trial ? "Trial" : sub?.BillingCycle.ToString();

            var end = license?.ExpiryDate
                      ?? (sub == null || sub.CurrentPeriodEnd == default ? (DateTime?)null : sub.CurrentPeriodEnd);
            dto.RenewalDate = end?.ToString("yyyy-MM-dd");

            bool live = !end.HasValue || DateTime.SpecifyKind(end.Value, DateTimeKind.Utc) > now;
            bool cancelled = (license?.CancelAtPeriodEnd ?? false)
                             || (sub?.CancelAtPeriodEnd ?? false)
                             || sub?.Status == Infrastructure.Models.SubscriptionStatus.Canceled;
            dto.Status = !live ? "EXPIRED" : cancelled ? "CANCELLED" : "ACTIVE";
            return dto;
        }

        public async Task<List<ManagerTeamUsageDto>> GetManagerTeamUsageAsync(uint profileId, int? userId, int? actorUserId)
        {
            await AuthorizeActorForCompanyAsync(profileId, actorUserId);

            var pid = (int)profileId;
            var teamManager = (int)UserGroups.TeamManager;

            // Same Team Manager rule as Add User: UserRoles, legacy UserGroupId only
            // for a user with no UserRoles rows.
            var managers = await _db.Users.AsNoTracking()
                .Where(u => u.ProfileId == pid && !u.IsDel
                            && (userId == null || u.UserId == userId)
                            && (u.Roles.Any(r => r.UserGroupId == teamManager)
                                || (!u.Roles.Any() && u.UserGroupId == teamManager)))
                .OrderBy(u => u.Name)
                .Select(u => new { u.UserId, u.Name, u.Username })
                .ToListAsync();

            var limits = await _db.GetProfileTeamLimitsAsync(profileId);

            var result = new List<ManagerTeamUsageDto>();
            foreach (var m in managers)
            {
                var teamIds = await _db.ManagerTeamIds(m.UserId).ToListAsync();
                var teams = await _db.BusinessUnits.AsNoTracking()
                    .Where(b => teamIds.Contains(b.Id))
                    .OrderBy(b => b.Name)
                    .Select(b => new ManagerTeamDto { UnitId = b.Id, Name = b.Name })
                    .ToListAsync();
                var maxTeams = await _db.ManagerTeamLimitAsync(m.UserId, limits);

                result.Add(new ManagerTeamUsageDto
                {
                    UserId = m.UserId,
                    Name = m.Name,
                    Email = m.Username,
                    TeamsUsed = teams.Count,
                    MaxTeams = maxTeams,
                    TeamUsage = LicenseTeamLimits.FormatUsage(teams.Count, maxTeams),
                    LimitReached = teams.Count >= maxTeams,
                    Teams = teams
                });
            }
            return result;
        }

        // Largest single extension, so a typo cannot open an unlimited team count.
        public const int MaxAdditionalTeams = 100;

        // RRIDA Admin raises a team limit (POST /api/license-assignments/extend-team-limit):
        //   * PROFILE — ProfileId only: every Team Manager licence of the profile.
        //   * LICENSE — ProfileId + ManagerId and/or CompanyLicenseId: that licence only.
        // The extension row is both the audit record and what the limit checks
        // (ManagerTeamLimitQueries) sum, so nothing else is rewritten and existing
        // team assignments stay as they are.
        public async Task<TeamLimitExtensionResultDto> ExtendTeamLimitAsync(ExtendTeamLimitRequestDto dto, int actorUserId)
        {
            if (dto == null)
                throw new InvalidOperationException("Request body is required.");

            var actor = await _db.Users.AsNoTracking()
                .Where(u => u.UserId == actorUserId && !u.IsDel)
                .Select(u => new { u.UserGroupId })
                .FirstOrDefaultAsync()
                ?? throw new UnauthorizedAccessException("Invalid or missing token.");
            if (actor.UserGroupId != (int)UserGroups.RridaAdmin)
                throw new UnauthorizedAccessException(ResponseMessages.Errors.TeamLimitExtensionNotAllowed);

            if (dto.ProfileId is null or 0)
                throw new InvalidOperationException("ProfileId is required.");
            if (dto.AdditionalTeams is null)
                throw new InvalidOperationException("AdditionalTeams is required.");
            if (dto.AdditionalTeams.Value < 1 || dto.AdditionalTeams.Value > MaxAdditionalTeams)
                throw new InvalidOperationException($"AdditionalTeams must be between 1 and {MaxAdditionalTeams}.");

            var notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim();
            if (notes?.Length > TeamLimitExtension.NotesMaxLength)
                throw new InvalidOperationException($"Notes cannot be longer than {TeamLimitExtension.NotesMaxLength} characters.");

            var profileId = dto.ProfileId.Value;
            var additional = dto.AdditionalTeams.Value;
            int? managerId = dto.ManagerId is > 0 ? dto.ManagerId : null;
            int? licenseId = dto.CompanyLicenseId is > 0 ? dto.CompanyLicenseId : null;

            if (!await _db.Profiles.AsNoTracking().AnyAsync(p => p.Id == profileId && !p.IsDel))
                throw new InvalidOperationException("Invalid ProfileId. Profile does not exist.");

            string? managerName = null;
            Infrastructure.Models.CompanyLicense? license = null;
            TeamLimitExtension extension;

            await using (var tx = await _db.Database.BeginTransactionAsync())
            {
                try
                {
                    // Same per-profile lock as every licence change, so two extensions
                    // of one profile each read the limit the other wrote.
                    await LockProfileLicensesAsync(profileId);

                    var now = DateTime.UtcNow;
                    var limits = await _db.GetProfileTeamLimitsAsync(profileId);

                    if (managerId == null && licenseId == null)
                    {
                        // A profile extension raises only licences whose plan allows it; a
                        // profile with none of those (all Light) has nothing to extend.
                        if (limits.PlanLicenseLimits.Count > 0
                            && limits.PlanLicenseLimits.Keys.All(id => !limits.AllowsExtension(id)))
                            throw new InvalidOperationException(ResponseMessages.Errors.TeamLimitExtensionPlanNotAllowed);

                        extension = new TeamLimitExtension
                        {
                            Scope = TeamLimitExtension.ProfileScope,
                            PreviousLimit = limits.TeamsPerLicense,
                            NewLimit = limits.TeamsPerLicense + additional
                        };
                    }
                    else
                    {
                        (license, managerId, managerName) = await ResolveLicenseToExtendAsync(profileId, managerId, licenseId, now);
                        // Only plans that allow it (Pro) can be extended; Light keeps its limit.
                        if (!limits.AllowsExtension(license.Id))
                            throw new InvalidOperationException(ResponseMessages.Errors.TeamLimitExtensionPlanNotAllowed);
                        var previous = limits.LimitOf(license.Id);
                        extension = new TeamLimitExtension
                        {
                            Scope = TeamLimitExtension.LicenseScope,
                            CompanyLicenseId = license.Id,
                            ManagerUserId = managerId,
                            PreviousLimit = previous,
                            NewLimit = previous + additional
                        };
                    }

                    extension.ProfileId = profileId;
                    extension.AdditionalTeams = additional;
                    extension.Notes = notes;
                    extension.ExtendedByUserId = actorUserId;
                    extension.CreatedAt = now;
                    _db.TeamLimitExtensions.Add(extension);
                    await _db.SaveChangesAsync();

                    await tx.CommitAsync();
                }
                catch
                {
                    await tx.RollbackAsync();
                    throw;
                }
            }

            var usage = await _repo.GetLicenseUsageAsync(profileId);
            var details = await GetLicenseDetailsAsync(profileId, actorUserId);
            if (license != null)
            {
                var extendedLicenseId = license.Id;
                details = details
                    .Where(r => r.CompanyLicenseId == extendedLicenseId || (managerId.HasValue && r.ManagerId == managerId.Value))
                    .ToList();
            }

            return new TeamLimitExtensionResultDto
            {
                ExtensionId = extension.Id,
                Scope = extension.Scope,
                ProfileId = profileId,
                ManagerId = managerId,
                ManagerName = managerName,
                CompanyLicenseId = license?.Id,
                LicenseKey = license?.LicenseKey,
                AdditionalTeams = additional,
                PreviousLimit = extension.PreviousLimit,
                NewLimit = extension.NewLimit,
                TeamsPerLicense = usage.TeamsPerLicense,
                MaxTeams = usage.MaxTeams,
                TotalTeams = usage.TotalTeams,
                Notes = notes,
                ExtendedByUserId = actorUserId,
                ExtendedAt = extension.CreatedAt,
                Licenses = details
            };
        }

        // The licence a LICENSE extension applies to, and its Team Manager:
        //   * CompanyLicenseId only    → that licence (manager = its live holder, if any);
        //   * ManagerId only           → the live licence the manager holds;
        //   * both                     → the licence, which must be the manager's.
        private async Task<(Infrastructure.Models.CompanyLicense License, int? ManagerId, string? ManagerName)> ResolveLicenseToExtendAsync(
            uint profileId, int? managerId, int? licenseId, DateTime now)
        {
            string? managerName = null;
            if (managerId.HasValue)
            {
                var manager = await _db.Users.AsNoTracking()
                    .Where(u => u.UserId == managerId.Value && !u.IsDel)
                    .Select(u => new { u.Name, u.ProfileId })
                    .FirstOrDefaultAsync()
                    ?? throw new InvalidOperationException($"Manager {managerId} not found.");
                if (manager.ProfileId != (int)profileId)
                    throw new InvalidOperationException($"Manager {managerId} does not belong to profile {profileId}.");
                if (!await _db.TeamManagersOf((int)profileId).AnyAsync(u => u.UserId == managerId.Value))
                    throw new InvalidOperationException($"User {managerId} is not a Team Manager.");
                managerName = manager.Name;
            }

            Infrastructure.Models.CompanyLicense license;
            if (licenseId.HasValue)
            {
                license = await _db.CompanyLicenses.AsNoTracking()
                    .FirstOrDefaultAsync(l => l.Id == licenseId.Value && !l.IsDel)
                    ?? throw new InvalidOperationException($"License {licenseId} not found.");
                if (license.ProfileId != profileId)
                    throw new InvalidOperationException($"License {licenseId} does not belong to profile {profileId}.");
                if (license.ExpiryDate.HasValue && license.ExpiryDate.Value <= now)
                    throw new InvalidOperationException($"License {licenseId} has expired.");

                if (managerId.HasValue)
                {
                    if (license.AssignedUserId != managerId.Value
                        && await _repo.FindUserLicenseIdAsync(managerId.Value) != license.Id)
                        throw new InvalidOperationException($"License {licenseId} is not held by manager {managerId}.");
                }
                else if (license.AssignedUserId is int holderId
                         && await _db.TeamManagersOf((int)profileId).AnyAsync(u => u.UserId == holderId))
                {
                    managerId = holderId;
                    managerName = await _db.Users.AsNoTracking()
                        .Where(u => u.UserId == holderId)
                        .Select(u => u.Name)
                        .FirstOrDefaultAsync();
                }
            }
            else
            {
                var heldId = await _repo.FindUserLicenseIdAsync(managerId!.Value)
                    ?? throw new InvalidOperationException(
                        $"Manager {managerId} holds no live license. Assign a license to them first.");
                license = await _db.CompanyLicenses.AsNoTracking().FirstAsync(l => l.Id == heldId);
            }

            return (license, managerId, managerName);
        }

        public async Task<List<CompanyLicenseDto>> GetByCompanyAsync(uint profileId)
        {
            var licenses = await _repo.GetByCompanyAsync(profileId);
            if (licenses.Count == 0) return new List<CompanyLicenseDto>();

            var licenseIds = licenses.Select(l => l.Id).ToList();

            // Pull active assignments + unit names in two cheap queries instead
            // of N+1 lookups.
            var activeAssignments = await _db.AssignedCompanyLicenses
                .AsNoTracking()
                .Where(a => licenseIds.Contains(a.CompanyLicenseId) && a.IsActive && !a.IsDel)
                .ToListAsync();

            // Active complimentary grants for these licenses — surfaced alongside
            // the purchased assignment so the License module reflects the extra
            // (same-manager) teams each license also covers.
            var grants = await _repo.GetActiveComplimentaryGrantsForLicensesAsync(licenseIds);

            var unitIds = activeAssignments.Select(a => a.BusinessUnitId)
                .Concat(grants.Select(g => g.ComplimentaryBusinessUnitId))
                .Distinct()
                .ToList();
            var unitNames = await _db.BusinessUnits
                .AsNoTracking()
                .Where(b => unitIds.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id, b => b.Name);

            var limits = await _db.GetProfileTeamLimitsAsync(profileId);

            return licenses.Select(l =>
            {
                var active = activeAssignments.FirstOrDefault(a => a.CompanyLicenseId == l.Id);
                var licenseGrants = grants
                    .Where(g => g.CompanyLicenseId == l.Id)
                    .OrderBy(g => g.GrantedAt).ThenBy(g => g.Id)
                    .ToList();
                var grant = licenseGrants.FirstOrDefault();
                var teamsUsed = (active != null ? 1 : 0) + licenseGrants.Count;
                return new CompanyLicenseDto
                {
                    Id = l.Id,
                    ProfileId = l.ProfileId,
                    LicenseKey = l.LicenseKey,
                    LicenseType = l.LicenseType,
                    CompanySubscriptionId = l.CompanySubscriptionId,
                    PlanId = l.PlanId,
                    CurrentlyAssignedBusinessUnitId = active?.BusinessUnitId,
                    CurrentlyAssignedBusinessUnitName = active != null && unitNames.TryGetValue(active.BusinessUnitId, out var n) ? n : null,
                    ComplimentaryBusinessUnitId = grant?.ComplimentaryBusinessUnitId,
                    ComplimentaryBusinessUnitName = grant != null && unitNames.TryGetValue(grant.ComplimentaryBusinessUnitId, out var cn) ? cn : null,
                    ComplimentaryTeams = licenseGrants.Select(g => new ComplimentaryTeamDto
                    {
                        BusinessUnitId = g.ComplimentaryBusinessUnitId,
                        BusinessUnitName = unitNames.TryGetValue(g.ComplimentaryBusinessUnitId, out var gn) ? gn : null,
                        ManagerUserId = g.ManagerUserId,
                        GrantedAt = g.GrantedAt
                    }).ToList(),
                    TeamsUsed = teamsUsed,
                    MaxTeams = limits.LimitOf(l.Id),
                    TeamUsage = LicenseTeamLimits.FormatUsage(teamsUsed, limits.LimitOf(l.Id)),
                    CreatedAt = l.CreatedAt,
                    UpdatedAt = l.UpdatedAt
                };
            }).ToList();
        }

        public async Task<List<CompanyLicenseAssignmentHistoryDto>> GetHistoryAsync(int companyLicenseId)
        {
            var history = await _repo.GetAssignmentHistoryAsync(companyLicenseId);
            if (history.Count == 0) return new List<CompanyLicenseAssignmentHistoryDto>();

            var unitIds = history.Select(h => h.BusinessUnitId).Distinct().ToList();
            var names = await _db.BusinessUnits
                .AsNoTracking()
                .Where(b => unitIds.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id, b => b.Name);

            return history.Select(h => new CompanyLicenseAssignmentHistoryDto
            {
                AssignmentId = h.Id,
                BusinessUnitId = h.BusinessUnitId,
                BusinessUnitName = names.TryGetValue(h.BusinessUnitId, out var n) ? n : null,
                AssignedAt = h.AssignedAt,
                UnassignedAt = h.UnassignedAt,
                IsActive = h.IsActive
            }).ToList();
        }

        public async Task<CompanyLicenseDto> AssignAsync(AssignCompanyLicenseDto dto, int? actorUserId)
        {
            var license = await _repo.GetByIdAsync(dto.CompanyLicenseId)
                ?? throw new InvalidOperationException($"License {dto.CompanyLicenseId} not found.");

            // Hard guard: the target BU must belong to the same company that
            // owns the license. Prevents cross-tenant license assignment.
            if (!await _repo.BusinessUnitExistsForCompanyAsync(license.ProfileId, dto.BusinessUnitId))
                throw new InvalidOperationException(
                    $"BusinessUnit {dto.BusinessUnitId} does not belong to Company {license.ProfileId}.");

            await _repo.AssignAsync(dto.CompanyLicenseId, dto.BusinessUnitId, actorUserId);

            // Re-read so the response reflects the active assignment.
            var refreshed = (await GetByCompanyAsync(license.ProfileId))
                .First(l => l.Id == dto.CompanyLicenseId);
            return refreshed;
        }

        public Task<bool> UnassignAsync(int companyLicenseId, int? actorUserId)
            => _repo.UnassignAsync(companyLicenseId, actorUserId);

        // Frees any license held by a unit that is being deleted. A unit can only
        // be in one of these states (EnsureNoComplimentaryCoverageAsync keeps a
        // unit from holding both), but we check both defensively:
        //   * complimentary beneficiary → revoke that grant;
        //   * purchased assignee        → unassign it AND revoke any grant riding
        //                                 on that license (the free coverage
        //                                 derives from the now-freed purchase).
        public async Task<int> ReleaseLicensesForUnitAsync(uint unitId, int? actorUserId)
        {
            int released = 0;

            var grantForUnit = await _repo.GetActiveComplimentaryGrantForUnitAsync(unitId);
            if (grantForUnit != null && await _repo.RevokeComplimentaryGrantAsync(grantForUnit.Id, actorUserId))
                released++;

            var assignment = await _repo.GetActiveAssignmentForUnitAsync(unitId);
            if (assignment != null)
            {
                if (await _repo.UnassignAsync(assignment.CompanyLicenseId, actorUserId))
                    released++;
                await RevokeDependentGrantAsync(assignment.CompanyLicenseId, actorUserId);
            }

            return released;
        }

        public async Task<int> ReleaseLicensesHeldByUserAsync(int userId, int? actorUserId)
        {
            var held = await _db.CompanyLicenses.AsNoTracking()
                .Where(l => l.AssignedUserId == userId)
                .Select(l => l.Id)
                .ToListAsync();

            foreach (var licenseId in held)
                await _repo.SetLicenseHolderAsync(licenseId, null, actorUserId);

            return held.Count;
        }

        // Revoke complimentary grants touching this unit that are no longer
        // manager-eligible. Wired into the manager assign/unassign paths.
        public Task<int> ReconcileGrantsForUnitAsync(uint unitId, int? actorUserId)
            => _repo.RevokeStaleGrantsForUnitAsync(unitId, actorUserId);

        // Safety-net sweep, driven by the reconciliation background job.
        public Task<int> ReconcileAllStaleGrantsAsync(int? actorUserId)
            => _repo.RevokeAllStaleGrantsAsync(actorUserId);

        public Task<int?> FindAvailableLicenseIdAsync(uint profileId)
            => _repo.FindAvailableLicenseIdAsync(profileId);

        public Task<LicenseUsage> GetLicenseUsageAsync(uint profileId)
            => _repo.GetLicenseUsageAsync(profileId);

        public Task<int?> AssignLicenseToUserAsync(uint profileId, int userId, int? actorUserId, string? planCode = null)
            => _repo.AssignLicenseToUserAsync(profileId, userId, actorUserId, planCode);

        public Task<bool> EnsureTeamCoverageAsync(uint unitId, int? actorUserId, bool allowFreeLicense = true)
            => _repo.EnsureTeamCoverageAsync(unitId, actorUserId, allowFreeLicense);

        public Task<bool> UnitHasLiveLicenseAsync(uint unitId)
            => _repo.UnitHasLiveLicenseAsync(unitId);

        // ---------- single-entry lifecycle dispatcher ----------
        //
        // One method drives all three operations so the controller stays a
        // thin pass-through. Per-action contract:
        //
        //   Assign   — license must be free (or already on the target unit
        //              for idempotency). Active on a different unit ⇒ reject;
        //              caller must use Reassign instead.
        //   Reassign — atomic from→to. From must match the current active
        //              assignee; otherwise reject (stale UI guard).
        //   Remove   — close the active assignment. Looks up by license id
        //              when given, else by unit id.
        //
        // All three delegate to repo primitives that keep the single-active
        // invariant inside a transaction and write to AssignedCompanyLicenseAuditLogs.
        // Confirms the actor may manage licenses for `profileId` (the company that
        // owns the targeted license). Clovera admins may manage any company; every
        // other role — including Partner admins — is confined to its own company,
        // so a Partner admin cannot assign/reassign/remove a child company's
        // licenses. A null actor means a system/webhook caller (no token) and is
        // allowed, since those paths are not user-driven. Throws
        // UnauthorizedAccessException when the actor is not permitted.
        private async Task AuthorizeActorForCompanyAsync(uint profileId, int? actorUserId)
        {
            if (!actorUserId.HasValue) return; // system/webhook caller

            var actor = await _db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.UserId == actorUserId.Value && !u.IsDel);

            if (actor == null)
                throw new UnauthorizedAccessException("Invalid or missing token.");

            if (actor.UserGroupId == (int)UserGroups.RridaAdmin)
                return;

            if (actor.ProfileId.HasValue && (uint)actor.ProfileId.Value == profileId)
                return;

            throw new UnauthorizedAccessException(
                "You are not authorized to manage licenses for this company.");
        }

        public async Task<LicenseAssignmentResultDto> ExecuteLifecycleAsync(
            LicenseAssignmentRequestDto dto, int? actorUserId)
        {
            if (dto == null) throw new InvalidOperationException("Request body is required.");

            return dto.Action switch
            {
                LicenseAssignmentAction.Assign when dto.ManagerId.HasValue
                                                            => await DoAssignToManagerAsync(dto, actorUserId),
                LicenseAssignmentAction.Assign              => await DoAssignAsync(dto, actorUserId),
                LicenseAssignmentAction.Reassign            => await DoReassignAsync(dto, actorUserId),
                LicenseAssignmentAction.Remove              => await DoRemoveAsync(dto, actorUserId),
                LicenseAssignmentAction.AssignComplimentary => await DoAssignComplimentaryAsync(dto, actorUserId),
                _ => throw new InvalidOperationException(
                        $"Unsupported action '{dto.Action}'. Use Assign, Reassign, Remove, or AssignComplimentary.")
            };
        }

        private async Task<LicenseAssignmentResultDto> DoAssignAsync(
            LicenseAssignmentRequestDto dto, int? actorUserId)
        {
            if (!dto.CompanyLicenseId.HasValue || dto.CompanyLicenseId.Value <= 0)
                throw new InvalidOperationException("CompanyLicenseId is required for Assign.");
            if (!dto.BusinessUnitId.HasValue || dto.BusinessUnitId.Value == 0)
                throw new InvalidOperationException("BusinessUnitId is required for Assign.");

            var licenseId = dto.CompanyLicenseId.Value;
            var unitId = dto.BusinessUnitId.Value;

            var license = await _repo.GetByIdAsync(licenseId)
                ?? throw new InvalidOperationException($"License {licenseId} not found.");

            await AuthorizeActorForCompanyAsync(license.ProfileId, actorUserId);

            if (!await _repo.BusinessUnitExistsForCompanyAsync(license.ProfileId, unitId))
                throw new InvalidOperationException(
                    $"BusinessUnit {unitId} does not belong to Company {license.ProfileId}.");

            var current = await _repo.GetActiveAssignmentAsync(licenseId);

            // Idempotent: already on the target unit.
            if (current != null && current.BusinessUnitId == unitId)
                return await BuildResultAsync(license, current, previous: null, operation: "Assign");

            // Active on a different unit ⇒ reject. Caller must use Reassign
            // explicitly to move it.
            if (current != null)
                throw new InvalidOperationException(
                    $"License is already assigned to BusinessUnit {current.BusinessUnitId}. " +
                    $"Use Action=Reassign to move it.");

            // A unit already covered by a complimentary grant must not also take
            // its own purchased license — that dual state makes the unit's
            // effective license ambiguous. Require the grant to be removed first.
            await EnsureNoComplimentaryCoverageAsync(unitId);

            var newAssignment = await _repo.AssignAsync(licenseId, unitId, actorUserId);
            return await BuildResultAsync(license, newAssignment, previous: null, operation: "Assign");
        }

        // Assign by Team Manager: 1 licence = 1 Team Manager, up to
        // LicenseTeamLimits.MaxTeamsPerLicense teams. The licence is recorded on
        // the manager's teams - a purchased assignment on the first, complimentary
        // grants on the rest - the same rows EnsureTeamCoverageAsync writes, so
        // licence details, dashboards and cancel all read it as this manager's.
        //
        // Licence used: CompanyLicenseId when sent, else the manager's current
        // licence (idempotent re-assign), else a free licence of the profile.
        private async Task<LicenseAssignmentResultDto> DoAssignToManagerAsync(
            LicenseAssignmentRequestDto dto, int? actorUserId)
        {
            var managerId = dto.ManagerId ?? 0;
            if (managerId <= 0)
                throw new InvalidOperationException("ManagerId is required for Assign.");

            var managerProfileId = await _db.Users.AsNoTracking()
                .Where(u => u.UserId == managerId && !u.IsDel)
                .Select(u => u.ProfileId)
                .FirstOrDefaultAsync();
            if (managerProfileId is null or <= 0)
                throw new InvalidOperationException($"Manager {managerId} not found.");
            var profileId = (uint)managerProfileId.Value;

            // The caller must be allowed to manage the manager's profile.
            await AuthorizeActorForCompanyAsync(profileId, actorUserId);

            // A Team Manager, or a Portfolio Manager / Profile Admin / user with no
            // role who becomes one here (also a Profile Admin taking back a removed
            // Team Manager role, for themselves or not): holding the role is not a
            // precondition for getting the licence that gives it. Their other roles
            // are kept.
            var promote = false;
            if (!await _db.TeamManagersOf((int)profileId).AnyAsync(u => u.UserId == managerId))
            {
                var candidate = await _db.Users.AsNoTracking()
                    .Where(u => u.UserId == managerId)
                    .Select(u => new { u.Name, u.Status, u.UserGroupId, Roles = u.Roles.Select(r => r.UserGroupId).ToList() })
                    .FirstAsync();
                var roles = candidate.Roles.Count > 0 ? candidate.Roles : new List<int> { candidate.UserGroupId };
                if (!RoleHierarchy.CanBecomeTeamManager(roles))
                    throw new InvalidOperationException($"User {managerId} is not a Team Manager.");
                if (candidate.Status == InactiveStatus)
                    throw new InvalidOperationException(
                        string.Format(ResponseMessages.Errors.TeamManagerLicenceUserInactive, candidate.Name));
                promote = true;
            }

            var allTeamIds = (await _db.ManagerTeamIds(managerId).ToListAsync()).OrderBy(id => id).ToList();

            // Plan catalogue: the plan of the licence to give (planId / planCode), null = any.
            var requestedPlan = await ResolveRequestedPlanAsync(dto.PlanId, dto.PlanCode);

            // Special units are covered without a licence, so a seat is never put on one.
            // A manager with no teams yet just holds the licence (CompanyLicenses.AssignedUserId);
            // their first team is covered by it when created.
            var teamIds = allTeamIds.Where(id => !_licensedPeriod.IsExemptFromLicensing(id)).ToList();

            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                await LockProfileLicensesAsync(profileId);

                var now = DateTime.UtcNow;
                var currentLicenseId = await _repo.FindUserLicenseIdAsync(managerId);

                // A new Team Manager takes one licence: under the lock, as Add User,
                // the profile must have one nobody is using.
                if (promote && currentLicenseId == null
                    && (await _repo.GetLicenseUsageAsync(profileId)).AvailableLicenses <= 0)
                    throw new InvalidOperationException(ResponseMessages.Errors.TeamManagerLicenceNotAvailable);

                // A manager on a trial licence moves to a purchased one: the trial
                // is ended in this same transaction (see below).
                var onTrial = currentLicenseId.HasValue
                              && await _db.CompanyLicenses.AsNoTracking()
                                  .AnyAsync(l => l.Id == currentLicenseId.Value && l.LicenseType == TrialLicenseType);

                int licenseId;
                if (dto.CompanyLicenseId is > 0)
                {
                    licenseId = dto.CompanyLicenseId.Value;
                }
                else if (requestedPlan != null)
                {
                    // The manager's licence when already on that plan, else a free one of it.
                    licenseId = await PickLicenseForPlanAsync(profileId, requestedPlan, currentLicenseId, onTrial);
                }
                else if (onTrial)
                {
                    // No licence sent (the Licences screen never sends one): a
                    // purchased licence the manager already holds alongside the
                    // trial (old data), else a free purchased licence. Reusing the
                    // trial here was the bug - the call "succeeded" and nothing
                    // changed.
                    licenseId = await _db.CompanyLicenses.AsNoTracking()
                                    .Where(l => l.AssignedUserId == managerId && l.ProfileId == profileId
                                                && !l.IsDel && l.LicenseType != TrialLicenseType
                                                && (l.ExpiryDate == null || l.ExpiryDate > now))
                                    .OrderBy(l => l.Id)
                                    .Select(l => (int?)l.Id)
                                    .FirstOrDefaultAsync()
                                ?? await _repo.FindAvailablePaidLicenseIdAsync(profileId)
                                ?? throw new InvalidOperationException(ResponseMessages.Errors.TrialManagerNoPaidLicenceAvailable);
                }
                else
                {
                    // No plan sent: the only free plan; Light and Pro both free → the admin
                    // must choose (planCode), never a random one.
                    int? picked = null;
                    if (currentLicenseId == null)
                    {
                        var (freeId, outcome) = await _db.PickFreeLicenseAsync(profileId, null, useDefaultPlan: false);
                        if (outcome == LicencePlanQueries.AutoPickOutcome.MixedPlans)
                            throw new InvalidOperationException(ResponseMessages.Errors.PlanChoiceRequired);
                        picked = freeId;
                    }
                    licenseId = currentLicenseId
                                ?? picked
                                ?? throw new InvalidOperationException(promote
                                    ? ResponseMessages.Errors.TeamManagerLicenceNotAvailable
                                    : "No available license. All licenses are in use — purchase another license first.");
                }

                // GetByIdAsync skips deleted rows, which is also how an expired seat looks.
                var license = await _repo.GetByIdAsync(licenseId)
                    ?? throw new InvalidOperationException($"License {licenseId} not found or has expired.");

                await AuthorizeActorForCompanyAsync(license.ProfileId, actorUserId);

                if (license.ProfileId != profileId)
                    throw new InvalidOperationException(
                        $"License {licenseId} does not belong to the manager's profile.");
                if (license.ExpiryDate.HasValue && license.ExpiryDate.Value <= now)
                    throw new InvalidOperationException($"License {licenseId} has expired.");
                if (requestedPlan != null && await LicensePlanIdAsync(licenseId) != requestedPlan.Id)
                    throw new InvalidOperationException(string.Format(ResponseMessages.Errors.PlanLicenceWrongPlan, licenseId, requestedPlan.Name));

                // Already holding a different licence: only trial -> purchased is a
                // move (the trial ends). Purchased -> trial is refused; any other
                // swap needs the current licence removed first, as before.
                int? trialToTerminate = null;
                int? licenseToRelease = null;
                if (currentLicenseId.HasValue && currentLicenseId.Value != licenseId)
                {
                    var targetIsTrial = license.LicenseType == TrialLicenseType;
                    if (onTrial && !targetIsTrial)
                        trialToTerminate = currentLicenseId.Value;
                    else if (!onTrial && targetIsTrial)
                        throw new InvalidOperationException(ResponseMessages.Errors.PaidLicenceCannotBeReplacedByTrial);
                    else if (await IsPlanSwitchAsync(currentLicenseId.Value, licenseId))
                        // Light <-> Pro: the current licence is released below, same transaction.
                        licenseToRelease = currentLicenseId.Value;
                    else
                        throw new InvalidOperationException(
                            $"Manager {managerId} already holds license {currentLicenseId.Value}. " +
                            "Remove it before assigning another one.");
                }

                // A licence belongs to one manager: refuse one held by, or already on
                // a team of, another manager.
                var heldByOtherManager =
                    (license.AssignedUserId is int holderId && holderId != managerId
                     && await _db.TeamManagersOf((int)profileId).AnyAsync(u => u.UserId == holderId))
                    || await _db.AssignedCompanyLicenses.AnyAsync(a =>
                        a.CompanyLicenseId == licenseId && a.IsActive && !a.IsDel
                        && !allTeamIds.Contains(a.BusinessUnitId)
                        && _db.BusinessUnits.Any(b => b.Id == a.BusinessUnitId && b.IsDel != true))
                    || await _db.ComplimentaryLicenseGrants.AnyAsync(g =>
                        g.CompanyLicenseId == licenseId && g.IsActive && !g.IsDel
                        && !allTeamIds.Contains(g.ComplimentaryBusinessUnitId)
                        && _db.BusinessUnits.Any(b => b.Id == g.ComplimentaryBusinessUnitId && b.IsDel != true));
                if (heldByOtherManager)
                    throw new InvalidOperationException(
                        $"License {licenseId} is already assigned to another manager.");

                // Trial first: its team assignment and grants close, so the
                // manager's teams are uncovered and the purchased licence takes
                // them below. Same transaction - a failure later rolls this back.
                // The plan must hold the manager's teams (Pro -> Light with 3 teams is refused).
                await EnsurePlanFitsTeamsAsync(license, licenseToRelease.HasValue, managerId, allTeamIds.Count);

                if (trialToTerminate.HasValue)
                    await _repo.TerminateTrialLicenseAsync(trialToTerminate.Value, licenseId, actorUserId);

                // Plan switch: the old licence leaves the manager and their teams first, so
                // the new one covers them below and only one plan applies.
                if (licenseToRelease.HasValue)
                    await ReleaseLicenseAsync(licenseToRelease.Value, actorUserId);

                await _repo.SetLicenseHolderAsync(licenseId, managerId, actorUserId);

                // Licence first, then the role it gives - same transaction, so a
                // failure leaves neither.
                if (promote)
                {
                    await AddTeamManagerRoleAsync(managerId, actorUserId);
                    await _db.SaveChangesAsync();
                }

                if (teamIds.Count == 0)
                {
                    await tx.CommitAsync();
                    var held = await BuildResultAsync(license, active: null, previous: null, operation: "Assign");
                    held.ManagerUserId = managerId;
                    held.TeamManagerRoleAssigned = promote;
                    held.LicenseNewlyAssigned = currentLicenseId == null;
                    held.TrialConverted = trialToTerminate.HasValue;
                    held.TerminatedTrialLicenseId = trialToTerminate;
                    await StampPlanAsync(held, licenseId, licenseToRelease);
                    return held;
                }

                var uncovered = new List<uint>();
                foreach (var teamId in teamIds)
                    if (!await _repo.UnitHasLiveLicenseAsync(teamId))
                        uncovered.Add(teamId);

                var primary = await _repo.GetActiveAssignmentAsync(licenseId);
                if (primary == null || !teamIds.Contains(primary.BusinessUnitId))
                {
                    // First uncovered team with no leftover row from an expired
                    // licence; AssignAsync closes this licence's stale row, if any.
                    uint primaryTeam = 0;
                    foreach (var teamId in uncovered)
                    {
                        if (await _repo.GetActiveAssignmentForUnitAsync(teamId) != null) continue;
                        if (await _repo.GetActiveComplimentaryGrantForUnitAsync(teamId) != null) continue;
                        primaryTeam = teamId;
                        break;
                    }
                    if (primaryTeam == 0)
                        throw new InvalidOperationException(
                            $"None of manager {managerId}'s teams can take license {licenseId}.");

                    primary = await _repo.AssignAsync(licenseId, primaryTeam, actorUserId);
                }

                // The manager's other teams ride on the same licence, up to the limit.
                var grantCount = (await _repo.GetActiveComplimentaryGrantsForLicenseAsync(licenseId)).Count;
                var maxGrants = await _db.LicenseTeamLimitAsync(licenseId) - 1;
                foreach (var teamId in uncovered)
                {
                    if (teamId == primary.BusinessUnitId) continue;
                    if (grantCount >= maxGrants) break;
                    if (await _repo.GetActiveComplimentaryGrantForUnitAsync(teamId) != null) continue;
                    if (await _repo.GetActiveAssignmentForUnitAsync(teamId) != null) continue;

                    await _repo.AddComplimentaryGrantAsync(licenseId, primary.BusinessUnitId, teamId, managerId, actorUserId);
                    grantCount++;
                }

                await tx.CommitAsync();

                var result = await BuildResultAsync(license, primary, previous: null, operation: "Assign");
                result.ManagerUserId = managerId;
                result.TeamManagerRoleAssigned = promote;
                result.LicenseNewlyAssigned = currentLicenseId == null;
                result.TrialConverted = trialToTerminate.HasValue;
                result.TerminatedTrialLicenseId = trialToTerminate;
                await StampPlanAsync(result, licenseId, licenseToRelease);
                return result;
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        // Separate so tests can run on the in-memory database, which has no advisory locks.
        protected virtual Task LockProfileLicensesAsync(uint profileId)
            => _db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({ProfileLicenseLockKey}, {(int)profileId})");

        // Adds the Team Manager role if missing and re-derives Users.UserGroupId
        // (highest role), as Add User does. No SaveChanges - the caller owns the
        // transaction.
        private async Task AddTeamManagerRoleAsync(int userId, int? actorUserId)
        {
            var role = (int)UserGroups.TeamManager;
            var user = await _db.Users.FirstAsync(u => u.UserId == userId);
            var rows = await _db.UserRoles.Where(r => r.UserId == userId).ToListAsync();
            if (rows.Any(r => r.UserGroupId == role))
                return;

            // Not backfilled yet: keep the role the column already gives them.
            if (rows.Count == 0 && user.UserGroupId > 0 && user.UserGroupId != role)
            {
                var legacy = new UserRole { UserId = userId, UserGroupId = user.UserGroupId, CreatedDate = DateTime.UtcNow };
                _db.UserRoles.Add(legacy);
                rows.Add(legacy);
            }

            var added = new UserRole { UserId = userId, UserGroupId = role, CreatedBy = actorUserId, CreatedDate = DateTime.UtcNow };
            _db.UserRoles.Add(added);
            rows.Add(added);

            var primary = RoleHierarchy.PrimaryOf(rows.Select(r => r.UserGroupId)) ?? role;
            if (user.UserGroupId != primary)
            {
                user.UserGroupId = primary;
                user.ModifiedDate = DateTime.UtcNow.ToString("o");
            }
        }

        // Undoes a Portfolio Manager -> Team Manager promotion whose follow-up step
        // (the team move) failed: the Team Manager role goes, and the licence goes
        // back to the pool when this promotion is what gave it to them.
        public async Task RevertTeamManagerPromotionAsync(int userId, int companyLicenseId, bool releaseLicense, int? actorUserId)
        {
            var profileId = await _db.Users.AsNoTracking()
                .Where(u => u.UserId == userId)
                .Select(u => u.ProfileId)
                .FirstOrDefaultAsync() ?? 0;

            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                await LockProfileLicensesAsync((uint)profileId);

                var user = await _db.Users.FirstAsync(u => u.UserId == userId);
                var rows = await _db.UserRoles.Where(r => r.UserId == userId).ToListAsync();
                var teamManagerRows = rows.Where(r => r.UserGroupId == (int)UserGroups.TeamManager).ToList();
                _db.UserRoles.RemoveRange(teamManagerRows);

                var remaining = rows.Except(teamManagerRows).Select(r => r.UserGroupId).ToList();
                var primary = RoleHierarchy.PrimaryOf(remaining) ?? RoleHierarchy.NoRoleGroupId;
                if (user.UserGroupId != primary)
                {
                    user.UserGroupId = primary;
                    user.ModifiedDate = DateTime.UtcNow.ToString("o");
                }
                await _db.SaveChangesAsync();

                if (releaseLicense)
                {
                    await _repo.SetLicenseHolderAsync(companyLicenseId, null, actorUserId);
                    if (await _repo.GetActiveAssignmentAsync(companyLicenseId) != null)
                    {
                        await _repo.UnassignAsync(companyLicenseId, actorUserId);
                        await RevokeDependentGrantAsync(companyLicenseId, actorUserId);
                    }
                }

                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        // Grants a purchased license to an additional team (Team B) that shares
        // a manager with the team currently holding the license's purchased
        // assignment (Team A). Creates a ComplimentaryLicenseGrant — NOT an
        // AssignedCompanyLicense — so no purchased slot is consumed and available
        // counts are unaffected. Rules enforced:
        //   * license must already be purchased-assigned to a primary team;
        //   * Team B must differ from Team A;
        //   * Team B must not already hold its own purchased license;
        //   * Team B must not already have complimentary coverage elsewhere;
        //   * the license may cover at most LicenseTeamLimits.MaxTeamsPerLicense
        //     teams (purchased + complimentary); grants made under an older,
        //     higher limit are left alone;
        //   * Team A and Team B must share a manager (a user assigned to both).
        private async Task<LicenseAssignmentResultDto> DoAssignComplimentaryAsync(
            LicenseAssignmentRequestDto dto, int? actorUserId)
        {
            if (!dto.CompanyLicenseId.HasValue || dto.CompanyLicenseId.Value <= 0)
                throw new InvalidOperationException("CompanyLicenseId is required for AssignComplimentary.");
            if (!dto.BusinessUnitId.HasValue || dto.BusinessUnitId.Value == 0)
                throw new InvalidOperationException("BusinessUnitId (the complimentary team) is required for AssignComplimentary.");

            var licenseId = dto.CompanyLicenseId.Value;
            var teamB = dto.BusinessUnitId.Value;

            var license = await _repo.GetByIdAsync(licenseId)
                ?? throw new InvalidOperationException($"License {licenseId} not found.");

            await AuthorizeActorForCompanyAsync(license.ProfileId, actorUserId);

            if (!await _repo.BusinessUnitExistsForCompanyAsync(license.ProfileId, teamB))
                throw new InvalidOperationException(
                    $"BusinessUnit {teamB} does not belong to Company {license.ProfileId}.");

            // The license must already be purchased-assigned to a primary team.
            var primary = await _repo.GetActiveAssignmentAsync(licenseId)
                ?? throw new InvalidOperationException(
                    "License is not assigned to any team. Assign it to a primary team before granting complimentary coverage.");
            var teamA = primary.BusinessUnitId;

            if (teamB == teamA)
                throw new InvalidOperationException(
                    "The complimentary team must differ from the team already holding the license.");

            // Idempotent: this license already grants complimentary coverage to
            // exactly this team.
            var existingGrants = await _repo.GetActiveComplimentaryGrantsForLicenseAsync(licenseId);
            var existingGrant = existingGrants.FirstOrDefault(g => g.ComplimentaryBusinessUnitId == teamB);
            if (existingGrant != null)
                return await BuildComplimentaryResultAsync(license, existingGrant, "AssignComplimentary");

            // Team limit: purchased team + complimentary teams.
            var teamLimit = await _db.LicenseTeamLimitAsync(licenseId);
            if (existingGrants.Count >= teamLimit - 1)
                throw new InvalidOperationException(
                    $"Team limit reached: license already covers {LicenseTeamLimits.FormatUsage(1 + existingGrants.Count, teamLimit)} teams. " +
                    "Purchase another license to add more teams for this manager.");

            // Team B must not already hold its own purchased license.
            var teamBOwnLicense = await _repo.GetActiveAssignmentForUnitAsync(teamB);
            if (teamBOwnLicense != null)
                throw new InvalidOperationException(
                    $"BusinessUnit {teamB} already holds its own license; complimentary coverage is not needed.");

            // Team B must not already be a complimentary beneficiary of another license.
            var teamBExistingGrant = await _repo.GetActiveComplimentaryGrantForUnitAsync(teamB);
            if (teamBExistingGrant != null)
                throw new InvalidOperationException(
                    $"BusinessUnit {teamB} already has complimentary coverage from license {teamBExistingGrant.CompanyLicenseId}.");

            // Same-manager rule: a user must be actively assigned to both teams.
            var managerUserId = await _repo.GetSharedManagerUserIdAsync(teamA, teamB)
                ?? throw new InvalidOperationException(
                    $"BusinessUnits {teamA} and {teamB} do not share a manager. " +
                    "A complimentary license requires both teams to have the same manager.");

            var grant = await _repo.AddComplimentaryGrantAsync(
                licenseId, teamA, teamB, managerUserId, actorUserId);

            return await BuildComplimentaryResultAsync(license, grant, "AssignComplimentary");
        }

        // Standalone complimentary path. Resolves WHICH purchased license to ride
        // on from the target team alone, then delegates to the shared
        // DoAssignComplimentaryAsync so every guard (target has no own license /
        // no existing grant, teams-per-license limit, shared-manager, idempotency),
        // authorization, and the audit trail stay in exactly one place.
        public async Task<LicenseAssignmentResultDto> AutoAssignComplimentaryAsync(
            uint targetUnitId, int? actorUserId)
        {
            if (targetUnitId == 0)
                throw new InvalidOperationException("BusinessUnitId (the target team) is required.");

            // The target must not already have complimentary coverage — surface a
            // clear message instead of hunting for a source it can't take.
            // (DoAssignComplimentaryAsync re-checks this; here it just gives a
            //  friendlier error than "no source found".)
            var existingForTarget = await _repo.GetActiveComplimentaryGrantForUnitAsync(targetUnitId);
            if (existingForTarget != null)
                throw new InvalidOperationException(
                    $"BusinessUnit {targetUnitId} already has complimentary coverage from license {existingForTarget.CompanyLicenseId}.");

            var source = await _repo.FindPurchasedComplimentarySourceForUnitAsync(targetUnitId);
            if (source == null)
            {
                // Distinguish "no manager at all" from "manager exists but no
                // same-manager sibling owns an eligible purchased license".
                var hasManager = await _repo.UnitHasActiveManagerAsync(targetUnitId);
                if (!hasManager)
                    throw new InvalidOperationException(
                        $"BusinessUnit {targetUnitId} has no manager. A complimentary license requires a manager " +
                        "shared with a team that owns a purchased license.");

                throw new InvalidOperationException(
                    $"No team sharing a manager with BusinessUnit {targetUnitId} holds an active purchased license " +
                    "available for complimentary coverage. Assign a purchased license to a same-manager team first.");
            }

            var dto = new LicenseAssignmentRequestDto
            {
                Action = LicenseAssignmentAction.AssignComplimentary,
                CompanyLicenseId = source.CompanyLicenseId,
                BusinessUnitId = targetUnitId
            };
            return await DoAssignComplimentaryAsync(dto, actorUserId);
        }

        private async Task<LicenseAssignmentResultDto> DoReassignAsync(
            LicenseAssignmentRequestDto dto, int? actorUserId)
        {
            if (!dto.CompanyLicenseId.HasValue || dto.CompanyLicenseId.Value <= 0)
                throw new InvalidOperationException("CompanyLicenseId is required for Reassign.");
            if (!dto.FromBusinessUnitId.HasValue || dto.FromBusinessUnitId.Value == 0)
                throw new InvalidOperationException("FromBusinessUnitId is required for Reassign.");
            if (!dto.ToBusinessUnitId.HasValue || dto.ToBusinessUnitId.Value == 0)
                throw new InvalidOperationException("ToBusinessUnitId is required for Reassign.");

            var licenseId = dto.CompanyLicenseId.Value;
            var fromUnit = dto.FromBusinessUnitId.Value;
            var toUnit = dto.ToBusinessUnitId.Value;

            var license = await _repo.GetByIdAsync(licenseId)
                ?? throw new InvalidOperationException($"License {licenseId} not found.");

            await AuthorizeActorForCompanyAsync(license.ProfileId, actorUserId);

            if (!await _repo.BusinessUnitExistsForCompanyAsync(license.ProfileId, fromUnit))
                throw new InvalidOperationException(
                    $"FromBusinessUnit {fromUnit} does not belong to Company {license.ProfileId}.");
            if (!await _repo.BusinessUnitExistsForCompanyAsync(license.ProfileId, toUnit))
                throw new InvalidOperationException(
                    $"ToBusinessUnit {toUnit} does not belong to Company {license.ProfileId}.");

            // The destination unit must not already have complimentary coverage —
            // it can't hold both a purchased license and a grant.
            await EnsureNoComplimentaryCoverageAsync(toUnit);

            var newAssignment = await _repo.ReassignAsync(licenseId, fromUnit, toUnit, actorUserId);

            // Any complimentary grant was justified by the OLD primary team sharing
            // a manager with the beneficiary. Once the license moves to a different
            // primary team that basis no longer holds, so revoke it. The admin can
            // re-grant against the new primary if they still share a manager.
            await RevokeDependentGrantAsync(licenseId, actorUserId);

            return await BuildResultAsync(
                license, newAssignment,
                previous: fromUnit,
                operation: "Reassign");
        }

        // Remove accepts either a license id or a unit id. At least one must be
        // present.
        //
        // Complimentary interplay (order matters):
        //   * If BusinessUnitId names a unit that is only a complimentary
        //     beneficiary, revoke JUST that grant and leave the purchased
        //     assignment (on the primary team) untouched. This is resolved FIRST,
        //     even when CompanyLicenseId is also supplied — the beneficiary's UI
        //     row carries the PURCHASED license's id, so a "remove the free
        //     coverage from Team B" payload arrives as
        //     { CompanyLicenseId = <purchased>, BusinessUnitId = TeamB }. Letting
        //     the license id win here would unassign the purchased license from
        //     its primary team (Team A) — the bug this ordering prevents.
        //   * Otherwise, removing by license id (or by the purchased assignee
        //     unit) closes the purchased assignment AND cascades to any
        //     complimentary grant riding on that license (the free coverage
        //     derives from it).
        private async Task<LicenseAssignmentResultDto> DoRemoveAsync(
            LicenseAssignmentRequestDto dto, int? actorUserId)
        {
            // Complimentary-beneficiary removal takes precedence over the
            // license-id branch so the paired purchased team is never affected.
            if (dto.BusinessUnitId.HasValue && dto.BusinessUnitId.Value != 0)
            {
                var grantForUnit = await _repo.GetActiveComplimentaryGrantForUnitAsync(dto.BusinessUnitId.Value);
                if (grantForUnit != null)
                {
                    var grantLicense = await _repo.GetByIdAsync(grantForUnit.CompanyLicenseId)
                        ?? throw new InvalidOperationException($"License {grantForUnit.CompanyLicenseId} not found.");

                    await AuthorizeActorForCompanyAsync(grantLicense.ProfileId, actorUserId);

                    await _repo.RevokeComplimentaryGrantAsync(grantForUnit.Id, actorUserId);
                    return await BuildComplimentaryRemoveResultAsync(grantLicense, grantForUnit);
                }
            }

            if (dto.CompanyLicenseId.HasValue && dto.CompanyLicenseId.Value > 0)
            {
                var licenseId = dto.CompanyLicenseId.Value;
                var license = await _repo.GetByIdAsync(licenseId)
                    ?? throw new InvalidOperationException($"License {licenseId} not found.");

                await AuthorizeActorForCompanyAsync(license.ProfileId, actorUserId);

                // Taking the licence away from its manager frees it (available + 1).
                await _repo.SetLicenseHolderAsync(licenseId, null, actorUserId);

                var current = await _repo.GetActiveAssignmentAsync(licenseId);
                if (current == null)
                    return await BuildResultAsync(license, active: null, previous: null, operation: "Remove");

                var previousUnitId = current.BusinessUnitId;
                await _repo.UnassignAsync(licenseId, actorUserId);
                await RevokeDependentGrantAsync(licenseId, actorUserId);
                return await BuildResultAsync(license, active: null, previous: previousUnitId, operation: "Remove");
            }

            if (dto.BusinessUnitId.HasValue && dto.BusinessUnitId.Value != 0)
            {
                var unitId = dto.BusinessUnitId.Value;

                // Not a complimentary beneficiary (handled above) ⇒ this unit holds
                // a purchased assignment; close it and cascade to any grant riding
                // on that license.
                var current = await _repo.GetActiveAssignmentForUnitAsync(unitId)
                    ?? throw new InvalidOperationException(
                        $"BusinessUnit {unitId} has no active license to remove.");

                var license = await _repo.GetByIdAsync(current.CompanyLicenseId)
                    ?? throw new InvalidOperationException($"License {current.CompanyLicenseId} not found.");

                await AuthorizeActorForCompanyAsync(license.ProfileId, actorUserId);

                await _repo.UnassignAsync(current.CompanyLicenseId, actorUserId);
                await RevokeDependentGrantAsync(current.CompanyLicenseId, actorUserId);
                await _repo.SetLicenseHolderAsync(current.CompanyLicenseId, null, actorUserId);
                return await BuildResultAsync(license, active: null, previous: unitId, operation: "Remove");
            }

            throw new InvalidOperationException(
                "Provide CompanyLicenseId or BusinessUnitId for Remove.");
        }

        // Revokes every active complimentary grant riding on a license.
        // Called after the purchased assignment is closed or moved so the free
        // coverage never outlives the purchased slot it derives from.
        private async Task RevokeDependentGrantAsync(int companyLicenseId, int? actorUserId)
        {
            foreach (var grant in await _repo.GetActiveComplimentaryGrantsForLicenseAsync(companyLicenseId))
                await _repo.RevokeComplimentaryGrantAsync(grant.Id, actorUserId);
        }

        // Teams a license covers right now: its active purchased team (if any)
        // plus its active complimentary teams.
        private async Task<int> CountTeamsUsedAsync(int companyLicenseId)
        {
            var hasPurchased = await _repo.GetActiveAssignmentAsync(companyLicenseId) != null;
            var grants = await _repo.GetActiveComplimentaryGrantsForLicenseAsync(companyLicenseId);
            return (hasPurchased ? 1 : 0) + grants.Count;
        }

        // Guards against a unit holding BOTH a purchased license and complimentary
        // coverage. A complimentary beneficiary already has effective coverage, so
        // giving it its own purchased license would make its resolved license
        // ambiguous (reads give the purchased one precedence, silently shadowing
        // the grant). Reject and tell the caller to drop the grant first.
        private async Task EnsureNoComplimentaryCoverageAsync(uint unitId)
        {
            var grant = await _repo.GetActiveComplimentaryGrantForUnitAsync(unitId);
            if (grant != null)
                throw new InvalidOperationException(
                    $"BusinessUnit {unitId} already has complimentary coverage from license {grant.CompanyLicenseId}. " +
                    "Remove the complimentary grant first (Action=Remove, BusinessUnitId) before assigning it its own license.");
        }

        private async Task<LicenseAssignmentResultDto> BuildResultAsync(
            Infrastructure.Models.CompanyLicense license,
            Infrastructure.Models.AssignedCompanyLicense? active,
            uint? previous,
            string operation)
        {
            string? unitName = null;
            if (active != null)
            {
                unitName = await _db.BusinessUnits
                    .AsNoTracking()
                    .Where(b => b.Id == active.BusinessUnitId)
                    .Select(b => b.Name)
                    .FirstOrDefaultAsync();
            }

            // PreviousUnassignedAt mirrors the closed-row's UnassignedAt:
            //   * Reassign — the prior active row was closed in the same
            //     transaction as the new row's insert, so they share the
            //     same timestamp (active.AssignedAt).
            //   * Unassign — no new active row; use UtcNow (matches the
            //     UnassignedAt written by the repo a moment earlier).
            //   * Apply (no prior) / idempotent no-op — null.
            DateTime? previousUnassignedAt = previous.HasValue
                ? (active != null ? active.AssignedAt : DateTime.UtcNow)
                : null;

            var teamsUsed = await CountTeamsUsedAsync(license.Id);
            var maxTeams = await _db.LicenseTeamLimitAsync(license.Id);

            return new LicenseAssignmentResultDto
            {
                CompanyLicenseId = license.Id,
                LicenseKey = license.LicenseKey,
                ProfileId = license.ProfileId,
                CurrentBusinessUnitId = active?.BusinessUnitId,
                CurrentBusinessUnitName = unitName,
                AssignedAt = active?.AssignedAt,
                PreviousUnassignedAt = previousUnassignedAt,
                PreviousBusinessUnitId = previous,
                Operation = operation,
                TeamsUsed = teamsUsed,
                MaxTeams = maxTeams,
                TeamUsage = LicenseTeamLimits.FormatUsage(teamsUsed, maxTeams)
            };
        }

        // Result for a granted (or idempotent) complimentary assignment.
        // CurrentBusinessUnit* reflects the purchased/primary team; the extra
        // team lives in the Complimentary* fields.
        private async Task<LicenseAssignmentResultDto> BuildComplimentaryResultAsync(
            Infrastructure.Models.CompanyLicense license,
            Infrastructure.Models.ComplimentaryLicenseGrant grant,
            string operation)
        {
            var names = await GetUnitNamesAsync(grant.PrimaryBusinessUnitId, grant.ComplimentaryBusinessUnitId);
            var teamsUsed = await CountTeamsUsedAsync(license.Id);
            var maxTeams = await _db.LicenseTeamLimitAsync(license.Id);

            return new LicenseAssignmentResultDto
            {
                CompanyLicenseId = license.Id,
                LicenseKey = license.LicenseKey,
                ProfileId = license.ProfileId,
                CurrentBusinessUnitId = grant.PrimaryBusinessUnitId,
                CurrentBusinessUnitName = names.TryGetValue(grant.PrimaryBusinessUnitId, out var pn) ? pn : null,
                AssignedAt = grant.GrantedAt,
                Operation = operation,
                IsComplimentary = true,
                ComplimentaryBusinessUnitId = grant.ComplimentaryBusinessUnitId,
                ComplimentaryBusinessUnitName = names.TryGetValue(grant.ComplimentaryBusinessUnitId, out var cn) ? cn : null,
                ManagerUserId = grant.ManagerUserId,
                TeamsUsed = teamsUsed,
                MaxTeams = maxTeams,
                TeamUsage = LicenseTeamLimits.FormatUsage(teamsUsed, maxTeams)
            };
        }

        // Result for revoking a complimentary grant (Remove of a beneficiary unit).
        private async Task<LicenseAssignmentResultDto> BuildComplimentaryRemoveResultAsync(
            Infrastructure.Models.CompanyLicense license,
            Infrastructure.Models.ComplimentaryLicenseGrant grant)
        {
            var names = await GetUnitNamesAsync(grant.ComplimentaryBusinessUnitId);
            var teamsUsed = await CountTeamsUsedAsync(license.Id);
            var maxTeams = await _db.LicenseTeamLimitAsync(license.Id);

            return new LicenseAssignmentResultDto
            {
                CompanyLicenseId = license.Id,
                LicenseKey = license.LicenseKey,
                ProfileId = license.ProfileId,
                PreviousUnassignedAt = grant.RevokedAt ?? DateTime.UtcNow,
                PreviousBusinessUnitId = grant.ComplimentaryBusinessUnitId,
                Operation = "Remove",
                IsComplimentary = true,
                ComplimentaryBusinessUnitId = grant.ComplimentaryBusinessUnitId,
                ComplimentaryBusinessUnitName = names.TryGetValue(grant.ComplimentaryBusinessUnitId, out var cn) ? cn : null,
                ManagerUserId = grant.ManagerUserId,
                TeamsUsed = teamsUsed,
                MaxTeams = maxTeams,
                TeamUsage = LicenseTeamLimits.FormatUsage(teamsUsed, maxTeams)
            };
        }

        private async Task<Dictionary<uint, string>> GetUnitNamesAsync(params uint[] unitIds)
        {
            var ids = unitIds.Distinct().ToList();
            return await _db.BusinessUnits
                .AsNoTracking()
                .Where(b => ids.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id, b => b.Name);
        }
    }
}
