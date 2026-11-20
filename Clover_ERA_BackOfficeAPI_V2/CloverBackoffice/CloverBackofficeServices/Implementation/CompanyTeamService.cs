using CloverBackoffice.Components;
using CloverBackoffice.Components.Enums;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Implementation;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static CloverBackoffice.Infrastructure.DTO.LoginDto;

namespace CloverBackoffice.Services.Implementation
{
    public class CompanyTeamService : ICompanyTeamService
    {
        private readonly ICompanyTeamRepository _repository;
        private readonly ApplicationDbContext _context;
        private readonly IAggregateDashboardRepository _aggregateRepo;
        private readonly ICompanySubscriptionRepository _subscriptionRepo;
        private readonly ICompanyLicenseService _licenseService;
        private readonly ILicensedPeriodService _licensedPeriod;
        private readonly ITeamOnboardingNotificationService _onboardingNotification;
        public CompanyTeamService(ICompanyTeamRepository repository, ApplicationDbContext context, IAggregateDashboardRepository aggregateRepo, ICompanySubscriptionRepository subscriptionRepo, ICompanyLicenseService licenseService, ILicensedPeriodService licensedPeriod, ITeamOnboardingNotificationService onboardingNotification)
        {
            _onboardingNotification = onboardingNotification;
            _repository = repository;
            _context = context;
            _aggregateRepo = aggregateRepo;
            _subscriptionRepo = subscriptionRepo;
            _licenseService = licenseService;
            _licensedPeriod = licensedPeriod;
        }

        // First key of pg_advisory_xact_lock(int, int); the second is the profile id.
        // The same key Add User uses, so both paths queue behind one another when
        // they race for the last free licence of a profile.
        private const int LicenseLockNamespace = 16016;

        // First key of pg_advisory_xact_lock(int, int); the second is the team id.
        // Two admins moving the same team at once queue here, so the team never
        // ends up with two active managers.
        private const int TeamManagerLockNamespace = 16020;

        // Best-effort revoke of complimentary grants that lost their shared
        // manager because of a UserTeam change on `unitId`. Never lets a
        // reconciliation hiccup fail the manager operation itself — the
        // reconciliation background job is the backstop.
        private async Task ReconcileComplimentaryGrantsAsync(uint unitId, int? actorUserId)
        {
            try
            {
                await _licenseService.ReconcileGrantsForUnitAsync(unitId, actorUserId);
            }
            catch
            {
                // Swallowed by design; the safety-net job will catch it.
            }
        }

        // Best-effort licence coverage for a team (never consumes a licence). A
        // failure must not fail the team / manager operation that already committed.
        private async Task EnsureTeamCoverageAsync(uint unitId, int? actorUserId)
        {
            try
            {
                await _licenseService.EnsureTeamCoverageAsync(unitId, actorUserId);
            }
            catch
            {
                // Swallowed by design; the next manager change on the team retries it.
            }
        }

        public async Task<UpsertBusinessUnitDto> UpsertBusinessUnitAsync(UpsertBusinessUnitDto dto)
        {
            var company = await _context.Profiles.FirstOrDefaultAsync(c => c.Id == dto.ProfileId);
            if (company == null)
            {
                dto.message = "Invalid ProfileId";
                return dto;
            }
            dto.ParentId = dto.ParentId == 0 ? null : dto.ParentId;
            if (dto.ParentId.HasValue)
            {
                var PBusinessUnits = await _context.BusinessUnits.FirstOrDefaultAsync(c => c.Profile.Id == dto.ProfileId && c.Id == dto.ParentId.Value);
                if (PBusinessUnits == null)
                {
                    dto.message = "Invalid ParentId";
                    return dto;
                }

            }

            var businessUnitCodes = BussinessManager.GenerateCodes();

            // The unit's contact is now three columns on the unit itself. Same values
            // the old Contacts row carried, so the generated placeholder address is
            // unchanged.
            var contactFirstName = dto.TeamName;
            var contactLastName = dto.TeamName;
            var contactEmailAddress = $"default{businessUnitCodes}@{company.DomainName}";


            string fullName = dto.TeamName.Trim();

            if (dto.Id == 0)
            {
                // A licence is consumed by a Team Manager, not by a team. A new team
                // only has to fit the profile's team capacity (licences x 3), checked
                // here under the per-profile advisory lock. Add User checks it itself
                // and sets LicenseAlreadyReserved, so it is not checked twice.
                //
                // The lock lives for the transaction, so the create runs in one: the
                // caller's when they already opened it (Add User, manager onboarding),
                // otherwise one opened here. Without it two admins could both see the
                // last free team slot and both create a team.
                var transaction = _context.Database.CurrentTransaction == null
                    ? await _context.Database.BeginTransactionAsync()
                    : null;

                try
                {
                    if (!dto.LicenseAlreadyReserved)
                    {
                        await _context.Database.ExecuteSqlInterpolatedAsync(
                            $"SELECT pg_advisory_xact_lock({LicenseLockNamespace}, {dto.ProfileId})");

                        var capacityError = (await _licenseService.GetLicenseUsageAsync((uint)dto.ProfileId)).TeamCapacityError();
                        if (capacityError != null)
                        {
                            if (transaction != null) await transaction.RollbackAsync();
                            dto.message = capacityError;
                            return dto;
                        }
                    }

                    var dailyQuestionSetGroup = await _context.DailyQuestionSetGroups.FirstOrDefaultAsync(g => g.Id == 1);
                    var unit = new BusinessUnit
                    {
                        BusinessUnitCode = businessUnitCodes.Code,
                        BusinessUnitShortCode = businessUnitCodes.ShortCode,
                        Name = fullName,
                        NameInvariant = fullName + businessUnitCodes,
                        RegistrationDate = DateTime.UtcNow,
                        Profile = company,
                        ContactFirstName = contactFirstName,
                        ContactLastName = contactLastName,
                        ContactEmailAddress = contactEmailAddress,
                        ParentId = dto.ParentId,
                        TeamSize = dto.TeamSize,
                        ActiveDailyQuestionSetGroupId = dailyQuestionSetGroup.Id,
                        ActiveDailyQuestionSetGroup = dailyQuestionSetGroup
                    };

                    await _repository.AddAsync(unit);
                    dto.Id = (int)unit.Id;

                    if (transaction != null) await transaction.CommitAsync();
                }
                catch
                {
                    if (transaction != null) await transaction.RollbackAsync();
                    throw;
                }
                finally
                {
                    if (transaction != null) await transaction.DisposeAsync();
                }

                // Licence coverage for the new team (what licence-gated reads look
                // at). A caller with its own transaction does this after its commit,
                // once the team has its manager.
                if (transaction != null)
                    await EnsureTeamCoverageAsync((uint)dto.Id, dto.CreatedBy == 0 ? null : dto.CreatedBy);

                dto.message = "Businessteam unit created successfully";
            }
            else
            {
                var existing = await _repository.GetByIdAsync(dto.Id);
                if (existing == null)
                {
                    dto.message = "BusinessUnit not found";
                    return dto;
                }

                // Detach the existing tracked entity
                _context.Entry(existing).State = EntityState.Detached;
                var fixedRegistrationDate = DateTime.SpecifyKind(existing.RegistrationDate, DateTimeKind.Utc);
                var dailyQuestionSetGroup = await _context.DailyQuestionSetGroups.FirstOrDefaultAsync(g => g.Id == 1);
                var unit = new BusinessUnit
                {
                    Id = existing.Id,
                    BusinessUnitCode = existing.BusinessUnitCode,
                    BusinessUnitShortCode = existing.BusinessUnitShortCode,
                    Name = fullName,
                    NameInvariant = fullName,
                    RegistrationDate = fixedRegistrationDate,
                    Profile = existing.Profile,
                    ContactFirstName = existing.ContactFirstName,
                    ContactLastName = existing.ContactLastName,
                    ContactEmailAddress = existing.ContactEmailAddress,
                    ParentId = dto.ParentId,
                    // Optional on edit: a null leaves the stored team size untouched.
                    TeamSize = dto.TeamSize ?? existing.TeamSize,
                    ActiveDailyQuestionSetGroupId = dailyQuestionSetGroup.Id,
                    ActiveDailyQuestionSetGroup = dailyQuestionSetGroup
                };

                await _repository.UpdateAsync(unit);

                // Echo back what was actually stored so the caller sees the effective size.
                dto.TeamSize = unit.TeamSize;
                dto.message = "Businessteam unit updated successfully";
            }

            return dto;
        }

