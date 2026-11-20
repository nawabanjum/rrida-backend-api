using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CloverBackoffice.Components.Enums;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Implementation;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CloverBackoffice.Services.Implementation
{
    /// <summary>
    /// Add User with multiple roles. Composes the services the existing flows use —
    /// IAccountRegistrationService creates the user exactly as upsert-user always
    /// has, ICompanyTeamService creates the team exactly as the team screens do —
    /// and adds the role rows, the team / portfolio relationship and one transaction
    /// around all of it.
    /// </summary>
    public partial class UserRoleAssignmentService : IUserRoleAssignmentService
    {
        private readonly ApplicationDbContext _context;
        private readonly IAccountRegistrationService _accountRegistration;
        private readonly ICompanyTeamService _companyTeamService;
        private readonly ICompanyLicenseService _licenseService;
        private readonly ITeamOnboardingNotificationService _onboardingNotification;
        private readonly ITeamManagerHandoverService _handover;
        private readonly ILogger<UserRoleAssignmentService> _logger;

        public UserRoleAssignmentService(
            ApplicationDbContext context,
            IAccountRegistrationService accountRegistration,
            ICompanyTeamService companyTeamService,
            ICompanyLicenseService licenseService,
            ITeamOnboardingNotificationService onboardingNotification,
            ITeamManagerHandoverService handover,
            ILogger<UserRoleAssignmentService> logger)
        {
            _handover = handover;
            _context = context;
            _accountRegistration = accountRegistration;
            _companyTeamService = companyTeamService;
            _licenseService = licenseService;
            _onboardingNotification = onboardingNotification;
            _logger = logger;
        }

        public async Task<AddUserOutcomeDto> AddOrAssignUserAsync(UserUpsertDto dto, int actorUserId, CancellationToken ct = default)
        {
            var role = dto.UserGroupId;
            var profileId = dto.ProfileId ?? 0;

            if (profileId <= 0)
                return AddUserOutcomeDto.Fail(ResponseMessages.Errors.InvalidProfileId);
            if (!RoleHierarchy.IsAssignable(role))
                return AddUserOutcomeDto.Fail(ResponseMessages.Errors.InvalidUserGroup);

            // ── Who is the user? ─────────────────────────────────────────────
            //   userId > 0            edit that user
            //   email of a live user  same profile → give them the role (no duplicate)
            //                         other profile → refused
            //   otherwise             new user, invited
            User? existing;
            if (dto.UserId > 0)
            {
                existing = await _context.Users.AsNoTracking()
                    .FirstOrDefaultAsync(u => u.UserId == dto.UserId && !u.IsDel, ct);
                // Profile is part of the rule: another profile's user is "not found",
                // so a crafted userId cannot edit or move somebody else's user.
                if (existing == null || existing.ProfileId != profileId)
                    return AddUserOutcomeDto.Fail(ResponseMessages.Errors.UserNotFound);
            }
            else
            {
                existing = await _accountRegistration.UserEmailExistsAsync(dto.Email);
                if (existing != null && existing.ProfileId != profileId)
                    return AddUserOutcomeDto.Fail(ResponseMessages.Errors.UserEmailInAnotherProfile);
            }

            var isNewUser = existing == null;
            var isEmailMatch = dto.UserId == 0 && existing != null;
            var targetUserId = existing?.UserId ?? 0;

            var heldRoles = isNewUser ? new List<int>() : await GetRoleIdsAsync(targetUserId, ct);
            if (heldRoles.Contains((int)UserGroups.RridaAdmin))
                return AddUserOutcomeDto.Fail(ResponseMessages.Errors.RoleNotAssignableToRridaAdmin);

            var roleAlreadyHeld = heldRoles.Contains(role);

            // ── Validate the relationship. Nothing is written yet. ───────────
            TeamPlan? team = null;
            PortfolioPlan? portfolio = null;

            if (role == (int)UserGroups.TeamManager)
            {
                var requested = dto.IsNewTeam || dto.ExistingUnitId is > 0;
                if (!requested && !roleAlreadyHeld)
                    return AddUserOutcomeDto.Fail(ResponseMessages.Errors.ManagerTeamRequired);

                if (requested)
                {
                    var (plan, outcome) = await PlanTeamAsync(dto, (uint)profileId, targetUserId, ct);
                    if (outcome != null) return outcome;
                    team = plan;

                    // Manager licence team limit. A new user has no teams yet.
                    if (team != null && !team.AlreadyManaged && targetUserId > 0)
                    {
                        var limitError = await _context.ManagerTeamLimitErrorAsync(targetUserId, team.UnitId ?? 0, ct);
                        if (limitError != null) return AddUserOutcomeDto.Fail(limitError);
                    }
                }
            }
            else if (role == (int)UserGroups.PortfolioManager)
            {
                var requested = dto.ManagerId is > 0;
                if (!requested && !roleAlreadyHeld)
                    return AddUserOutcomeDto.Fail(ResponseMessages.Errors.PortfolioManagerManagerRequired);

                if (requested)
                {
                    var (plan, outcome) = await PlanPortfolioAsync(dto, (uint)profileId, targetUserId, ct);
                    if (outcome != null) return outcome;
                    portfolio = plan;
                }
            }

            var nothingToDo = roleAlreadyHeld
                              && (team == null || team.AlreadyManaged)
                              && (portfolio == null || portfolio.AlreadyInPortfolio);
            if (isEmailMatch && nothingToDo)
                return AddUserOutcomeDto.Fail(ResponseMessages.Errors.UserAlreadyAssigned);

            // ── Write ────────────────────────────────────────────────────────
            var assignment = new UserRoleAssignmentResultDto { UserGroupId = role };
            UserUpsertDto user;
            uint? assignedUnitId = null;
            uint? pendingUnitId = null;
            int? replacedManagerId = null;
            int primary;

            await using var transaction = await _context.Database.BeginTransactionAsync(ct);
            try
            {
                // Team capacity first, under the lock, before anything is written.
                // The user is never blocked for lack of a licence: a new Team
                // Manager gets a free one after the commit when one exists.
                var licenseOutcome = await CheckLicenseAsync(team, (uint)profileId, ct);
                if (licenseOutcome != null)
                {
                    await transaction.RollbackAsync(ct);
                    return licenseOutcome;
                }

                if (isEmailMatch)
                {
                    // The admin asked for a role, not an edit: the user's name and
                    // email stay exactly as they are.
                    var parts = (existing!.Name ?? string.Empty).Trim().Split(' ', 2);
                    user = new UserUpsertDto
                    {
                        UserId = existing.UserId,
                        Email = existing.Username,
                        FirstName = parts.Length > 0 ? parts[0] : string.Empty,
                        LastName = parts.Length > 1 ? parts[1] : string.Empty,
                        ProfileId = profileId
                    };
                }
                else
                {
                    // The ordinary create / edit path, unchanged.
                    user = await _accountRegistration.UpsertUserAsync(new UserUpsertDto
                    {
                        UserId = dto.UserId,
                        Email = dto.Email,
                        FirstName = dto.FirstName,
                        LastName = dto.LastName,
                        ProfileId = profileId,
                        CreatedBy = actorUserId,
                        UserGroupId = role
                    });

                    if (user == null || user.UserId <= 0)
                    {
                        await transaction.RollbackAsync(ct);
                        return AddUserOutcomeDto.Fail(ResponseMessages.Errors.UserUpsertFail);
                    }
                }

                targetUserId = user.UserId;

                var (roleAdded, primaryRole) = await EnsureRoleAsync(targetUserId, role, actorUserId, isNewUser, ct);
                assignment.RoleWasAdded = roleAdded;
                primary = primaryRole;

                if (team != null)
                {
                    uint unitId;
                    if (team.CreateNew)
                    {
                        var created = await _companyTeamService.UpsertBusinessUnitAsync(new UpsertBusinessUnitDto
                        {
                            Id = 0,
                            TeamName = team.Name,
                            ProfileId = profileId,
                            ParentId = null,
                            TeamSize = null,
                            CreatedBy = actorUserId,

                            // Team capacity was already checked under the lock a few
                            // lines up; the create must not check it again.
                            LicenseAlreadyReserved = true
                        });

                        if (created == null || created.Id <= 0)
                        {
                            await transaction.RollbackAsync(ct);
                            return AddUserOutcomeDto.Fail(string.IsNullOrWhiteSpace(created?.message)
                                ? ResponseMessages.Errors.ManagerTeamCreateFailed
                                : created!.message);
                        }

                        // No licence is consumed by the team. Its licence coverage
                        // (the manager's licence) is given after the commit.
                        unitId = (uint)created.Id;
                    }
                    else
                    {
                        unitId = team.UnitId!.Value;
                    }

                    if (!team.AlreadyManaged)
                    {
                        var incumbents = await _context.UserTeams
                            .Where(ut => ut.UnitId == unitId && !ut.IsUnAssigned)
                            .ToListAsync(ct);

                        // Re-checked inside the transaction: someone may have taken
                        // the team since validation, and that must not be replaced
                        // without the admin having confirmed it.
                        if (incumbents.Any(i => i.UserId != targetUserId) && !dto.ReplaceExistingManager)
                        {
                            await transaction.RollbackAsync(ct);
                            return AddUserOutcomeDto.Fail(ResponseMessages.Errors.ManagerCreateFailed);
                        }

                        // Whatever was pending on this team is superseded by this decision.
                        await _handover.CancelPendingForUnitAsync(unitId, ct);

                        // Replacing a manager with someone who has not accepted their
                        // invitation yet must not leave the team under a Pending
                        // account. The outgoing manager keeps the team; the new row
                        // waits (unassigned) until the invitation is accepted, and
                        // TeamManagerHandoverService finalizes the swap then.
                        var displacing = incumbents.Where(i => i.UserId != targetUserId).ToList();
                        var waitForActivation = displacing.Count > 0
                                                && await _handover.IsAwaitingActivationAsync(targetUserId, ct);

                        if (waitForActivation)
                        {
                            await _context.SaveChangesAsync(ct);

                            _context.UserTeams.Add(new UserTeam
                            {
                                UserId = targetUserId,
                                UnitId = unitId,
                                IsUnAssigned = true,
                                IsPendingActivation = true,
                                IsReadOnly = false,
                                CreatedBy = actorUserId,
                                CreatedDate = DateTime.UtcNow,
                                IsNewTeam = false
                            });

                            pendingUnitId = unitId;
                            assignment.ReplacementPendingActivation = true;
                            replacedManagerId = displacing[0].UserId;
                        }
                        else
                        {
                            foreach (var row in incumbents)
                            {
                                row.IsUnAssigned = true;
                                if (row.UserId != targetUserId)
                                    replacedManagerId = row.UserId;
                            }

                            // Flipped rows saved first, then the new row, so the order
                            // never depends on EF's batching.
                            await _context.SaveChangesAsync(ct);

                            _context.UserTeams.Add(new UserTeam
                            {
                                UserId = targetUserId,
                                UnitId = unitId,
                                IsUnAssigned = false,
                                IsReadOnly = false,
                                CreatedBy = actorUserId,
                                CreatedDate = DateTime.UtcNow,
                                IsNewTeam = team.CreateNew
                            });

                            assignedUnitId = unitId;
                        }
                    }

                    assignment.UnitId = unitId;
                    assignment.TeamName = team.Name;
                    assignment.TeamWasCreated = team.CreateNew;
                    assignment.ReplacedManagerUserId = replacedManagerId;
                }

                if (portfolio != null)
                {
                    if (!portfolio.AlreadyInPortfolio)
                    {
                        var current = await _context.PortfolioManagerAssignments
                            .Where(a => a.ManagerUserId == portfolio.ManagerId && !a.IsUnAssigned)
                            .ToListAsync(ct);

                        if (current.Any(a => a.PortfolioManagerUserId != targetUserId) && !dto.ReplaceExistingPortfolioManager)
                        {
                            await transaction.RollbackAsync(ct);
                            return AddUserOutcomeDto.Fail(ResponseMessages.Errors.UserUpsertFail);
                        }

                        foreach (var row in current)
                        {
                            row.IsUnAssigned = true;
                            row.UnAssignedDate = DateTime.UtcNow;
                            if (row.PortfolioManagerUserId != targetUserId)
                                assignment.ReplacedPortfolioManagerUserId = row.PortfolioManagerUserId;
                        }

                        // Partial unique index on ManagerUserId: flip first, insert second.
                        await _context.SaveChangesAsync(ct);

                        _context.PortfolioManagerAssignments.Add(new PortfolioManagerAssignment
                        {
                            PortfolioManagerUserId = targetUserId,
                            ManagerUserId = portfolio.ManagerId,
                            ProfileId = (uint)profileId,
                            IsUnAssigned = false,
                            CreatedBy = actorUserId,
                            CreatedDate = DateTime.UtcNow
                        });
                    }

                    assignment.ManagerId = portfolio.ManagerId;
                    assignment.ManagerName = portfolio.ManagerName;
                }

                await _context.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            catch
            {
                try { await transaction.RollbackAsync(ct); } catch { /* already gone */ }
                throw;
            }

            // A user who just became a Team Manager gets one free licence, when
            // one exists. Before team coverage, so their team rides on it. No
            // licence free → the user is still created, unlicensed. Other roles
            // never take one. A new user's role row is written by the
            // ApplicationDbContext hook, so RoleWasAdded is false for them —
            // isNewUser covers that case.
            if (role == (int)UserGroups.TeamManager && (assignment.RoleWasAdded || isNewUser))
            {
                try
                {
                    var licenseId = await _licenseService.AssignLicenseToUserAsync((uint)profileId, targetUserId, actorUserId, dto.PlanCode);
                    if (licenseId.HasValue)
                    {
                        assignment.LicenseId = licenseId;
                        assignment.LicenseKey = await _context.CompanyLicenses.AsNoTracking()
                            .Where(l => l.Id == licenseId.Value)
                            .Select(l => l.LicenseKey)
                            .FirstOrDefaultAsync(ct);
                    }
                }
                catch (Exception ex) { _logger.LogWarning(ex, "Licence auto-assign failed for user {UserId}.", targetUserId); }
            }

            if (assignedUnitId.HasValue)
                await AfterTeamAssignedAsync(targetUserId, assignedUnitId.Value, replacedManagerId, isNewUser, actorUserId, ct);

            // The licence now covering a team created here (purchased or the
            // manager's shared licence), reported as before.
            if (assignment.TeamWasCreated && assignment.LicenseId == null && assignment.UnitId is long coveredUnitId)
            {
                var covering = await _context.CompanyLicenses.AsNoTracking()
                    .Where(l => _context.AssignedCompanyLicenses.Any(a =>
                                    a.CompanyLicenseId == l.Id && a.BusinessUnitId == (uint)coveredUnitId && a.IsActive && !a.IsDel)
                                || _context.ComplimentaryLicenseGrants.Any(g =>
                                    g.CompanyLicenseId == l.Id && g.ComplimentaryBusinessUnitId == (uint)coveredUnitId && g.IsActive && !g.IsDel))
                    .Select(l => new { l.Id, l.LicenseKey })
                    .FirstOrDefaultAsync(ct);
                assignment.LicenseId = covering?.Id;
                assignment.LicenseKey = covering?.LicenseKey;
            }

            // Pending replacement: nothing on the team changed yet, so no licence
            // reconciliation and no "unassigned" email - both happen on acceptance.
            // A new user's assignment email is suppressed as usual.
            if (pendingUnitId.HasValue && isNewUser)
            {
                try
                {
                    await _onboardingNotification.SuppressAssignmentNotificationAsync(
                        targetUserId, pendingUnitId.Value, "user added with this team; the invitation email covers it", ct);
                }
                catch (Exception ex) { _logger.LogWarning(ex, "Assignment notification failed for user {UserId}, unit {UnitId}.", targetUserId, pendingUnitId); }
            }

            user.UserGroupId = primary;
            user.Roles = await GetRolesAsync(targetUserId, ct);
            user.Assignment = assignment;
            user.IsExistingUser = isEmailMatch;

            if (!isNewUser)
            {
                user.FirstTimeUserAdded = await _context.Users
                    .CountAsync(u => u.ProfileId == profileId && !u.IsDel, ct) == 2;
            }

            return new AddUserOutcomeDto
            {
                IsSuccess = true,
                IsNewUser = isNewUser,
                IsExistingUserAssignment = isEmailMatch,
                Data = user
            };
        }

        /// <summary>
        /// The same side effects assign-user-to-team and create-manager have, after
        /// the commit and best-effort: licence reconciliation, the assignment email
        /// (suppressed for a new user — the invitation covers it), and the
        /// "you have been unassigned" email to a displaced manager.
        /// </summary>
        private async Task AfterTeamAssignedAsync(
            int userId, uint unitId, int? replacedManagerId, bool isNewUser, int actorUserId, CancellationToken ct)
        {
            // Teams riding on this team's licence lose their grant if the manager
            // changed (reconciliation below); they are re-covered after it.
            var ridingTeamIds = await _context.ComplimentaryLicenseGrants.AsNoTracking()
                .Where(g => g.PrimaryBusinessUnitId == unitId && g.IsActive && !g.IsDel)
                .Select(g => g.ComplimentaryBusinessUnitId)
                .ToListAsync(ct);

            try { await _licenseService.ReconcileGrantsForUnitAsync(unitId, actorUserId); }
            catch (Exception ex) { _logger.LogWarning(ex, "Licence reconciliation failed for unit {UnitId}.", unitId); }

            // Licence coverage for the team — only from the manager's own licence
            // (auto-assigned above, or one they already had). Never another free one.
            foreach (var coverUnitId in ridingTeamIds.Prepend(unitId))
            {
                try { await _licenseService.EnsureTeamCoverageAsync(coverUnitId, actorUserId, allowFreeLicense: false); }
                catch (Exception ex) { _logger.LogWarning(ex, "Licence coverage failed for unit {UnitId}.", coverUnitId); }
            }

            try
            {
                if (isNewUser)
                    await _onboardingNotification.SuppressAssignmentNotificationAsync(
                        userId, unitId, "user added with this team; the invitation email covers it", ct);
                else
                    await _onboardingNotification.NotifyManagerAssignedAsync(userId, unitId, ct);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Assignment notification failed for user {UserId}, unit {UnitId}.", userId, unitId); }

            if (replacedManagerId.HasValue)
            {
                try { await _onboardingNotification.NotifyManagerUnassignedAsync(replacedManagerId.Value, unitId, ct); }
                catch (Exception ex) { _logger.LogWarning(ex, "Unassigned notification failed for user {UserId}.", replacedManagerId); }
            }
        }
    }
}
