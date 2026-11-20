using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Repository.Implementation
{
    public class CompanyLicenseRepository : ICompanyLicenseRepository
    {
        private readonly ApplicationDbContext _context;

        // First key of pg_advisory_xact_lock(int, int); the second is the license id.
        // 16016 is taken by the per-profile Add User licence lock.
        private const int ComplimentaryGrantLockKey = 16017;

        // The per-profile licence lock (second key = profile id), shared with Add
        // User, create-manager and every team-creating path.
        private const int ProfileLicenseLockKey = 16016;

        public CompanyLicenseRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public Task<CompanyLicense?> GetByIdAsync(int id)
            => _context.CompanyLicenses
                .Include(l => l.Assignments)
                .FirstOrDefaultAsync(l => l.Id == id && !l.IsDel);

        public Task<List<CompanyLicense>> GetByCompanyAsync(uint profileId)
            => _context.CompanyLicenses
                .AsNoTracking()
                .Where(l => l.ProfileId == profileId && !l.IsDel)
                .OrderBy(l => l.Id)
                .ToListAsync();

        public Task<AssignedCompanyLicense?> GetActiveAssignmentAsync(int companyLicenseId)
            => _context.AssignedCompanyLicenses
                .FirstOrDefaultAsync(a => a.CompanyLicenseId == companyLicenseId && a.IsActive && !a.IsDel);

        public Task<List<AssignedCompanyLicense>> GetAssignmentHistoryAsync(int companyLicenseId)
            => _context.AssignedCompanyLicenses
                .AsNoTracking()
                .Where(a => a.CompanyLicenseId == companyLicenseId && !a.IsDel)
                .OrderBy(a => a.AssignedAt)
                .ToListAsync();

        public Task<bool> BusinessUnitExistsForCompanyAsync(uint profileId, uint businessUnitId)
            => _context.BusinessUnits
                .AnyAsync(b => b.Id == businessUnitId
                            && b.ProfileId == profileId
                            && (b.IsDel == false || b.IsDel == null));

        // Closes the currently active assignment (if any) and inserts a new one.
        // Wrapped in a transaction so a license can never be double-assigned in
        // the active window. Both the closed row (UPDATE) and the new row (INSERT)
        // produce audit entries.
        //
        // Joins the caller's transaction when one is already open (Add User creates
        // the team and assigns its license as one unit of work); Npgsql does not
        // allow a nested BeginTransaction on the same connection.
        public async Task<AssignedCompanyLicense> AssignAsync(int companyLicenseId, uint businessUnitId, int? actorUserId)
        {
            await using var tx = _context.Database.CurrentTransaction == null
                ? await _context.Database.BeginTransactionAsync()
                : null;
            try
            {
                var active = await GetActiveAssignmentAsync(companyLicenseId);
                var now = DateTime.UtcNow;

                if (active != null)
                {
                    // No-op if the target unit is already the active assignee.
                    if (active.BusinessUnitId == businessUnitId)
                    {
                        if (tx != null) await tx.CommitAsync();
                        return active;
                    }

                    active.IsActive = false;
                    active.UnassignedAt = now;
                    active.UpdatedAt = now;
                    active.UpdatedBy = actorUserId;
                    await _context.SaveChangesAsync();
                    await WriteAssignmentAuditAsync(active, "UPDATE");
                }

                var assignment = new AssignedCompanyLicense
                {
                    CompanyLicenseId = companyLicenseId,
                    BusinessUnitId = businessUnitId,
                    AssignedAt = now,
                    IsActive = true,
                    CreatedBy = actorUserId,
                    UpdatedBy = actorUserId,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                _context.AssignedCompanyLicenses.Add(assignment);
                await _context.SaveChangesAsync();
                await WriteAssignmentAuditAsync(assignment, "INSERT");

                if (tx != null) await tx.CommitAsync();
                return assignment;
            }
            catch
            {
                // A joined transaction is the caller's to roll back.
                if (tx != null) await tx.RollbackAsync();
                throw;
            }
        }

        public async Task<bool> UnassignAsync(int companyLicenseId, int? actorUserId)
        {
            var active = await GetActiveAssignmentAsync(companyLicenseId);
            if (active == null) return false;

            var now = DateTime.UtcNow;
            active.IsActive = false;
            active.UnassignedAt = now;
            active.UpdatedAt = now;
            active.UpdatedBy = actorUserId;
            await _context.SaveChangesAsync();
            await WriteAssignmentAuditAsync(active, "UPDATE");
            return true;
        }

        public async Task<AssignedCompanyLicense> ReassignAsync(
            int companyLicenseId, uint fromBusinessUnitId, uint toBusinessUnitId, int? actorUserId)
        {
            if (fromBusinessUnitId == toBusinessUnitId)
                throw new InvalidOperationException("FromBusinessUnitId and ToBusinessUnitId must differ.");

            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                var active = await GetActiveAssignmentAsync(companyLicenseId)
                    ?? throw new InvalidOperationException(
                        "License has no active assignment to reassign. Use /apply instead.");

                if (active.BusinessUnitId != fromBusinessUnitId)
                    throw new InvalidOperationException(
                        $"License is currently assigned to BusinessUnit {active.BusinessUnitId}, " +
                        $"not {fromBusinessUnitId}. Refresh and retry.");

                var now = DateTime.UtcNow;
                active.IsActive = false;
                active.UnassignedAt = now;
                active.UpdatedAt = now;
                active.UpdatedBy = actorUserId;
                await _context.SaveChangesAsync();
                await WriteAssignmentAuditAsync(active, "UPDATE");

                var assignment = new AssignedCompanyLicense
                {
                    CompanyLicenseId = companyLicenseId,
                    BusinessUnitId = toBusinessUnitId,
                    AssignedAt = now,
                    IsActive = true,
                    CreatedBy = actorUserId,
                    UpdatedBy = actorUserId,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                _context.AssignedCompanyLicenses.Add(assignment);
                await _context.SaveChangesAsync();
                await WriteAssignmentAuditAsync(assignment, "INSERT");

                await tx.CommitAsync();
                return assignment;
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        public Task<AssignedCompanyLicense?> GetActiveAssignmentForUnitAsync(uint businessUnitId)
            => _context.AssignedCompanyLicenses
                .FirstOrDefaultAsync(a => a.BusinessUnitId == businessUnitId && a.IsActive && !a.IsDel);

        // Free := live (not deleted, not expired), not held by a live Team Manager
        // and not actively on a live team. An active row pointing at a deleted team
        // is stale — the same rule the subscription license pool uses — so that
        // license counts as free, and AssignAsync closes the stale row when the
        // license is used. A holder who was deleted or lost the Team Manager role
        // frees the licence the same way.
        public Task<int?> FindAvailableLicenseIdAsync(uint profileId)
        {
            var now = DateTime.UtcNow;
            var teamManagers = _context.TeamManagersOf((int)profileId);
            return _context.CompanyLicenses
                .AsNoTracking()
                .Where(l => l.ProfileId == profileId
                            && !l.IsDel
                            && (l.ExpiryDate == null || l.ExpiryDate > now)
                            && !(l.AssignedUserId != null && teamManagers.Any(u => u.UserId == l.AssignedUserId))
                            && !_context.AssignedCompanyLicenses.Any(a =>
                                a.CompanyLicenseId == l.Id && a.IsActive && !a.IsDel
                                && a.BusinessUnit != null && a.BusinessUnit.IsDel != true))
                .OrderBy(l => l.Id)
                .Select(l => (int?)l.Id)
                .FirstOrDefaultAsync();
        }

        // Consumed = live licences held by a live Team Manager OR actively on a
        // live team, each licence counted once — the exact complement of
        // FindAvailableLicenseIdAsync's "free". A new Profile Admin's trial licence
        // is held by them, so a fresh profile reads 1 / 1 / 0.
        public Task<List<CloverBackoffice.Infrastructure.DTO.PlanManagement.PlanLicenseCountDto>> GetPlanLicenseCountsAsync(uint profileId)
            => _context.GetPlanLicenseCountsAsync(profileId);

        public async Task<LicenseUsage> GetLicenseUsageAsync(uint profileId)
        {
            var now = DateTime.UtcNow;
            var pid = (int)profileId;
            var teamManagers = _context.TeamManagersOf(pid);

            var live = _context.CompanyLicenses
                .AsNoTracking()
                .Where(l => l.ProfileId == profileId
                            && !l.IsDel
                            && (l.ExpiryDate == null || l.ExpiryDate > now));

            var total = await live.CountAsync();

            var assigned = await live.CountAsync(l =>
                (l.AssignedUserId != null && teamManagers.Any(u => u.UserId == l.AssignedUserId))
                || _context.AssignedCompanyLicenses.Any(a =>
                    a.CompanyLicenseId == l.Id && a.IsActive && !a.IsDel
                    && a.BusinessUnit != null && a.BusinessUnit.IsDel != true));

            var managers = await teamManagers.CountAsync();

            var teams = await _context.BusinessUnits
                .AsNoTracking()
                .CountAsync(b => b.ProfileId == profileId && b.IsDel != true);

            // RRIDA Admin extensions raise the capacity: per licence for the
            // profile, and per extended licence while it is live.
            // With a plan catalogue each licence allows its plan's teams (Light 1, Pro 3 + extra),
            // so the difference from TeamsPerLicense is carried here: MaxTeams = sum of limits.
            var limits = await _context.GetProfileTeamLimitsAsync(profileId);
            var extraLicenseTeams = 0;
            if (limits.LicenseExtraTeams.Count > 0 || limits.PlanLicenseLimits.Count > 0)
            {
                var liveIds = await live.Select(l => l.Id).ToListAsync();
                extraLicenseTeams = liveIds.Sum(id => limits.LimitOf(id) - limits.TeamsPerLicense);
            }

            return new LicenseUsage
            {
                TotalLicenses = total,
                AssignedLicenses = assigned,
                TotalTeamManagers = managers,
                TotalTeams = teams,
                TeamsPerLicense = limits.TeamsPerLicense,
                ExtraLicenseTeams = extraLicenseTeams
            };
        }

        // The live licence a Team Manager holds: one stamped with them, else one
        // actively on any of their teams (purchased or complimentary). Oldest first.
        public async Task<int?> FindUserLicenseIdAsync(int userId)
        {
            var now = DateTime.UtcNow;
            var live = _context.CompanyLicenses.AsNoTracking()
                .Where(l => !l.IsDel && (l.ExpiryDate == null || l.ExpiryDate > now));

            var held = await live.Where(l => l.AssignedUserId == userId)
                .OrderBy(l => l.Id).Select(l => (int?)l.Id).FirstOrDefaultAsync();
            if (held != null) return held;

            var teamIds = _context.ManagerTeamIds(userId);
            return await live
                .Where(l => _context.AssignedCompanyLicenses.Any(a =>
                                a.CompanyLicenseId == l.Id && a.IsActive && !a.IsDel && teamIds.Contains(a.BusinessUnitId))
                            || _context.ComplimentaryLicenseGrants.Any(g =>
                                g.CompanyLicenseId == l.Id && g.IsActive && !g.IsDel && teamIds.Contains(g.ComplimentaryBusinessUnitId)))
                .OrderBy(l => l.Id)
                .Select(l => (int?)l.Id)
                .FirstOrDefaultAsync();
        }

        // Gives a Team Manager exactly one licence: the one they already hold,
        // else the first free one. Null when the user is not a live Team Manager
        // of the profile (Profile Admin / Portfolio Manager only need none) or no
        // licence is free — the user is never blocked, just left unlicensed.
        // Runs under the per-profile licence lock, so two users can never be
        // given the same licence; joins the caller's transaction when one is open.
        public async Task<int?> AssignLicenseToUserAsync(uint profileId, int userId, int? actorUserId, string? planCode = null)
        {
            await using var tx = _context.Database.CurrentTransaction == null
                ? await _context.Database.BeginTransactionAsync()
                : null;
            try
            {
                await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock({ProfileLicenseLockKey}, {(int)profileId})");

                int? licenseId = null;
                if (await _context.TeamManagersOf((int)profileId).AnyAsync(u => u.UserId == userId))
                {
                    // The plan the admin chose, else the only free plan; with Light and Pro
                    // both free and no choice, none (the Profile Admin assigns one).
                    licenseId = await FindUserLicenseIdAsync(userId)
                                ?? (await _context.PickFreeLicenseAsync(profileId, planCode, useDefaultPlan: false)).LicenseId;
                    if (licenseId != null)
                        await SetLicenseHolderAsync(licenseId.Value, userId, actorUserId);
                }

                if (tx != null) await tx.CommitAsync();
                return licenseId;
            }
            catch
            {
                if (tx != null) await tx.RollbackAsync();
                throw;
            }
        }

        // Stamps (userId) or clears (null) the licence's holder. No-op when unchanged.
        public async Task SetLicenseHolderAsync(int companyLicenseId, int? userId, int? actorUserId)
        {
            var license = await _context.CompanyLicenses.FirstOrDefaultAsync(l => l.Id == companyLicenseId);
            if (license == null || license.AssignedUserId == userId) return;

            var now = DateTime.UtcNow;
            license.AssignedUserId = userId;
            license.AssignedUserAt = userId == null ? null : now;
            license.UpdatedAt = now;
            license.UpdatedBy = actorUserId;
            await _context.SaveChangesAsync();
        }

        // Same "free" rule as FindAvailableLicenseIdAsync, purchased licences only
        // (anything that is not a trial). Used to move a Team Manager off a trial.
        public Task<int?> FindAvailablePaidLicenseIdAsync(uint profileId)
        {
            var now = DateTime.UtcNow;
            var teamManagers = _context.TeamManagersOf((int)profileId);
            return _context.CompanyLicenses
                .AsNoTracking()
                .Where(l => l.ProfileId == profileId
                            && !l.IsDel
                            && l.LicenseType != "Trial"
                            && (l.ExpiryDate == null || l.ExpiryDate > now)
                            && !(l.AssignedUserId != null && teamManagers.Any(u => u.UserId == l.AssignedUserId))
                            && !_context.AssignedCompanyLicenses.Any(a =>
                                a.CompanyLicenseId == l.Id && a.IsActive && !a.IsDel
                                && a.BusinessUnit != null && a.BusinessUnit.IsDel != true))
                .OrderBy(l => l.Id)
                .Select(l => (int?)l.Id)
                .FirstOrDefaultAsync();
        }

        // Ends a trial licence early because its Team Manager got a purchased one:
        // closes its team assignment, revokes the complimentary grants riding on
        // it, clears the holder, expires the row now (ExpiryDate = now, IsDel) and
        // stamps TerminatedAt / ReplacedByLicenseId so nothing revives it.
        // ExpiryNotifiedAt is stamped too: this is a conversion, not a lapse, so
        // no "licence expired" email goes out. Run it inside the caller's
        // transaction (the conversion is all-or-nothing).
        public async Task TerminateTrialLicenseAsync(int trialLicenseId, int replacedByLicenseId, int? actorUserId)
        {
            var trial = await _context.CompanyLicenses.FirstOrDefaultAsync(l => l.Id == trialLicenseId)
                ?? throw new InvalidOperationException($"License {trialLicenseId} not found.");
            if (!string.Equals(trial.LicenseType, "Trial", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"License {trialLicenseId} is not a trial license.");

            var now = DateTime.UtcNow;

            await UnassignAsync(trialLicenseId, actorUserId);
            foreach (var grant in await GetActiveComplimentaryGrantsForLicenseAsync(trialLicenseId))
                await RevokeComplimentaryGrantAsync(grant.Id, actorUserId);

            trial.AssignedUserId = null;
            trial.AssignedUserAt = null;
            trial.ExpiryDate = now;
            trial.IsDel = true;
            trial.TerminatedAt = now;
            trial.ReplacedByLicenseId = replacedByLicenseId;
            trial.ExpiryNotifiedAt ??= now;
            trial.UpdatedAt = now;
            trial.UpdatedBy = actorUserId;
            await _context.SaveChangesAsync();

            var version = await _context.CompanyLicenseAuditLogs
                .Where(a => a.Id == trial.Id)
                .Select(a => (int?)a.VersionNumber)
                .MaxAsync() ?? 0;
            _context.CompanyLicenseAuditLogs.Add(new CompanyLicenseAuditLog
            {
                Id = trial.Id,
                ProfileId = trial.ProfileId,
                LicenseKey = trial.LicenseKey,
                LicenseType = trial.LicenseType,
                CompanySubscriptionId = trial.CompanySubscriptionId,
                PlanId = trial.PlanId,
                ExpiryDate = trial.ExpiryDate,
                Notes = trial.Notes,
                CreatedBy = trial.CreatedBy,
                UpdatedBy = trial.UpdatedBy,
                CreatedAt = trial.CreatedAt,
                UpdatedAt = trial.UpdatedAt,
                IsDel = trial.IsDel,
                LoggedAt = now,
                VersionNumber = version + 1,
                OperationType = "UPDATE"
            });
            await _context.SaveChangesAsync();
        }

        // allowFreeLicense = false: only the manager's own licence may cover the
        // team (one already on their other team, or one they hold with no team
        // yet). User-creation flows give the manager a licence first
        // (AssignLicenseToUserAsync), so the team is covered by that one.
        public async Task<bool> EnsureTeamCoverageAsync(uint businessUnitId, int? actorUserId, bool allowFreeLicense = true)
        {
            var profileId = await _context.BusinessUnits
                .Where(b => b.Id == businessUnitId && b.IsDel != true)
                .Select(b => (uint?)b.ProfileId)
                .FirstOrDefaultAsync();
            if (profileId == null) return false;

            await using var tx = _context.Database.CurrentTransaction == null
                ? await _context.Database.BeginTransactionAsync()
                : null;
            try
            {
                // Same per-profile lock as Add User / team creation, so two teams
                // can never be given the same free licence row.
                await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock({ProfileLicenseLockKey}, {(int)profileId.Value})");

                var covered = await UnitHasLiveLicenseAsync(businessUnitId)
                              || await TryCoverWithManagerLicenseAsync(businessUnitId, actorUserId)
                              || await TryCoverWithHeldLicenseAsync(businessUnitId, actorUserId)
                              || (allowFreeLicense && await TryCoverWithFreeLicenseAsync(profileId.Value, businessUnitId, actorUserId));

                if (tx != null) await tx.CommitAsync();
                return covered;
            }
            catch
            {
                if (tx != null) await tx.RollbackAsync();
                throw;
            }
        }

        // The manager's own licence covers their next team (up to 3 in total).
        private async Task<bool> TryCoverWithManagerLicenseAsync(uint businessUnitId, int? actorUserId)
        {
            if (await GetActiveComplimentaryGrantForUnitAsync(businessUnitId) != null) return false;

            var source = await FindPurchasedComplimentarySourceForUnitAsync(businessUnitId);
            if (source == null) return false;

            try
            {
                await AddComplimentaryGrantAsync(
                    source.CompanyLicenseId, source.SourceBusinessUnitId, businessUnitId, source.ManagerUserId, actorUserId);
                return true;
            }
            catch (InvalidOperationException)
            {
                // The licence filled up since the lookup (re-checked under its lock
                // before anything is written); fall back to a free licence row.
                return false;
            }
        }

        // A licence the team's manager holds but has not put on a team yet (the
        // Profile Admin's trial licence, or one auto-assigned at user creation)
        // becomes this team's licence.
        private async Task<bool> TryCoverWithHeldLicenseAsync(uint businessUnitId, int? actorUserId)
        {
            var now = DateTime.UtcNow;
            var managerIds = _context.UserTeams
                .Where(ut => ut.UnitId == businessUnitId && (!ut.IsUnAssigned || ut.IsPendingActivation))
                .Select(ut => ut.UserId);

            var held = await _context.CompanyLicenses.AsNoTracking()
                .Where(l => !l.IsDel
                            && (l.ExpiryDate == null || l.ExpiryDate > now)
                            && l.AssignedUserId != null
                            && managerIds.Contains(l.AssignedUserId.Value)
                            && !_context.AssignedCompanyLicenses.Any(a =>
                                a.CompanyLicenseId == l.Id && a.IsActive && !a.IsDel
                                && a.BusinessUnit != null && a.BusinessUnit.IsDel != true))
                .OrderBy(l => l.Id)
                .Select(l => (int?)l.Id)
                .FirstOrDefaultAsync();
            if (held == null) return false;

            await AssignAsync(held.Value, businessUnitId, actorUserId);
            return true;
        }

        private async Task<bool> TryCoverWithFreeLicenseAsync(uint profileId, uint businessUnitId, int? actorUserId)
        {
            // Light and Pro both free: the company default plan (Pro), so the team is
            // still covered and never by a random plan.
            var free = (await _context.PickFreeLicenseAsync(profileId, null, useDefaultPlan: true)).LicenseId;
            if (free == null) return false;

            await AssignAsync(free.Value, businessUnitId, actorUserId);
            return true;
        }

        // Covered := a live license actively on the unit, purchased or complimentary.
        public Task<bool> UnitHasLiveLicenseAsync(uint businessUnitId)
        {
            var now = DateTime.UtcNow;
            return _context.CompanyLicenses
                .AnyAsync(l => !l.IsDel
                               && (l.ExpiryDate == null || l.ExpiryDate > now)
                               && (_context.AssignedCompanyLicenses.Any(a =>
                                       a.CompanyLicenseId == l.Id && a.BusinessUnitId == businessUnitId && a.IsActive && !a.IsDel)
                                   || _context.ComplimentaryLicenseGrants.Any(g =>
                                       g.CompanyLicenseId == l.Id && g.ComplimentaryBusinessUnitId == businessUnitId && g.IsActive && !g.IsDel)));
        }

        // ---------- complimentary (same-manager) grants ----------

        public Task<List<ComplimentaryLicenseGrant>> GetActiveComplimentaryGrantsForLicenseAsync(int companyLicenseId)
            => _context.ComplimentaryLicenseGrants
                .Where(g => g.CompanyLicenseId == companyLicenseId && g.IsActive && !g.IsDel)
                .OrderBy(g => g.GrantedAt).ThenBy(g => g.Id)
                .ToListAsync();

        public Task<ComplimentaryLicenseGrant?> GetActiveComplimentaryGrantForUnitAsync(uint businessUnitId)
            => _context.ComplimentaryLicenseGrants
                .FirstOrDefaultAsync(g => g.ComplimentaryBusinessUnitId == businessUnitId && g.IsActive && !g.IsDel);

        public Task<List<ComplimentaryLicenseGrant>> GetActiveComplimentaryGrantsForLicensesAsync(IEnumerable<int> companyLicenseIds)
        {
            var ids = companyLicenseIds.ToList();
            return _context.ComplimentaryLicenseGrants
                .AsNoTracking()
                .Where(g => ids.Contains(g.CompanyLicenseId) && g.IsActive && !g.IsDel)
                .ToListAsync();
        }

        public Task<List<ComplimentaryLicenseGrant>> GetActiveComplimentaryGrantsByCompanyAsync(uint profileId)
            => (from g in _context.ComplimentaryLicenseGrants.AsNoTracking()
                join l in _context.CompanyLicenses on g.CompanyLicenseId equals l.Id
                where g.IsActive && !g.IsDel && l.ProfileId == profileId
                select g).ToListAsync();

        // Shared manager := a user actively assigned (UserTeams, IsUnAssigned=false)
        // to BOTH units. Prefer the most-recently-assigned shared user so the
        // recorded ManagerUserId lines up with the "latest UserTeam" manager the
        // team hierarchy UI shows.
        public async Task<int?> GetSharedManagerUserIdAsync(uint unitA, uint unitB)
        {
            var usersOfA = _context.UserTeams
                .Where(ut => ut.UnitId == unitA && !ut.IsUnAssigned)
                .Select(ut => ut.UserId);

            return await _context.UserTeams
                .Where(ut => ut.UnitId == unitB && !ut.IsUnAssigned && usersOfA.Contains(ut.UserId))
                .OrderByDescending(ut => ut.CreatedDate)
                .ThenByDescending(ut => ut.Id)
                .Select(ut => (int?)ut.UserId)
                .FirstOrDefaultAsync();
        }

        public Task<bool> UnitHasActiveManagerAsync(uint businessUnitId)
            => _context.UserTeams
                .AnyAsync(ut => ut.UnitId == businessUnitId && !ut.IsUnAssigned);

        // Shares the same "manager := active UserTeam" definition as
        // GetSharedManagerUserIdAsync. A candidate source is a sibling team that
        // (a) shares an active manager with the target, (b) currently holds an
        // active purchased assignment, and (c) whose
        // license still has a free team slot (a license covers at most its team
        // limit: LicenseTeamLimits.MaxTeamsPerLicense, plus any RRIDA Admin
        // extension). Ordered so the most-recently-assigned shared manager/team wins.
        public async Task<ComplimentaryLicenseSourceDto?> FindPurchasedComplimentarySourceForUnitAsync(uint targetUnitId)
        {
            var targetManagers = _context.UserTeams
                .Where(ut => ut.UnitId == targetUnitId && !ut.IsUnAssigned)
                .Select(ut => ut.UserId);

            var query =
                from ut in _context.UserTeams
                where ut.UnitId != targetUnitId
                   && !ut.IsUnAssigned
                   && targetManagers.Contains(ut.UserId)
                join a in _context.AssignedCompanyLicenses
                    on ut.UnitId equals a.BusinessUnitId
                where a.IsActive && !a.IsDel
                orderby ut.CreatedDate descending, ut.Id descending
                select new
                {
                    Source = new ComplimentaryLicenseSourceDto
                    {
                        CompanyLicenseId = a.CompanyLicenseId,
                        SourceBusinessUnitId = ut.UnitId,
                        ManagerUserId = ut.UserId
                    },
                    Grants = _context.ComplimentaryLicenseGrants
                        .Count(g => g.CompanyLicenseId == a.CompanyLicenseId && g.IsActive && !g.IsDel)
                };

            // The free-slot check needs each licence's own limit, so it runs on the
            // (few) candidates, in the same order.
            foreach (var candidate in await query.ToListAsync())
            {
                var maxGrants = await _context.LicenseTeamLimitAsync(candidate.Source.CompanyLicenseId) - 1;
                if (candidate.Grants < maxGrants)
                    return candidate.Source;
            }
            return null;
        }

        public async Task<ComplimentaryLicenseGrant> AddComplimentaryGrantAsync(
            int companyLicenseId, uint primaryBusinessUnitId, uint complimentaryBusinessUnitId,
            int managerUserId, int? actorUserId)
        {
            // Joins the caller's transaction when one is open (EnsureTeamCoverageAsync).
            await using var tx = _context.Database.CurrentTransaction == null
                ? await _context.Database.BeginTransactionAsync()
                : null;
            try
            {
                // Serialize grants per license, then re-check the limit under the
                // lock (the DB no longer enforces one active grant per license).
                await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock({ComplimentaryGrantLockKey}, {companyLicenseId})");

                var activeGrants = await _context.ComplimentaryLicenseGrants
                    .CountAsync(g => g.CompanyLicenseId == companyLicenseId && g.IsActive && !g.IsDel);
                var teamLimit = await _context.LicenseTeamLimitAsync(companyLicenseId);
                if (activeGrants >= teamLimit - 1)
                    throw new InvalidOperationException(
                        $"Team limit reached: license already covers {LicenseTeamLimits.FormatUsage(activeGrants + 1, teamLimit)} teams. " +
                        "Purchase another license to add more teams for this manager.");

                var now = DateTime.UtcNow;
                var grant = new ComplimentaryLicenseGrant
                {
                    CompanyLicenseId = companyLicenseId,
                    PrimaryBusinessUnitId = primaryBusinessUnitId,
                    ComplimentaryBusinessUnitId = complimentaryBusinessUnitId,
                    ManagerUserId = managerUserId,
                    GrantedAt = now,
                    IsActive = true,
                    CreatedBy = actorUserId,
                    UpdatedBy = actorUserId,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                _context.ComplimentaryLicenseGrants.Add(grant);
                await _context.SaveChangesAsync();
                await WriteGrantAuditAsync(grant, "INSERT");

                if (tx != null) await tx.CommitAsync();
                return grant;
            }
            catch
            {
                // A joined transaction is the caller's to roll back.
                if (tx != null) await tx.RollbackAsync();
                throw;
            }
        }

        public async Task<bool> RevokeComplimentaryGrantAsync(int grantId, int? actorUserId)
        {
            var grant = await _context.ComplimentaryLicenseGrants
                .FirstOrDefaultAsync(g => g.Id == grantId && g.IsActive && !g.IsDel);
            if (grant == null) return false;

            var now = DateTime.UtcNow;
            grant.IsActive = false;
            grant.RevokedAt = now;
            grant.UpdatedAt = now;
            grant.UpdatedBy = actorUserId;
            await _context.SaveChangesAsync();
            await WriteGrantAuditAsync(grant, "UPDATE");
            return true;
        }

        public async Task<int> RevokeStaleGrantsForUnitAsync(uint unitId, int? actorUserId)
        {
            var grants = await _context.ComplimentaryLicenseGrants
                .Where(g => g.IsActive && !g.IsDel
                         && (g.PrimaryBusinessUnitId == unitId || g.ComplimentaryBusinessUnitId == unitId))
                .ToListAsync();

            return await RevokeStaleAsync(grants, actorUserId);
        }

        public async Task<int> RevokeAllStaleGrantsAsync(int? actorUserId)
        {
            var grants = await _context.ComplimentaryLicenseGrants
                .Where(g => g.IsActive && !g.IsDel)
                .ToListAsync();

            return await RevokeStaleAsync(grants, actorUserId);
        }

        // Revokes each grant in the set whose primary and complimentary units no
        // longer share a manager. Shared eligibility is re-evaluated with the
        // same rule used at grant time (GetSharedManagerUserIdAsync).
        private async Task<int> RevokeStaleAsync(List<ComplimentaryLicenseGrant> grants, int? actorUserId)
        {
            var revoked = 0;
            foreach (var grant in grants)
            {
                var sharedManager = await GetSharedManagerUserIdAsync(
                    grant.PrimaryBusinessUnitId, grant.ComplimentaryBusinessUnitId);

                if (sharedManager == null && await RevokeComplimentaryGrantAsync(grant.Id, actorUserId))
                    revoked++;
            }
            return revoked;
        }

        // Audit-write for grants — mirror of WriteAssignmentAuditAsync but on the
        // dedicated ComplimentaryLicenseGrantAuditLogs table so complimentary
        // coverage is reported separately from purchased assignments.
        private async Task WriteGrantAuditAsync(ComplimentaryLicenseGrant entity, string operationType)
        {
            var lastVersion = await _context.ComplimentaryLicenseGrantAuditLogs
                .Where(a => a.Id == entity.Id)
                .OrderByDescending(a => a.VersionNumber)
                .Select(a => (int?)a.VersionNumber)
                .FirstOrDefaultAsync();

            _context.ComplimentaryLicenseGrantAuditLogs.Add(new ComplimentaryLicenseGrantAuditLog
            {
                Id = entity.Id,
                CompanyLicenseId = entity.CompanyLicenseId,
                PrimaryBusinessUnitId = entity.PrimaryBusinessUnitId,
                ComplimentaryBusinessUnitId = entity.ComplimentaryBusinessUnitId,
                ManagerUserId = entity.ManagerUserId,
                GrantedAt = entity.GrantedAt,
                RevokedAt = entity.RevokedAt,
                IsActive = entity.IsActive,
                CreatedBy = entity.CreatedBy,
                UpdatedBy = entity.UpdatedBy,
                CreatedAt = entity.CreatedAt,
                UpdatedAt = entity.UpdatedAt,
                IsDel = entity.IsDel,
                LoggedAt = DateTime.UtcNow,
                VersionNumber = (lastVersion ?? 0) + 1,
                OperationType = operationType
            });
            await _context.SaveChangesAsync();
        }

        // Internal audit-write — mirror of LLMReportScheduleRepository.WriteAuditAsync.
        // VersionNumber auto-increments per AssignmentId so the history reads
        // 1, 2, 3 … no matter how many edits happen.
        private async Task WriteAssignmentAuditAsync(AssignedCompanyLicense entity, string operationType)
        {
            var lastVersion = await _context.AssignedCompanyLicenseAuditLogs
                .Where(a => a.Id == entity.Id)
                .OrderByDescending(a => a.VersionNumber)
                .Select(a => (int?)a.VersionNumber)
                .FirstOrDefaultAsync();

            _context.AssignedCompanyLicenseAuditLogs.Add(new AssignedCompanyLicenseAuditLog
            {
                Id = entity.Id,
                CompanyLicenseId = entity.CompanyLicenseId,
                BusinessUnitId = entity.BusinessUnitId,
                AssignedAt = entity.AssignedAt,
                UnassignedAt = entity.UnassignedAt,
                IsActive = entity.IsActive,
                GracePeriod = entity.GracePeriod,
                CreatedBy = entity.CreatedBy,
                UpdatedBy = entity.UpdatedBy,
                CreatedAt = entity.CreatedAt,
                UpdatedAt = entity.UpdatedAt,
                IsDel = entity.IsDel,
                LoggedAt = DateTime.UtcNow,
                VersionNumber = (lastVersion ?? 0) + 1,
                OperationType = operationType
            });
            await _context.SaveChangesAsync();
        }
    }
}