        public async Task<bool> DeleteAsync(uint id, int? actorUserId = null)
        {
            // Snapshot the unit + all descendants BEFORE the soft-delete so we can
            // free every license held anywhere in the subtree being removed
            // (delete only touches BusinessUnits/Contacts, so the assignment rows
            // stay resolvable afterwards).
            var affectedUnitIds = await _aggregateRepo.GetBusinessUnitHierarchyRecursiveAsync(id);

            var deleted = await _repository.DeleteAsync(id);
            if (!deleted) return false;

            // Unassign purchased licenses and revoke complimentary grants so the
            // licenses are no longer tied to the deleted teams and become
            // available for reassignment.
            foreach (var unitId in affectedUnitIds)
                await _licenseService.ReleaseLicensesForUnitAsync((uint)unitId, actorUserId);

            return true;
        }

        public async Task<UserTeam?> AssignUserToTeamAsync(UserTeamDto dto, int createdBy)
        {
            // Check if user exists
            bool userExists = await _repository.UserExistsAsync(dto.UserId);
            if (!userExists)
                return null;

            // Check if Unit exists
            bool unitExists = await _repository.UnitExistsAsync((int)dto.UnitId);
            if (!unitExists)
                return null;

            // Check if user is already assigned to THIS specific team (UserId + UnitId) with IsUnAssigned = false
            var existingActiveAssignment = await _repository.GetUserActiveAssignmentAsync(dto.UserId, (int)dto.UnitId);

            // If already assigned to this team, return the existing assignment (do nothing - idempotent)
            if (existingActiveAssignment != null)
            {
                // The assignment itself is a no-op, but the NOTIFICATION may
                // still be outstanding: the manager could have been assigned
                // before this feature existed, or the original send could have
                // failed. Re-clicking Assign is the natural way an admin retries
                // "the manager never got the email", and this used to return
                // here silently — no mail, no audit row, nothing to diagnose.
                //
                // NotifyManagerAssignedAsync is idempotent per (team, manager),
                // so a manager who was already told is not told twice; they get
                // an audited "already notified" line instead.
                try
                {
                    await _onboardingNotification.NotifyManagerAssignedAsync(dto.UserId, dto.UnitId);
                }
                catch
                {
                    // Swallowed by design; the service logs its own failures.
                }

                return existingActiveAssignment;
            }

            // Manager licence team limit: a new team for this user must fit.
            // Thrown so the controller can return the reason to the caller.
            var limitError = await _context.ManagerTeamLimitErrorAsync(dto.UserId, dto.UnitId);
            if (limitError != null)
                throw new InvalidOperationException(limitError);

            // Teams riding on this team's licence as complimentary grants. If the
            // manager changes, those grants lose their shared manager and are
            // revoked below; the teams are re-covered afterwards.
            var ridingTeamIds = await _context.ComplimentaryLicenseGrants
                .Where(g => g.PrimaryBusinessUnitId == dto.UnitId && g.IsActive && !g.IsDel)
                .Select(g => g.ComplimentaryBusinessUnitId)
                .ToListAsync();

            // Use transaction to ensure update happens before insert, and both are atomic
            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock({TeamManagerLockNamespace}, {(int)dto.UnitId})");

                // Check if the unit already has an active user assigned (any user with this UnitId and IsUnAssigned = false)
                // A unit can only have one active user assignment at a time
                var unitActiveAssignment = await _repository.GetUnitActiveAssignmentAsync((int)dto.UnitId);

                // STEP 1: If unit has an active assignment, unassign the previous user (preserve historical record)
                // This MUST happen before inserting the new record
                if (unitActiveAssignment != null)
                {
                    unitActiveAssignment.IsUnAssigned = true;
                    _context.UserTeams.Update(unitActiveAssignment);
                }

                // A replacement still waiting for an invited manager to accept is
                // superseded by this explicit assignment, so accepting later can
                // never take the team away from the manager assigned here.
                var pendingOnUnit = await _context.UserTeams
                    .Where(ut => ut.UnitId == dto.UnitId && ut.IsPendingActivation)
                    .ToListAsync();
                foreach (var pending in pendingOnUnit)
                {
                    pending.IsPendingActivation = false;
                    pending.IsUnAssigned = true;
                }

                // Captured before the commit so the outgoing manager can be told
                // after it. Null when the team had no manager, and skipped when
                // the same person is simply being re-saved onto their own team.
                var displacedUserId = unitActiveAssignment != null && unitActiveAssignment.UserId != dto.UserId
                    ? (int?)unitActiveAssignment.UserId
                    : null;

                // STEP 2: Create new assignment with IsUnAssigned = false
                // Note: A user can still be assigned to multiple different units simultaneously
                // But each unit can only have one active user at a time
                var newAssignment = new UserTeam
                {
                    UserId = dto.UserId,
                    UnitId = dto.UnitId,
                    IsUnAssigned = false,
                    IsReadOnly = dto.IsReadOnly ?? false, // Default to false if null
                    CreatedBy = createdBy,
                    CreatedDate = DateTime.UtcNow
                };

                _context.UserTeams.Add(newAssignment);
                
                // Save both operations (update old record, then insert new record) in correct order
                await _context.SaveChangesAsync();

                // Commit transaction
                await transaction.CommitAsync();

                // Reassigning a unit's manager can break the shared-manager basis
                // of any complimentary grant on this unit — drop stale ones.
                await ReconcileComplimentaryGrantsAsync(dto.UnitId, createdBy);

                // A team moving between licensed managers keeps licence coverage,
                // now from its new manager's licence. No licence is consumed.
                await EnsureTeamCoverageAsync(dto.UnitId, createdBy);
                foreach (var ridingTeamId in ridingTeamIds)
                    await EnsureTeamCoverageAsync(ridingTeamId, createdBy);

                // Tell the new manager the team is theirs. This is the first
                // point at which a newly created team HAS a manager to notify —
                // upsert-team creates the team with nobody assigned — so it is
                // also where the day 2 / day 5 onboarding clock starts.
                //
                // Best-effort: the assignment is committed above and must not be
                // undone by a mail or notification failure.
                try
                {
                    await _onboardingNotification.NotifyManagerAssignedAsync(dto.UserId, dto.UnitId);
                }
                catch
                {
                    // Swallowed by design; the service already contains and logs
                    // its own failures.
                }

                // Reassigning a team silently removes the previous manager. They
                // stop receiving that team's reports and alerts, so they are told
                // too — the same message the explicit unassign endpoint sends.
                if (displacedUserId.HasValue)
                {
                    try
                    {
                        await _onboardingNotification.NotifyManagerUnassignedAsync(displacedUserId.Value, dto.UnitId);
                    }
                    catch
                    {
                        // Same contract: the reassignment is committed.
                    }
                }

                return newAssignment;
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<UserTeam?> UnAssignUserFromTeamAsync(long userId, int unitId)
        {
            var userTeam = await _repository.GetUserTeamAsync(userId, unitId);

            // Record not exists
            if (userTeam == null)
                return null;

            // Already unassigned → return as is
            if (userTeam.IsUnAssigned)
                return userTeam;

            // Update IsUnAssigned to true
            userTeam.IsUnAssigned = true;
            _context.UserTeams.Update(userTeam);
            await _context.SaveChangesAsync();

            // Removing this unit's manager may leave a complimentary grant without
            // a shared manager — revoke it.
            await ReconcileComplimentaryGrantsAsync((uint)unitId, null);

            // Tell the manager they no longer hold this team. Best-effort: the
            // unassignment is committed above and must not be undone by a mail
            // failure.
            try
            {
                await _onboardingNotification.NotifyManagerUnassignedAsync(userTeam.UserId, (uint)unitId);
            }
            catch
            {
                // Swallowed by design; the service contains and logs its own failures.
            }

            return userTeam;
        }
        public async Task<TeamNotificationDto> GetTeamNotificationsAsync(int profileId, int userId)
        {
            var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId);
            if (user == null)
                return new TeamNotificationDto();

            // A RRIDA Admin and a Profile Admin both see every team in the profile;
            // the difference is which profiles they may ask about at all, which the
            // tenant guard has already settled by the time we get here.
            if (user.UserGroupId == (int)UserGroups.RridaAdmin ||
                user.UserGroupId == (int)UserGroups.ProfileAdmin)
                return await _repository.GetTeamNotificationsAsync(profileId);

            // Manager: only count their assigned teams + all descendants
            if (user.UserGroupId == (int)UserGroups.TeamManager)
            {
                var filteredUnitIds = await GetTeamManagerUnitIdsAsync(profileId, userId);
                return await _repository.GetTeamNotificationsAsync(profileId, filteredUnitIds);
            }

            return new TeamNotificationDto();
        }

        private async Task<List<int>> GetTeamManagerUnitIdsAsync(int profileId, int userId)
        {
            // Get root units directly assigned to this manager
            var rootUnitIds = await _context.UserTeams
                .Where(ut => ut.UserId == userId && ut.IsUnAssigned == false)
                .Select(ut => (int)ut.UnitId)
                .Distinct()
                .ToListAsync();

            if (!rootUnitIds.Any()) return new List<int>();

            // Get all units in the company to traverse children
            var allUnits = await _context.BusinessUnits
                .Where(bu => bu.Profile.Id == profileId && bu.IsDel != true)
                .Select(bu => new { Id = (int)bu.Id, ParentId = bu.ParentId })
                .ToListAsync();

            // Collect all descendant unit IDs recursively
            var result = new HashSet<int>(rootUnitIds);
            var queue = new Queue<int>(rootUnitIds);

            while (queue.Count > 0)
            {
                var parentId = queue.Dequeue();
                var children = allUnits.Where(u => u.ParentId == parentId).Select(u => u.Id);
                foreach (var child in children)
                {
                    if (result.Add(child))
                        queue.Enqueue(child);
                }
            }

            return result.ToList();
        }

        public async Task<GetCompanyUnitsWithHierarchyDto?> GetCompanyUnitsWithHierarchy(int profileId, int userId)
        {

            var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId);
            if (user == null)
                return new GetCompanyUnitsWithHierarchyDto();

            GetCompanyUnitsWithHierarchyDto result = null;
            switch (user.UserGroupId)
            {
                case (int)UserGroups.TeamManager:
                    result = TeamManagerAdmin(profileId, userId);
                    break;
                case (int)UserGroups.RridaAdmin:
                case (int)UserGroups.ProfileAdmin:
                    result = CloveraAdmin(profileId);
                    break;
            }

            if (result?.Units != null && result.Units.Any())
            {
                // Licensed intervals per team → metrics exclude data generated
                // while the team had no active (purchased or complimentary) license.
                var resultUnitIds = new List<long>();
                CollectUnitIds(result.Units, resultUnitIds);
                var resultIntervals = await _licensedPeriod.GetLicensedIntervalsAsync(resultUnitIds);

                await PopulateTrendChartsAsync(result.Units, resultIntervals);
                await PopulateLastActionHubUpdatedAtAsync(result.Units, resultIntervals);
                await PopulateTeamStatusAsync(result.Units, resultIntervals);
                // Summary is built AFTER PopulateUnitSubscriptionAndLicense below —
                // that pass can still blank an unlicensed team's metrics, and a
                // summary computed here would count values the rows no longer show.
            }

            // Per-unit subscription banner + license details. One company-
            // subscription read and one latest-assignment-per-unit read feed
            // every unit. The subscription banner is the same for the whole
            // company, so compute it once and stamp it on each unit.
            CompanySubscription? subscription = null;
            var latestAssignmentByUnit = new Dictionary<uint, AssignedCompanyLicense>();
            // Beneficiary unit -> the unit holding the purchased license it rides on.
            var complimentaryParentByUnit = new Dictionary<uint, uint>();
            if (result != null)
            {
                (subscription, latestAssignmentByUnit, complimentaryParentByUnit) = await LoadLicenseLookupsAsync(profileId);

                if (result.Units != null)
                {
                    PopulateUnitSubscriptionAndLicense(result.Units, latestAssignmentByUnit, subscription, complimentaryParentByUnit);
                    result.Summary = BuildTeamStructureSummary(result.Units);
                }
            }

            // For Team Manager only: also attach full company structure (limited view for Team Structure tab)
            if (user.UserGroupId == (int)UserGroups.TeamManager && result != null)
            {
                var fullStructure = CloveraAdmin(profileId);
                if (fullStructure?.Units != null && fullStructure.Units.Any())
                {
                    var overallUnitIds = new List<long>();
                    CollectUnitIds(fullStructure.Units, overallUnitIds);
                    var overallIntervals = await _licensedPeriod.GetLicensedIntervalsAsync(overallUnitIds);

                    await PopulateTrendChartsAsync(fullStructure.Units, overallIntervals);
                    await PopulateLastActionHubUpdatedAtAsync(fullStructure.Units, overallIntervals);
                    await PopulateTeamStatusAsync(fullStructure.Units, overallIntervals);
                    PopulateUnitSubscriptionAndLicense(fullStructure.Units, latestAssignmentByUnit, subscription, complimentaryParentByUnit);
                }
                result.OverallTeamStructure = fullStructure?.Units ?? new List<BusinessUnitTeamDto>();
            }

            // For Partner Company Admin (isPartner = true) viewing a partner company,
            // hide sensitive fields from the units response
            if (user.UserGroupId == (int)UserGroups.ProfileAdmin && result?.Units != null
                && user.ProfileId.HasValue && user.ProfileId.Value != profileId)
            {
                var userCompany = await _context.Profiles.AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == user.ProfileId.Value);
                if (userCompany != null && userCompany.IsPartner)
                {
                    StripPartnerFields(result.Units);
                }
            }

            return result ?? new GetCompanyUnitsWithHierarchyDto();
        }

        // The company subscription, the latest license assignment per unit and,
        // for complimentary beneficiaries, the unit holding the purchased license
        // they ride on. Feeds BuildUnitLicense / BuildSubscriptionMessage.
        private async Task<(CompanySubscription? Subscription,
                            Dictionary<uint, AssignedCompanyLicense> LatestAssignmentByUnit,
                            Dictionary<uint, uint> ComplimentaryParentByUnit)> LoadLicenseLookupsAsync(int profileId)
        {
            var subscription = await _subscriptionRepo.GetActiveByProfileIdAsync((uint)profileId);

            // Latest assignment per unit regardless of IsActive / license.IsDel,
            // so expired licenses (cascade closes the assignment) are still seen.
            var latestAssignments = await _subscriptionRepo.GetLatestLicenseAssignmentsByCompanyAsync((uint)profileId);
            var latestAssignmentByUnit = latestAssignments
                .GroupBy(a => a.BusinessUnitId)
                .ToDictionary(g => g.Key, g => g.First());
            var complimentaryParentByUnit = new Dictionary<uint, uint>();

            // Complimentary beneficiaries ride the primary team's purchased
            // license. Project each active grant into a synthetic assignment on
            // the beneficiary unit so it shows the same license/expiry/banner.
            // Overwrites any closed assignment the beneficiary may have had.
            var grants = await _context.ComplimentaryLicenseGrants
                .Where(g => g.IsActive && !g.IsDel)
                .Join(_context.CompanyLicenses, g => g.CompanyLicenseId, l => l.Id,
                      (g, l) => new { Grant = g, l.ProfileId })
                .Where(x => x.ProfileId == (uint)profileId)
                .Select(x => x.Grant)
                .ToListAsync();

            foreach (var grant in grants)
            {
                var primary = latestAssignments.FirstOrDefault(a =>
                    a.CompanyLicenseId == grant.CompanyLicenseId && a.IsActive && a.CompanyLicense != null);
                if (primary?.CompanyLicense == null)
                    continue;

                latestAssignmentByUnit[grant.ComplimentaryBusinessUnitId] =
                    ComplimentaryLicenseProjection.SyntheticAssignment(grant, primary.CompanyLicense, primary.GracePeriod);

                // Record the LIVE purchased assignee as the parent, not
                // grant.PrimaryBusinessUnitId — the license can have been
                // reassigned to another team since the grant was created.
                complimentaryParentByUnit[grant.ComplimentaryBusinessUnitId] = primary.BusinessUnitId;
            }

            return (subscription, latestAssignmentByUnit, complimentaryParentByUnit);
        }

        // Licence details per team for the Team Structure page — the same
        // expiry / grace / trial / complimentary rules as get-teams-structure.
        public async Task<Dictionary<uint, TeamLicenceDto>> GetTeamLicencesAsync(int profileId, IEnumerable<uint> unitIds)
        {
            var (sub, latestAssignmentByUnit, complimentaryParentByUnit) = await LoadLicenseLookupsAsync(profileId);

            var result = new Dictionary<uint, TeamLicenceDto>();
            foreach (var unitId in unitIds.Distinct())
            {
                latestAssignmentByUnit.TryGetValue(unitId, out var assignment);
                var license = BuildUnitLicense(assignment, sub);
                var backingSub = assignment?.CompanyLicense?.CompanySubscription ?? sub;
                var isTrial = license.HasLicense && IsTrialLicense(backingSub, assignment?.CompanyLicense);

                // Special units are licensed without an end date (see PopulateUnitSubscriptionAndLicense).
                var isExempt = _licensedPeriod.IsExemptFromLicensing(unitId);
                if (isExempt)
                {
                    license.HasLicense = true;
                    license.Status = "Active";
                    license.IsExpired = false;
                    if (license.ExpiryDate.HasValue && license.ExpiryDate.Value <= DateTime.UtcNow)
                    {
                        license.ExpiryDate = null;
                        license.DaysRemaining = null;
                    }
                }

                var isComplimentary = complimentaryParentByUnit.TryGetValue(unitId, out var parentUnitId);
                var active = license.HasLicense && !license.IsExpired;

                result[unitId] = new TeamLicenceDto
                {
                    HasLicence = license.HasLicense,
                    Status = !license.HasLicense ? "None" : license.IsExpired ? "Expired" : "Active",
                    LicenceKey = assignment?.CompanyLicense?.LicenseKey,
                    LicenceType = !license.HasLicense ? null : isTrial ? "Trial" : "Paid",
                    StartDate = license.StartDate,
                    ExpiryDate = license.ExpiryDate,
                    DaysRemaining = license.DaysRemaining,
                    IsExpired = license.HasLicense && license.IsExpired,
                    // Trials never renew; a paid licence renews while its
                    // subscription is live and not set to cancel.
                    WillAutoRenew = active && !isExempt && !isTrial && backingSub != null
                                    && (backingSub.Status == SubscriptionStatus.Active || backingSub.Status == SubscriptionStatus.PastDue)
                                    && !backingSub.CancelAtPeriodEnd,
                    IsExpiringSoon = active && license.DaysRemaining.HasValue
                                     && license.DaysRemaining.Value <= TeamLicenceDto.ExpiringSoonDays,
                    IsComplimentary = isComplimentary,
                    ParentUnitId = isComplimentary ? parentUnitId : null,
                    SubscriptionMessage = license.HasLicense && !isExempt
                        ? BuildSubscriptionMessage(backingSub, assignment?.CompanyLicense, license)
                        : null
                };
            }
            return result;
        }

        // Walks the unit tree and stamps each unit with the shared subscription
        // banner and its own license details. Recurses into SubUnit so every
        // level is populated.
        private void PopulateUnitSubscriptionAndLicense(
            List<BusinessUnitTeamDto> units,
            Dictionary<uint, AssignedCompanyLicense> latestAssignmentByUnit,
            CompanySubscription? sub,
            Dictionary<uint, uint> complimentaryParentByUnit)
        {
            foreach (var unit in units)
            {
                latestAssignmentByUnit.TryGetValue(unit.BusinessUnitId, out var assignment);
                unit.License = BuildUnitLicense(assignment, sub);

                // Special units (FeedbackAutoImport:BusinessUnitIds) are exempt from
                // licensing. This method derives licence state from raw assignment
                // rows rather than from ILicensedPeriodService, so without this the
                // exemption applied to the metrics above would be undone here and the
                // unit would come back Unlicensed with its data blanked.
                //
                // Present them exactly as a normally licensed team: HasLicense true,
                // Status "Active", never expired, and no past expiry date left on the
                // DTO (same shape as a legacy licence that never auto-expires). No new
                // status value is introduced, so the frontend's existing licence checks
                // pass unchanged and cannot tell these units apart from real ones.
                var isExempt = _licensedPeriod.IsExemptFromLicensing(unit.BusinessUnitId);
                if (isExempt)
                {
                    unit.License.HasLicense = true;
                    unit.License.Status = "Active";
                    unit.License.IsExpired = false;
                    if (unit.License.ExpiryDate.HasValue && unit.License.ExpiryDate.Value <= DateTime.UtcNow)
                    {
                        unit.License.ExpiryDate = null;
                        unit.License.DaysRemaining = null;
                    }
                }

                // Tag units whose coverage comes from a complimentary grant so the
                // UI can badge them; the license/expiry/banner already mirror the
                // primary team via the synthetic assignment. ParentBusinessUnitId
                // names the paid team backing the grant so the UI can warn about
                // the right team on delete / unassign.
                if (complimentaryParentByUnit.TryGetValue(unit.BusinessUnitId, out var parentUnitId))
                {
                    unit.License.IsComplimentary = true;
                    unit.License.ParentBusinessUnitId = parentUnitId;
                }

                // Banner reflects THIS unit's assigned license — its cadence and
                // its expiry — using the subscription that actually backs the
                // license (falling back to the company sub), NOT one company-wide
                // value. So a team holding a yearly license reads "Yearly", even
                // when another team on the same company is monthly. Only shown on
                // units that actually hold a license.
                unit.SubscriptionMessage = unit.License.HasLicense
                    ? BuildSubscriptionMessage(
                        assignment?.CompanyLicense?.CompanySubscription ?? sub,
                        assignment?.CompanyLicense,
                        unit.License)
                    : null;

                // Special units must not advertise a renewal date. A single
                // space, NOT "" or null: the frontend falls back to a date built
                // from /subscription/detail whenever this field is falsy
                // (`team.subscriptionMessage || ...`), and these units do hold
                // live licenses there — so an empty string would bring the banner
                // straight back. A space is truthy, short-circuits that fallback,
                // and collapses to nothing when rendered. Nothing else changes.
                if (isExempt)
                    unit.SubscriptionMessage = " ";

                // Teams without an ACTIVE license (none assigned, or expired) must
                // not surface license-dependent metrics. Suppress them and flag the
                // team so the UI shows an "Unlicensed" indicator instead. An active
                // purchased OR complimentary license (License.Status == "Active")
                // keeps the metrics visible.
                var hasActiveLicense = unit.License.HasLicense
                    && string.Equals(unit.License.Status, "Active", StringComparison.OrdinalIgnoreCase);
                if (!hasActiveLicense && !isExempt)
                {
                    unit.IsUnlicensed = true;
                    unit.TrendChart = null;
                    unit.LastActionHubUpdatedAt = null;
                    unit.LastResponseAt = null;
                    unit.LastFeedbackAt = null;
                    unit.TotalResponses = 0;

                    // A team without a manager keeps "NoManager": coverage comes from
                    // the manager's licence, so the missing manager is the real issue.
                    if (unit.HasManager)
                        unit.TeamStatus = "Unlicensed";

                    // "Expired" only when this team actually held a licence that ended.
                    // A team that never had one (new, or no manager yet) is not expired,
                    // and the company's own licence may be perfectly live.
                    if (unit.License.HasLicense)
                    {
                        unit.IsLicenseExpired = true;
                        unit.LicenseMessage = "The license for this team has expired.";
                    }
                    else
                    {
                        unit.IsLicenseExpired = false;
                        unit.LicenseMessage = "No license assigned to this team.";
                    }
                }
                else
                {
                    unit.IsLicenseExpired = false;
                    unit.LicenseMessage = "License is active.";
                }

                if (unit.SubUnit?.Any() == true)
                    PopulateUnitSubscriptionAndLicense(unit.SubUnit, latestAssignmentByUnit, sub, complimentaryParentByUnit);
            }
        }

        // Builds the per-unit license object. Expiry is the license's OWN
        // ExpiryDate (independent of the subscription period) plus the resolved
        // grace (sub-level wins, else the assignment's). HasLicense is false when
        // the unit holds no license, or when its latest assignment was closed
        // because the license moved to another unit (still live, not expired).
        private static UnitLicenseDto BuildUnitLicense(AssignedCompanyLicense? assignment, CompanySubscription? sub)
        {
            var dto = new UnitLicenseDto { HasLicense = false, Status = "Inactive" };

            var license = assignment?.CompanyLicense;
            if (assignment == null || license == null)
                return dto;

            var now = DateTime.UtcNow;

            // Grace precedence mirrors SubscriptionService.ResolveGrace.
            var grace = sub?.GracePeriod ?? assignment.GracePeriod ?? 0;

            var rawExpiry = license.ExpiryDate;
            var effectiveExpiry = rawExpiry.HasValue && grace > 0
                ? rawExpiry.Value.AddDays(grace)
                : rawExpiry;

            // Expired when the license lifecycle deleted it (expiry cascade) or
            // its effective expiry has passed. Licenses with no ExpiryDate never
            // auto-expire (legacy / imported rows).
            bool expired = license.IsDel
                           || (effectiveExpiry.HasValue && now >= effectiveExpiry.Value);

            // Assignment closed but the license is still live and in-date → it
            // was reassigned to another unit, so THIS unit no longer holds it.
            if (!assignment.IsActive && !expired)
                return dto;

            dto.HasLicense = true;
            dto.StartDate = assignment.AssignedAt;
            dto.ExpiryDate = effectiveExpiry;
            dto.IsExpired = expired;
            dto.Status = expired ? "Inactive" : "Active";
            dto.DaysRemaining = expired
                ? 0
                : (effectiveExpiry.HasValue
                    ? Math.Max(0, (int)Math.Ceiling((effectiveExpiry.Value - now).TotalDays))
                    : (int?)null);

            return dto;
        }

        // Builds the per-team subscription banner shown on the team-structure
        // screen, e.g. "Yearly - Renews July 6, 2027" / "Monthly - Renews
        // August 2, 2026" / "Trial - Expires <date>". It reflects the TEAM's OWN
        // assigned license, not one company-wide value:
        //   * the DATE is the license's own expiry (grace already honoured by
        //     BuildUnitLicense), i.e. when THIS team's license actually ends;
        //   * a TRIAL license always reads "Trial - Expires <date>", never a
        //     renewal cadence — see IsTrialLicense for how a trial is detected;
        //   * otherwise the CADENCE is "Yearly" when the backing subscription is
        //     Annual OR the license's own validity window spans clearly more than
        //     a month (see LicenseSpansYear) — the latter covers a yearly license
        //     issued on a monthly base subscription (a yearly seat added to a
        //     monthly plan), which the sub's BillingCycle alone would mislabel
        //     "Monthly".
        // Returns null when there's no expiry to show, when there's no backing
        // subscription for a paid license, or when the subscription won't
        // auto-renew (canceled / expired / paused).
        private static string? BuildSubscriptionMessage(
            CompanySubscription? sub,
            CompanyLicense? licenseRow,
            UnitLicenseDto license)
        {
            // The team's own license expiry is the source of truth; fall back to
            // the subscription period end only for a legacy license with no date.
            var expiry = license.ExpiryDate
                ?? (sub != null && sub.CurrentPeriodEnd != default ? sub.CurrentPeriodEnd : (DateTime?)null);
            if (!expiry.HasValue)
                return null;

            // Trial wins over every cadence word: a trial never "renews".
            // Checked BEFORE the null-sub guard so a trial license still gets its
            // banner when the subscription row can't be resolved.
            if (IsTrialLicense(sub, licenseRow))
                return $"Trial - Expires {expiry.Value:MMMM d, yyyy}";

            if (sub == null)
                return null;

            // Paid and active (Active / PastDue still run until period end and
            // auto-renew). Canceled / Expired / Paused subscriptions won't
            // renew, so they get no banner.
            if (sub.Status == SubscriptionStatus.Active || sub.Status == SubscriptionStatus.PastDue)
            {
                var isAnnual = sub.BillingCycle == BillingCycle.Annual || LicenseSpansYear(license);
                var cycleWord = isAnnual ? "Yearly" : "Monthly";
                return $"{cycleWord} - Renews {expiry.Value:MMMM d, yyyy}";
            }

            return null;
        }

        // True when the license backing this team is a TRIAL license.
        //
        // The license row's own LicenseType is the primary signal — it is minted
        // "Trial" at trial signup and flipped to "Paid" the moment the trial
        // converts (see SubscriptionService's promote-to-paid paths), so it stays
        // correct even when the subscription row's Status drifts (e.g. an expired
        // trial auto-healed to Active, or a trial issued against a sub row left
        // on a paid status). This mirrors how the subscription/licenses screen
        // labels a license's billing cycle.
        //
        // The subscription-level check is the fallback for licenses with no type
        // (legacy / imported rows) and matches SubscriptionService.IsTrialSub:
        // a live Trial, or an Expired row that never had a Stripe subscription
        // (an expired trial, not a lapsed paid plan).
        private static bool IsTrialLicense(CompanySubscription? sub, CompanyLicense? licenseRow)
        {
            if (licenseRow != null && !string.IsNullOrWhiteSpace(licenseRow.LicenseType))
                return string.Equals(licenseRow.LicenseType, "Trial", StringComparison.OrdinalIgnoreCase);

            if (sub == null)
                return false;

            return sub.Status == SubscriptionStatus.Trial
                   || (sub.Status == SubscriptionStatus.Expired && string.IsNullOrEmpty(sub.StripeSubscriptionId));
        }

        // True when a license's own validity window is clearly longer than a
        // month — i.e. a yearly license. Uses the license's assigned-start →
        // expiry span with a 180-day cut (monthly ≈ 30 days, yearly ≈ 365), so
        // the banner stays correct even when the backing subscription's
        // BillingCycle is stale/monthly (e.g. a yearly seat added onto a monthly
        // base plan). Returns false when either date is missing.
        private static bool LicenseSpansYear(UnitLicenseDto license)
        {
            if (!license.StartDate.HasValue || !license.ExpiryDate.HasValue)
                return false;
            return (license.ExpiryDate.Value - license.StartDate.Value).TotalDays > 180;
        }

        private GetCompanyUnitsWithHierarchyDto CloveraAdmin(long profileId)
        {
            try
            {
                var company = _context.Profiles
                      .Where(c => c.Id == profileId)
                      .Select(c => new { c.Id, Name = c.ProfileName })
                      .FirstOrDefault();


                if (company == null) return null;
                var allUnits = _context.BusinessUnits
                    .Where(bu => bu.Profile.Id == profileId && bu.IsDel != true)
                    .OrderBy(bu => bu.Name)
                    .ToList();

                if (!allUnits.Any())
                    return new GetCompanyUnitsWithHierarchyDto();

                // Create a lookup for fast parent-child access
                var unitLookup = allUnits.ToLookup(bu => bu.ParentId);

                // Unit ids on the current root -> node path. A ParentId loop in
                // the data would otherwise recurse forever and take the process
                // down with a StackOverflowException, which cannot be caught. In
                // valid data no unit is ever its own ancestor, so nothing is ever
                // skipped and the tree built is identical.
                var ancestorPath = new HashSet<uint>();

                // Recursive method to build the hierarchy
                List<BusinessUnitTeamDto> BuildHierarchy(int? parentId)
                {
                    var children = unitLookup[parentId].ToList();
                    var built = new List<BusinessUnitTeamDto>(children.Count);

                    foreach (var unit in children)
                    {
                        if (!ancestorPath.Add(unit.Id))
                            continue; // cycle: unit already sits on its own path

                        var managerInfo = loadManagerNameWithIsReadOnly(profileId, (int)unit.Id);
                        built.Add(new BusinessUnitTeamDto
                        {
                            BusinessUnitId = unit.Id,
                            BusinessUnitName = unit.Name,
                            BusinessUnitShortCode = unit.BusinessUnitShortCode,
                            TeamSize = unit.TeamSize,
                            SubUnit = BuildHierarchy((int)unit.Id),
                            ManagerId = managerInfo.id,
                            ManagerName = managerInfo.name,
                            IsReadOnly = managerInfo.isReadOnly
                        });

                        ancestorPath.Remove(unit.Id);
                    }

                    return built;
                }

                // Start building from root units (ParentId == null)
                var result = BuildHierarchy(null);
                return new GetCompanyUnitsWithHierarchyDto
                {
                    ProfileId = (int)company.Id,
                    CompanyName = company.Name,
                    Units = result
                };

            }
            catch (Exception ex)
            {
                return new GetCompanyUnitsWithHierarchyDto();
            }
        }
        (string name, int id) loadManagerName(long profileId, int unitId)
        {
            var user = _context.UserTeams
                .Where(ut => ut.UnitId == (uint)unitId && !ut.IsUnAssigned)
                .OrderBy(ut => ut.Id) // Order by UserTeam Id
                .Join(_context.Users.Where(u => !u.IsDel), ut => ut.UserId, user => user.UserId, (ut, user) => user)
                .LastOrDefault();

            return (user?.Name ?? "", user?.UserId ?? 0);
        }

        // A unit's manager is whoever holds its most recent active UserTeams row.
        // Deleted users are excluded: deleting a user now unassigns their teams,
        // but rows left behind by deletes that predate that cascade would still
        // name a removed manager here. Joining through the non-deleted users makes
        // the unit fall back to its next active assignment, or report no manager
        // (id 0 -> HasManager false -> the UI's unassigned state).
        (string name, int id, bool? isReadOnly) loadManagerNameWithIsReadOnly(long profileId, int unitId)
        {
            var assignment = _context.UserTeams
                .Where(ut => ut.UnitId == (uint)unitId && !ut.IsUnAssigned)
                .Join(_context.Users.Where(u => !u.IsDel),
                      ut => ut.UserId,
                      user => user.UserId,
                      (ut, user) => new { ut.Id, ut.CreatedDate, ut.IsReadOnly, user.Name, user.UserId })
                .OrderByDescending(x => x.CreatedDate)
                .ThenByDescending(x => x.Id)
                .FirstOrDefault();

            if (assignment == null)
                return ("", 0, null);

            return (assignment.Name ?? "", assignment.UserId, assignment.IsReadOnly);
        }
        private GetCompanyUnitsWithHierarchyDto TeamManagerAdmin(long profileId, int userId)
    {
     try
     {
         List<long> rootUnitIds;


         rootUnitIds = _context.UserTeams
             .Where(ut => ut.UserId == userId && ut.IsUnAssigned == false)
             .Select(ut => (long)ut.UnitId)
             .Distinct()
             .ToList();

         if (!rootUnitIds.Any())
             return new GetCompanyUnitsWithHierarchyDto();

         // Get ALL units in the company to include all children and sub-children
         var allUnits = _context.BusinessUnits
             .Where(bu => bu.Profile.Id == profileId && bu.IsDel != true)
             .OrderBy(bu => bu.Name)
             .ToList();

         if (!allUnits.Any())
             return new GetCompanyUnitsWithHierarchyDto();

         try
         {
             var company = _context.Profiles
                   .Where(c => c.Id == profileId)
                   .Select(c => new { c.Id, Name = c.ProfileName })
                   .FirstOrDefault();


             if (company == null) return null;

             // Create a lookup for fast parent-child access
             var unitLookup = allUnits.ToLookup(bu => bu.ParentId);
             var unitDict = allUnits.ToDictionary(bu => (long)bu.Id);

             // Helper to check if a unit has an ancestor in rootUnitIds
             bool HasAncestorInRootUnits(long unitId)
             {
                 // Walks the parent chain iteratively, remembering every id it has
                 // stepped through. A ParentId loop would make the previous
                 // recursive form recurse forever and kill the process with an
                 // (uncatchable) StackOverflowException. For a well-formed chain
                 // the answer is unchanged: true as soon as an ancestor is a root
                 // unit, false once the chain ends.
                 var seen = new HashSet<long> { unitId };
                 var currentId = unitId;

                 while (true)
                 {
                     if (!unitDict.TryGetValue(currentId, out var unit) || unit.ParentId == null)
                         return false;

                     var parentId = (long)unit.ParentId.Value;
                     if (rootUnitIds.Contains(parentId))
                         return true;

                     if (!seen.Add(parentId))
                         return false; // looped without meeting a root unit

                     currentId = parentId;
                 }
             }

             // Unit ids on the current root -> node path; see the same guard in
             // CloveraAdmin. Never trims anything for valid hierarchies.
             var ancestorPath = new HashSet<uint>();

             // Recursive method to build the hierarchy
             List<BusinessUnitTeamDto> BuildHierarchy(int? parentId)
             {
                 var children = unitLookup[parentId].ToList();
                 var built = new List<BusinessUnitTeamDto>(children.Count);

                 foreach (var unit in children)
                 {
                     if (!ancestorPath.Add(unit.Id))
                         continue; // cycle: unit already sits on its own path

                     var managerInfo = loadManagerNameWithIsReadOnly(profileId, (int)unit.Id);
                     built.Add(new BusinessUnitTeamDto
                     {
                         BusinessUnitId = unit.Id,
                         BusinessUnitName = unit.Name,
                         BusinessUnitShortCode = unit.BusinessUnitShortCode,
                         TeamSize = unit.TeamSize,
                         SubUnit = BuildHierarchy((int)unit.Id),
                         ManagerId = managerInfo.id,
                         ManagerName = managerInfo.name,
                         IsReadOnly = managerInfo.isReadOnly
                     });

                     ancestorPath.Remove(unit.Id);
                 }

                 return built;
             }

             // Filter to only root units where user is assigned AND not a descendant of another assigned unit
             var rootUnits = allUnits.Where(bu => rootUnitIds.Contains((long)bu.Id) && !HasAncestorInRootUnits((long)bu.Id)).ToList();
             var result = new List<BusinessUnitTeamDto>(rootUnits.Count);

             foreach (var unit in rootUnits)
             {
                 // Seed the path with the root itself. These roots are the user's
                 // assigned units, NOT necessarily ParentId-null ones, so unlike
                 // the other two builders a root here can itself sit in a cycle.
                 ancestorPath.Add(unit.Id);

                 var managerInfo = loadManagerNameWithIsReadOnly(profileId, (int)unit.Id);
                 result.Add(new BusinessUnitTeamDto
                 {
                     BusinessUnitId = unit.Id,
                     BusinessUnitName = unit.Name,
                     BusinessUnitShortCode = unit.BusinessUnitShortCode,
                     TeamSize = unit.TeamSize,
                     SubUnit = BuildHierarchy((int)unit.Id), // Build all children recursively
                     ManagerId = managerInfo.id,
                     ManagerName = managerInfo.name,
                     IsReadOnly = managerInfo.isReadOnly
                 });

                 ancestorPath.Remove(unit.Id);
             }

             return new GetCompanyUnitsWithHierarchyDto
             {
                 ProfileId = (int)company.Id,
                 CompanyName = company.Name,
                 Units = result
             };

         }
         catch (Exception ex)
         {
             return new GetCompanyUnitsWithHierarchyDto();
         }
     }
     catch (Exception ex)
     {
         return new GetCompanyUnitsWithHierarchyDto();
     }
    }

        private GetCompanyUnitsWithHierarchyDto TeamUnit(long profileId, int userId)
        {

            try
            {

                //int unit = 0;
                //unit = _context.CompanyLinks
                //    .Where(ut => ut.UserId == userId && ut.ProfileId == profileId)
                //    .Select(ut => (ut.UnitId).Value).FirstOrDefault();

                int unit = _context.UserTeams
                    .Where(ut => ut.UserId == userId && ut.IsUnAssigned == false)
                    .OrderByDescending(ut => ut.CreatedDate)
                    .Select(ut => (int)ut.UnitId)
                    .FirstOrDefault();


                var allUnits = _context.BusinessUnits
                    .Where(bu => bu.Profile.Id == profileId && (bu.Id == unit || bu.ParentId == unit) && bu.IsDel != true)
                    .OrderBy(bu => bu.Name)
                    .ToList();

                if (!allUnits.Any())
                    return new GetCompanyUnitsWithHierarchyDto();
                var company = _context.Profiles
                          .Where(c => c.Id == profileId)
                          .Select(c => new { c.Id, Name = c.ProfileName })
                          .FirstOrDefault();


                if (company == null) return null;


                if (!allUnits.Any())
                    return new GetCompanyUnitsWithHierarchyDto();

                // Create a lookup for fast parent-child access
                var unitLookup = allUnits.ToLookup(bu => bu.ParentId);

                // Unit ids on the current root -> node path; see the same guard in
                // CloveraAdmin. Never trims anything for valid hierarchies.
                var ancestorPath = new HashSet<uint>();

                // Recursive method to build the hierarchy
                List<BusinessUnitTeamDto> BuildHierarchy(int? parentId)
                {
                    var children = unitLookup[parentId].ToList();
                    var built = new List<BusinessUnitTeamDto>(children.Count);

                    foreach (var unit in children)
                    {
                        if (!ancestorPath.Add(unit.Id))
                            continue; // cycle: unit already sits on its own path

                        var managerInfo = loadManagerNameWithIsReadOnly(profileId, (int)unit.Id);
                        built.Add(new BusinessUnitTeamDto
                        {
                            BusinessUnitId = unit.Id,
                            BusinessUnitName = unit.Name,
                            BusinessUnitShortCode = unit.BusinessUnitShortCode,
                            TeamSize = unit.TeamSize,
                            SubUnit = BuildHierarchy((int)unit.Id),
                            ManagerId = managerInfo.id,
                            ManagerName = managerInfo.name,
                            IsReadOnly = managerInfo.isReadOnly
                        });

                        ancestorPath.Remove(unit.Id);
                    }

                    return built;
                }

                // Start building from root units (ParentId == null)
                var result = BuildHierarchy(null);
                return new GetCompanyUnitsWithHierarchyDto
                {
                    ProfileId = (int)company.Id,
                    CompanyName = company.Name,
                    Units = result
                };
            }
            catch (Exception ex)
            {
                return new GetCompanyUnitsWithHierarchyDto();
            }

        }

        private async Task PopulateTrendChartsAsync(
            List<BusinessUnitTeamDto> units,
            Dictionary<uint, List<LicensedInterval>> licensedIntervals)
        {
            var allUnitIds = new List<long>();
            CollectUnitIds(units, allUnitIds);

            if (!allUnitIds.Any()) return;

            var toDate = DateTime.UtcNow;
            var fromDate = toDate.AddDays(-30);
            var extendedFromDate = fromDate.AddDays(-30);

            var feedbackRows = await _aggregateRepo.GetDailyFeedbackReportAsync(
                allUnitIds,
                new DateTimeOffset(extendedFromDate, TimeSpan.Zero),
                new DateTimeOffset(toDate, TimeSpan.Zero));

            // Drop feedback generated while a team was unlicensed so the trend
            // reflects only licensed-period activity.
            feedbackRows = feedbackRows
                .Where(r => r.BusinessUnitId.HasValue
                            && _licensedPeriod.IsLicensed(licensedIntervals, r.BusinessUnitId.Value, r.SubmissionDateTime.UtcDateTime))
                .ToList();

            PopulateTrendChartsRecursive(units, feedbackRows, fromDate, toDate);
        }

        private void CollectUnitIds(List<BusinessUnitTeamDto> units, List<long> ids)
        {
            foreach (var unit in units)
            {
                ids.Add(unit.BusinessUnitId);
                if (unit.SubUnit?.Any() == true)
                    CollectUnitIds(unit.SubUnit, ids);
            }
        }

        private List<long> GetDescendantIds(BusinessUnitTeamDto unit)
        {
            var ids = new List<long> { unit.BusinessUnitId };
            if (unit.SubUnit != null)
            {
                foreach (var child in unit.SubUnit)
                    ids.AddRange(GetDescendantIds(child));
            }
            return ids;
        }

        private void PopulateTrendChartsRecursive(List<BusinessUnitTeamDto> units, List<DailyFeedbackReportRowDto> allFeedback, DateTime fromDate, DateTime toDate)
        {
            foreach (var unit in units)
            {
                var unitIds = GetDescendantIds(unit);
                unit.TrendChart = _aggregateRepo.CalculateTrendChartFromFeedback(allFeedback, unitIds, fromDate, toDate);

                if (unit.SubUnit?.Any() == true)
                    PopulateTrendChartsRecursive(unit.SubUnit, allFeedback, fromDate, toDate);
            }
        }

        private async Task PopulateLastActionHubUpdatedAtAsync(
            List<BusinessUnitTeamDto> units,
            Dictionary<uint, List<LicensedInterval>> licensedIntervals)
        {
            var allUnitIds = new List<long>();
            CollectUnitIds(units, allUnitIds);

            if (!allUnitIds.Any()) return;

            var castUnitIds = allUnitIds.Select(id => (uint)id).ToList();

            var minStart = _licensedPeriod.EarliestIntervalStart(licensedIntervals);

            var hubRows = await _context.ActionHubs
                .Where(ah => !ah.IsDel && castUnitIds.Contains(ah.UnitId) && ah.UpdatedAt != null && ah.UpdatedAt >= minStart)
                .Select(ah => new { ah.UnitId, ah.UpdatedAt })
                .ToListAsync();

            // Only action-hub updates made during a licensed period count.
            var latestUpdatedAtByUnit = hubRows
                .Where(ah => ah.UpdatedAt.HasValue && _licensedPeriod.IsLicensed(licensedIntervals, ah.UnitId, ah.UpdatedAt.Value))
                .GroupBy(ah => ah.UnitId)
                .ToDictionary(g => g.Key, g => (DateTime?)g.Max(x => x.UpdatedAt!.Value));

            SetLastActionHubUpdatedAtRecursive(units, latestUpdatedAtByUnit);
        }

        private void SetLastActionHubUpdatedAtRecursive(List<BusinessUnitTeamDto> units, Dictionary<uint, DateTime?> latestUpdatedAtByUnit)
        {
            foreach (var unit in units)
            {
                if (latestUpdatedAtByUnit.TryGetValue((uint)unit.BusinessUnitId, out var updatedAt))
                {
                    unit.LastActionHubUpdatedAt = updatedAt;
                }

                if (unit.SubUnit?.Any() == true)
                    SetLastActionHubUpdatedAtRecursive(unit.SubUnit, latestUpdatedAtByUnit);
            }
        }

        private async Task PopulateTeamStatusAsync(
            List<BusinessUnitTeamDto> units,
            Dictionary<uint, List<LicensedInterval>> licensedIntervals)
        {
            var allUnitIds = new List<long>();
            CollectUnitIds(units, allUnitIds);

            if (!allUnitIds.Any()) return;

            var castUnitIds = allUnitIds.Select(id => (uint)id).ToList();

            var cutoff48h = DateTime.UtcNow.AddHours(-48);

            // Nothing before the earliest licensed interval can ever be licensed —
            // bound the fetch so we don't pull pre-license history into memory.
            var minStart = _licensedPeriod.EarliestIntervalStart(licensedIntervals);

            var rawFeedback = await _context.DailyFeedbacks
                .Where(df => castUnitIds.Contains(df.BusinessUnitId) && df.SubmissionDateTime >= minStart)
                .Select(df => new { df.BusinessUnitId, df.SubmissionDateTime })
                .ToListAsync();

            // Only feedback submitted during a licensed period counts toward the
            // team's last-response time and totals.
            var feedbackStats = rawFeedback
                .Where(df => _licensedPeriod.IsLicensed(licensedIntervals, df.BusinessUnitId, df.SubmissionDateTime))
                .GroupBy(df => df.BusinessUnitId)
                .ToDictionary(g => g.Key, g => new UnitFeedbackStats
                {
                    UnitId = g.Key,
                    LastFeedbackAt = g.Max(x => x.SubmissionDateTime),
                    TotalResponses = g.Count()
                });

            SetTeamStatusRecursive(units, feedbackStats, cutoff48h);
        }

        private void SetTeamStatusRecursive(
            List<BusinessUnitTeamDto> units,
            Dictionary<uint, UnitFeedbackStats> feedbackStats,
            DateTime cutoff48h)
        {
            foreach (var unit in units)
            {
                unit.HasManager = unit.ManagerId.HasValue && unit.ManagerId.Value > 0;

                if (feedbackStats.TryGetValue((uint)unit.BusinessUnitId, out var stats))
                {
                    unit.LastFeedbackAt = stats.LastFeedbackAt;
                    unit.LastResponseAt = stats.LastFeedbackAt;
                    unit.TotalResponses = stats.TotalResponses;
                }

                if (!unit.HasManager)
                    unit.TeamStatus = "NoManager";
                else if (unit.LastFeedbackAt == null || unit.LastFeedbackAt < cutoff48h)
                    unit.TeamStatus = "NeedsAttention";
                else
                    unit.TeamStatus = "Responding";

                if (unit.SubUnit?.Any() == true)
                    SetTeamStatusRecursive(unit.SubUnit, feedbackStats, cutoff48h);
            }
        }

        private TeamStructureSummaryDto BuildTeamStructureSummary(List<BusinessUnitTeamDto> units)
        {
            var allUnits = new List<BusinessUnitTeamDto>();
            FlattenUnits(units, allUnits);

            var cutoff48h = DateTime.UtcNow.AddHours(-48);
            var cutoff30d = DateTime.UtcNow.AddDays(-30);

            bool NoResponse(BusinessUnitTeamDto u) => u.HasManager && (u.LastFeedbackAt == null || u.LastFeedbackAt < cutoff48h);
            bool NoActionHub(BusinessUnitTeamDto u) => u.LastActionHubUpdatedAt == null || u.LastActionHubUpdatedAt < cutoff30d;

            var noManagerCount = allUnits.Count(u => !u.HasManager);
            var noActionHubCount = allUnits.Count(NoActionHub);

            return new TeamStructureSummaryDto
            {
                TotalTeams = allUnits.Count,
                NoManagerCount = noManagerCount,
                // Teams, not issues: a team failing several checks is counted once,
                // so this can never exceed TotalTeams.
                NeedsAttentionCount = allUnits.Count(u => !u.HasManager || NoResponse(u) || NoActionHub(u)),
                RespondingCount = allUnits.Count(u => u.TeamStatus == "Responding"),
                NoActionHubUpdateCount = noActionHubCount
            };
        }

        private void FlattenUnits(List<BusinessUnitTeamDto> units, List<BusinessUnitTeamDto> result)
        {
            foreach (var unit in units)
            {
                result.Add(unit);
                if (unit.SubUnit?.Any() == true)
                    FlattenUnits(unit.SubUnit, result);
            }
        }

        private void StripPartnerFields(List<BusinessUnitTeamDto> units)
        {
            foreach (var unit in units)
            {
                unit.BusinessUnitShortCode = null;
                unit.ManagerName = null;
                unit.ManagerId = null;
                unit.LastActionHubUpdatedAt = null;
                unit.LastFeedbackAt = null;
                unit.LastResponseAt = null;
                unit.SubscriptionMessage = null;
                unit.License = null;

                if (unit.SubUnit?.Any() == true)
                    StripPartnerFields(unit.SubUnit);
            }
        }

        private static string GetRelativeTime(DateTime dateTime)
        {
            var now = DateTime.UtcNow;
            var diff = now - dateTime;

            if (diff.TotalMinutes < 1)
                return "Just now";
            if (diff.TotalMinutes < 60)
                return $"{(int)diff.TotalMinutes} min ago";
            if (diff.TotalHours < 24)
                return diff.TotalHours < 2 ? "1 hour ago" : $"{(int)diff.TotalHours} hours ago";
            if (diff.TotalDays < 7)
                return diff.TotalDays < 2 ? "1 day ago" : $"{(int)diff.TotalDays} days ago";
            if (diff.TotalDays < 30)
            {
                var weeks = (int)(diff.TotalDays / 7);
                return weeks == 1 ? "1 week ago" : $"{weeks} weeks ago";
            }
            if (diff.TotalDays < 365)
            {
                var months = (int)(diff.TotalDays / 30);
                return months == 1 ? "1 month ago" : $"{months} months ago";
            }

            var years = (int)(diff.TotalDays / 365);
            return years == 1 ? "1 year ago" : $"{years} years ago";
        }

        private class UnitFeedbackStats
        {
            public uint UnitId { get; set; }
            public DateTime LastFeedbackAt { get; set; }
            public int TotalResponses { get; set; }
        }

    }
}
