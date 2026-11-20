using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CloverBackoffice.Components;
using CloverBackoffice.Components.Enums;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.BusinessModel;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Implementation;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Services.Implementation
{
    // Creating a Team Manager who must have a team, in the company their email
    // domain names.
    //
    // Everything here composes services that already exist rather than
    // reimplementing them: IAccountRegistrationService creates the user exactly
    // as the user list does, ICompanyTeamService creates the unit exactly as the
    // team screens do. What this class adds is the organisation lookup, the
    // company it provisions when there is nothing to join, the manager-to-team
    // row, and the transaction that holds them together.
    //
    // Nothing in this file changes the behaviour of the endpoints those services
    // already serve.
    public class ManagerOnboardingService : IManagerOnboardingService
    {
        private readonly ApplicationDbContext _context;
        private readonly IAccountRegistrationService _accountRegistration;
        private readonly ICompanyTeamService _companyTeamService;
        private readonly ICompanyLicenseService _licenseService;

        // First key of pg_advisory_xact_lock(int, int); the second is the profile id.
        // Shared with Add User and every team-creating path.
        private const int LicenseLockNamespace = 16016;

        private readonly ITeamOnboardingNotificationService _onboardingNotification;
        private readonly ITenantAccessService _tenantAccess;

        public ManagerOnboardingService(
            ApplicationDbContext context,
            IAccountRegistrationService accountRegistration,
            ICompanyTeamService companyTeamService,
            ICompanyLicenseService licenseService,
            ITeamOnboardingNotificationService onboardingNotification,
            ITenantAccessService tenantAccess)
        {
            _context = context;
            _accountRegistration = accountRegistration;
            _companyTeamService = companyTeamService;
            _licenseService = licenseService;
            _onboardingNotification = onboardingNotification;
            _tenantAccess = tenantAccess;
        }

        /// <summary>
        /// Creates a new Team Manager AND settles the team they will manage, as
        /// one unit of work.
        ///
        /// Two different rules decide where the manager lands.
        ///
        /// The PROFILE is not chosen by the caller. The manager's EMAIL DOMAIN
        /// names the organisation — "abc" for manager@abc.com, manager@abc.co.uk
        /// and manager@abc.co.us alike — and:
        ///   * "abc" already has a profile — it is REUSED, and the manager and
        ///     team are attached to it;
        ///   * nothing matches "abc" — the request is REFUSED. This endpoint no
        ///     longer creates a profile; register the profile first through
        ///     profile registration, then add its managers here.
        ///
        /// The TEAM is always chosen by the caller, and choosing one is mandatory:
        /// an existing team of that same company, or a new team created in this
        /// request. A manager is never created without one, which is the whole
        /// point of this endpoint existing.
        ///
        /// A manager without a team is the case this removes, so the writes cannot
        /// be allowed to come apart: the company, the team, the user row and the
        /// assignment share a single transaction and either all land or none do.
        ///
        /// The invitation is sent by the caller afterwards. Sending it from here
        /// would mean a mail failure could roll back a committed manager, and a
        /// failed creation could leave an invitation already in the inbox.
        /// </summary>
        public async Task<CreateManagerWithTeamResultDto> CreateManagerWithTeamAsync(
            CreateManagerWithTeamRequestDto dto, CancellationToken ct = default)
        {
            var result = new CreateManagerWithTeamResultDto();

            if (dto == null)
            {
                result.ErrorMessage = ResponseMessages.Errors.RequestBodyEmpty;
                return result;
            }

            // ── Shape of the request ─────────────────────────────────────────
            //
            // Same checks upsert-user makes, in the same order, so a manager
            // created here is validated exactly as any other user is. The name is
            // checked the way the value is BUILT — "First Last" — so it is empty
            // only when both halves are blank.
            if (string.IsNullOrWhiteSpace($"{dto.FirstName} {dto.LastName}"))
            {
                result.ErrorMessage = ResponseMessages.Errors.UserNameRequired;
                return result;
            }

            if (string.IsNullOrWhiteSpace(dto.Email))
            {
                result.ErrorMessage = ResponseMessages.Errors.EmailPasswordRequired;
                return result;
            }

            // ── Which organisation is this? ──────────────────────────────────
            //
            // Before the team is looked at, because the team is resolved inside
            // whatever company this names. A public suffix on its own
            // ("someone@co.uk"), a malformed address or an IP-literal domain names
            // no organisation and cannot be placed.
            var organization = EmailOrganizationResolver.FromEmail(dto.Email);
            if (organization == null)
            {
                result.ErrorMessage = ResponseMessages.Errors.ManagerEmailDomainUnresolved;
                return result;
            }

            // The disposable/free-domain blacklist, unchanged: enforced in
            // production and switched off in Development/UAT, exactly as it is for
            // every other registration path. See AccountRegistrationService.
            var domainCheck = await _accountRegistration.IsDomainValidAsync(dto.Email);
            if (!domainCheck.IsValid)
            {
                result.ErrorMessage = ResponseMessages.Errors.InvalidEmail;
                return result;
            }

            var existingUser = await _accountRegistration.UserEmailExistsAsync(dto.Email);
            if (existingUser != null)
            {
                result.ErrorMessage = ResponseMessages.Errors.UserEmailAlreadyExists;
                return result;
            }

            // ── The team, which is mandatory ─────────────────────────────────
            //
            // Exactly one route. Comparing the two flags for equality covers both
            // bad cases in one test: neither chosen, or both. Both is rejected
            // rather than resolved by preference, because silently picking one
            // would hide a frontend bug. Neither is rejected because a manager
            // without a team is precisely what this endpoint exists to prevent —
            // and it is refused BEFORE anything is written, so no company, team or
            // user is left behind and no invitation goes out.
            // isNewTeam is the only flag that decides this, so it is read first and
            // existingUnitId only matters when it is false. A caller that sends an
            // existingUnitId alongside isNewTeam true gets a new team: the flag
            // wins rather than the request being rejected as contradictory.
            bool assignExisting = !dto.IsNewTeam;
            var requestedExistingUnitId = assignExisting ? dto.ExistingUnitId : null;

            if (assignExisting && requestedExistingUnitId is not > 0)
            {
                result.ErrorMessage = ResponseMessages.Errors.ManagerTeamRequired;
                return result;
            }

            // Which profile the manager lands in is decided differently by the two
            // routes, and deliberately so.
            //
            // EXISTING TEAM: the team decides. The caller named a specific unit, and
            // a unit belongs to exactly one profile, so that profile is the answer —
            // there is nothing to guess. Deriving it from the email domain instead
            // was wrong twice over: a domain can be shared by many profiles (this
            // database has thirteen on yopmail.com, and the oldest one silently
            // won), so picking a team from any of the others failed with "does not
            // belong to this company" and no way for the caller to fix it short of
            // changing the manager's email address. Naming somebody else's unit is
            // not a hole here: existingUnitId ends in "UnitId", so
            // CompanyTenantGuardFilter has already refused any unit the caller may
            // not reach.
            //
            // NEW TEAM: there is no unit yet, so the email domain is all there is.
            // The profile must already exist — manager onboarding attaches to a
            // registered profile and never creates one.
            CompanySummary? company;
            UnitSummary? existingUnit = null;

            if (assignExisting)
            {
                var unitId = (uint)requestedExistingUnitId!.Value;

                company = await FindCompanyForUnitAsync(unitId, ct);
                if (company == null)
                {
                    result.ErrorMessage = ResponseMessages.Errors.ManagerTeamNotFound;
                    return result;
                }

                existingUnit = await FindUnitInCompanyAsync(company.Id, unitId, ct);
                if (existingUnit == null)
                {
                    result.ErrorMessage = ResponseMessages.Errors.ManagerTeamNotFound;
                    return result;
                }
            }
            else
            {
                company = await FindCompanyForOrganizationAsync(organization.OrganizationKey, ct);
                if (company == null)
                {
                    result.ErrorMessage = ResponseMessages.Errors.ManagerCompanyNotFound;
                    return result;
                }
            }

            // ── Write ────────────────────────────────────────────────────────
            await using var transaction = await _context.Database.BeginTransactionAsync(ct);
            try
            {
                // Creating a Team Manager never needs a licence; a free one is
                // auto-assigned after the commit when one exists. A new team still has to fit the
                // team capacity (licences x 3), checked under the per-profile
                // licence lock Add User uses, held until this transaction ends.
                await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock({LicenseLockNamespace}, {(int)company.Id})", ct);

                string? licenseError = null;
                if (!assignExisting)
                    licenseError = (await _licenseService.GetLicenseUsageAsync(company.Id)).TeamCapacityError();
                if (licenseError != null)
                {
                    await transaction.RollbackAsync(ct);
                    result.ErrorMessage = licenseError;
                    return result;
                }

                // The ordinary create path, untouched: same deleted-email
                // handling, same pending/unverified status, same
                // FirstTimeUserAdded count as a user added from the user list.
                // Only the profile differs — it comes from the email domain rather
                // than from the request, and it always already exists.
                var userRequest = new UserUpsertDto
                {
                    UserId = 0,
                    Email = dto.Email,
                    FirstName = dto.FirstName,
                    LastName = dto.LastName,
                    ProfileId = (int)company.Id,
                    CreatedBy = dto.CreatedBy,

                    // Set here, not taken from the request. This endpoint creates
                    // Team Managers and nothing else.
                    UserGroupId = (int)UserGroups.TeamManager
                };

                var createdUser = await _accountRegistration.UpsertUserAsync(userRequest);
                if (createdUser == null || createdUser.UserId <= 0)
                {
                    await transaction.RollbackAsync(ct);
                    result.ErrorMessage = ResponseMessages.Errors.UserUpsertFail;
                    return result;
                }

                UnitSummary unit;
                var teamWasCreated = false;

                if (assignExisting)
                {
                    unit = existingUnit!;
                }
                else
                {
                    var newName = await ResolveNewTeamNameAsync(createdUser, (int)company.Id);

                    var unitRequest = new UpsertBusinessUnitDto
                    {
                        Id = 0,
                        TeamName = newName,
                        ProfileId = (int)company.Id,
                        // Always top-level. A parent is not part of this contract;
                        // the team hierarchy is arranged in team setup afterwards.
                        ParentId = null,

                        // Not collected during manager setup. Size, type, context
                        // and goals belong to team setup, which the Company Admin
                        // does separately; the column is nullable precisely so a
                        // team can exist before anyone has filled them in.
                        TeamSize = null,
                        CreatedBy = dto.CreatedBy,

                        // Team capacity was checked under the lock above.
                        LicenseAlreadyReserved = true
                    };

                    var createdUnit = await _companyTeamService.UpsertBusinessUnitAsync(unitRequest);

                    // UpsertBusinessUnitAsync reports failure by leaving Id at 0
                    // and putting the reason on message, rather than throwing.
                    if (createdUnit == null || createdUnit.Id <= 0)
                    {
                        await transaction.RollbackAsync(ct);
                        result.ErrorMessage = string.IsNullOrWhiteSpace(createdUnit?.message)
                            ? ResponseMessages.Errors.ManagerTeamCreateFailed
                            : createdUnit!.message;
                        return result;
                    }

                    unit = new UnitSummary((uint)createdUnit.Id, newName);
                    teamWasCreated = true;
                }

                var replacedManagerUserId = await AssignManagerToUnitAsync(
                    createdUser.UserId, unit.Id, dto.CreatedBy, teamWasCreated, ct);

                await _context.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);

                // A UserTeam change on a unit can affect the shared-manager basis
                // of a complimentary grant on it. Best-effort and after the commit,
                // exactly as assign-user-to-team does it: the reconciliation
                // background job is the backstop.
                try
                {
                    await _licenseService.ReconcileGrantsForUnitAsync(unit.Id, dto.CreatedBy);
                }
                catch
                {
                    // Swallowed by design; the safety-net job will catch it.
                }

                // The new Team Manager gets one free licence when one exists (none
                // free → still created, unlicensed); then the team is covered by it.
                try
                {
                    await _licenseService.AssignLicenseToUserAsync(company.Id, createdUser.UserId, dto.CreatedBy, dto.PlanCode);
                }
                catch
                {
                    // Swallowed by design; the admin can still assign one by managerId.
                }

                // Licence coverage for the team — only from the licence the
                // manager holds.
                try
                {
                    await _licenseService.EnsureTeamCoverageAsync(unit.Id, dto.CreatedBy, allowFreeLicense: false);
                }
                catch
                {
                    // Swallowed by design; the next manager change on the team retries it.
                }

                // Stops the onboarding job's catch-up pass mailing this manager a
                // second time about the team they were just created on — the one
                // invitation this request sends is the whole story. See the
                // method's own notes for why this is a marker rather than a change
                // to the notification rules.
                try
                {
                    await _onboardingNotification.SuppressAssignmentNotificationAsync(
                        createdUser.UserId, unit.Id,
                        "manager created together with this team; the invitation email covers it", ct);
                }
                catch
                {
                    // Swallowed by design; the service logs its own failures. The
                    // worst case is one extra email, never a failed creation.
                }

                result.IsSuccess = true;
                result.Data = new CreateManagerWithTeamResponseDto
                {
                    UserId = createdUser.UserId,
                    Email = createdUser.Email,
                    FirstName = createdUser.FirstName,
                    LastName = createdUser.LastName,
                    ProfileId = (int)company.Id,
                    CompanyName = createdUser.CompanyName,
                    UserGroupId = (int)UserGroups.TeamManager,
                    UnitId = unit.Id,
                    TeamName = unit.Name,
                    OrganizationKey = organization.OrganizationKey,
                    TeamWasCreated = teamWasCreated,
                    IsNewTeam = teamWasCreated,

                    // Only meaningful when an existing team was chosen; for a new
                    // team UnitId is the team that was just created.
                    ExistingUnitId = teamWasCreated ? null : (long?)unit.Id,

                    // The manager this one displaced, when the chosen team had one.
                    ReplacedManagerUserId = replacedManagerUserId,
                    FirstTimeUserAdded = createdUser.FirstTimeUserAdded,

                    // Set by the caller once the invitation has actually gone out.
                    InvitationSent = false
                };

                return result;
            }
            catch
            {
                try { await transaction.RollbackAsync(ct); } catch { /* the transaction is already gone */ }
                throw;
            }
        }

        /// <summary>
        /// What the manager-creation screen needs before it can offer a team.
        ///
        /// The company a manager joins comes from their email domain, so the teams
        /// that may be picked are that company's — which the screen cannot know
        /// until the address has been typed. This answers, for one address: which
        /// organisation it is, whether that organisation already has a company,
        /// and which live teams it has.
        ///
        /// No company exists for the domain yet means there is nothing to select
        /// and the admin must create a team with the manager.
        ///
        /// Read-only: nothing is created here, and calling it is not a commitment
        /// to create anything.
        /// </summary>
        public async Task<ResolveManagerOrganizationDto?> ResolveOrganizationAsync(
            string email, User? caller, CancellationToken ct = default)
        {
            var organization = EmailOrganizationResolver.FromEmail(email);
            if (organization == null)
                return null;

            var company = await FindCompanyForOrganizationAsync(organization.OrganizationKey, ct);

            var resolved = new ResolveManagerOrganizationDto
            {
                OrganizationKey = organization.OrganizationKey,
                CompanyExists = company != null,
                ProfileId = company == null ? null : (int)company.Id,

                // The name it has, or the name it would be given.
                CompanyName = company?.Name ?? organization.DisplayName
            };

            if (company == null)
                return resolved;

            // Team names and ids belong to the company that owns them. A caller
            // from another tenant is told only that the organisation exists and
            // what it is called — enough to finish onboarding with a new team,
            // and not enough to enumerate somebody else's structure.
            resolved.CanSelectTeams = caller != null
                && await _tenantAccess.CanAccessCompanyAsync(caller, company.Id);

            if (!resolved.CanSelectTeams)
            {
                resolved.ProfileId = null;
                return resolved;
            }

            var teams = await _context.BusinessUnits
                .AsNoTracking()
                .Where(bu => bu.Profile.Id == company.Id && bu.IsDel != true)
                .OrderBy(bu => bu.Id)
                .Select(bu => new { bu.Id, bu.Name })
                .ToListAsync(ct);

            if (teams.Count == 0)
                return resolved;

            // Which of them are already managed. One query rather than one per
            // team. A team has one manager, so picking a managed one is a takeover —
            // the client should warn before letting the admin choose it.
            var teamIds = teams.Select(t => t.Id).ToList();
            var managedUnitIds = await _context.UserTeams
                .AsNoTracking()
                .Where(ut => teamIds.Contains(ut.UnitId) && !ut.IsUnAssigned)
                .Select(ut => ut.UnitId)
                .Distinct()
                .ToListAsync(ct);

            var managed = new HashSet<uint>(managedUnitIds);

            resolved.Teams = teams
                .Select(t => new ManagerOnboardingTeamOptionDto
                {
                    UnitId = t.Id,
                    TeamName = t.Name,
                    HasManager = managed.Contains(t.Id)
                })
                .ToList();

            return resolved;
        }

        /// <summary>
        /// Where the holder of an invitation link goes once their password is set.
        ///
        /// A Team Manager now always has a team from the moment their account is
        /// created, so they go straight to THAT team's Team Health page and never
        /// through a team setup step — whether the team was picked for them or
        /// created with them. Everyone else gets the sign-in screen.
        ///
        /// Reads the same encrypted parameter the invitation carried, so this
        /// costs no extra token. Anything unreadable, or a team that has since
        /// been deleted, falls back to the default landing rather than failing:
        /// the password has already been set by the time this is called and that
        /// must not be reported to the user as an error.
        /// </summary>
        public async Task<PostPasswordLandingDto?> GetPostPasswordLandingAsync(
            string encryptedParam, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(encryptedParam))
                return null;

            ResetPasswordPayload decrypted;
            try
            {
                decrypted = BussinessManager.DecryptObject<ResetPasswordPayload>(encryptedParam);
            }
            catch (Exception)
            {
                return null;
            }

            if (decrypted == null || decrypted.UserId <= 0)
                return null;

            var user = await _context.Users
                .AsNoTracking()
                .Where(u => u.UserId == decrypted.UserId && !u.IsDel)
                .Select(u => new
                {
                    u.UserId,
                    u.UserGroupId,
                    RoleIds = u.Roles.Select(r => r.UserGroupId).ToList()
                })
                .FirstOrDefaultAsync(ct);

            if (user == null)
                return null;

            // Roles, not the derived column: a Profile Admin who is also a Team
            // Manager still lands on their team.
            var roleIds = user.RoleIds.Count > 0 ? user.RoleIds : new List<int> { user.UserGroupId };

            var landing = new PostPasswordLandingDto
            {
                UserId = user.UserId,
                RoleIds = roleIds,
                IsTeamManager = roleIds.Contains((int)UserGroups.TeamManager)
            };

            if (!landing.IsTeamManager)
                return landing;

            // The team they hold now. Newest first, so a manager who holds more
            // than one lands on the most recent — the one the invitation was
            // about.
            var assignment = await _context.UserTeams
                .AsNoTracking()
                .Where(ut => ut.UserId == user.UserId && !ut.IsUnAssigned)
                .OrderByDescending(ut => ut.CreatedDate)
                .ThenByDescending(ut => ut.Id)
                .FirstOrDefaultAsync(ct);

            if (assignment == null)
                return landing;

            // How this manager got the team, recorded when they were onboarded.
            // Rows written before the column existed read false, which is the
            // truthful answer for them: they were put on a team that already
            // existed.
            landing.IsNewTeam = assignment.IsNewTeam;
            landing.ExistingUnitId = assignment.IsNewTeam ? null : (long?)assignment.UnitId;

            var unit = await _context.BusinessUnits
                .AsNoTracking()
                .Where(bu => bu.Id == assignment.UnitId && bu.IsDel != true)
                .Select(bu => new { bu.Id, bu.Name })
                .FirstOrDefaultAsync(ct);

            if (unit == null)
                return landing;

            landing.UnitId = unit.Id;
            landing.TeamName = unit.Name;
            return landing;
        }

        // Just enough of a company / unit to place a manager, so nothing is
        // tracked or loaded that this does not use.
        private sealed record CompanySummary(uint Id, string Name);

        private sealed record UnitSummary(uint Id, string Name);

        /// <summary>
        /// The company an organisation key belongs to, or null when the domain has
        /// never been seen.
        ///
        /// Matching is on the ORGANISATION, not the host, so abc.com, abc.co.uk,
        /// abc.co.us and mail.abc.com all find the same company. The stored
        /// DomainName is a host and there is no organisation column to index, so
        /// this narrows in SQL to the rows that could possibly carry the key and
        /// then applies the real suffix rules to those few in memory. The key is a
        /// DNS label — letters, digits and hyphens — so it can carry no LIKE
        /// wildcard.
        ///
        /// Several companies sharing one domain is normal in this data: every
        /// registration path mints a fresh company and none of them checks the
        /// domain. The match is therefore pinned to the OLDEST live one, so a
        /// second manager on a domain lands where the first one did instead of
        /// wherever an unordered FirstOrDefault happened to point.
        /// </summary>
        private async Task<CompanySummary?> FindCompanyForOrganizationAsync(
            string organizationKey, CancellationToken ct)
        {
            // Lower-cased on BOTH sides: LIKE is case-sensitive on PostgreSQL and
            // DomainName is stored however the registering address was typed.
            var pattern = $"%{organizationKey}.%";

            var candidates = await _context.Profiles
                .AsNoTracking()
                .Where(c => !c.IsDel && EF.Functions.Like(c.DomainName.ToLower(), pattern))
                .OrderBy(c => c.Id)
                .Select(c => new { c.Id, c.DomainName, Name = c.ProfileName })
                .ToListAsync(ct);

            foreach (var candidate in candidates)
            {
                // Some historic rows hold a whole address rather than a host. Take
                // the host off either shape rather than skipping them.
                var host = HostOf(candidate.DomainName);
                var key = EmailOrganizationResolver.OrganizationKeyFromHost(host);

                if (string.Equals(key, organizationKey, StringComparison.OrdinalIgnoreCase))
                    return new CompanySummary(candidate.Id, candidate.Name);
            }

            return null;
        }

        /// <summary>
        /// One live team of a given company, or null. Both halves matter: a team
        /// id that exists but belongs to a different company must be refused, not
        /// silently used.
        /// </summary>
        /// <summary>
        /// The profile that owns a team. Used by the existing-team route, where the
        /// chosen unit — not the manager's email domain — decides which profile the
        /// manager joins.
        ///
        /// Returns null when the unit does not exist or is deleted, which the caller
        /// reports as "team not found" rather than as a profile problem: from the
        /// admin's point of view the team they picked is the thing that was wrong.
        /// </summary>
        private async Task<CompanySummary?> FindCompanyForUnitAsync(uint unitId, CancellationToken ct)
        {
            var owner = await _context.BusinessUnits
                .AsNoTracking()
                .Where(bu => bu.Id == unitId && bu.IsDel != true)
                .Select(bu => new { Id = bu.Profile.Id, Name = bu.Profile.ProfileName })
                .FirstOrDefaultAsync(ct);

            return owner == null ? null : new CompanySummary(owner.Id, owner.Name);
        }

        private async Task<UnitSummary?> FindUnitInCompanyAsync(uint profileId, uint unitId, CancellationToken ct)
        {
            var unit = await _context.BusinessUnits
                .AsNoTracking()
                .Where(bu => bu.Id == unitId && bu.IsDel != true && bu.Profile.Id == profileId)
                .Select(bu => new { bu.Id, bu.Name })
                .FirstOrDefaultAsync(ct);

            return unit == null ? null : new UnitSummary(unit.Id, unit.Name);
        }

        /// <summary>
        /// The name to store for a team created during manager setup.
        ///
        /// Always derived from the manager it was made for. The request carries no
        /// team name — a business unit cannot exist without one, but the admin is
        /// not asked to invent one just to get a manager created, and the Company
        /// Admin renames the team in team setup.
        ///
        /// Team names are unique per company, so a clash takes a numeric suffix
        /// rather than failing the whole creation. Uniqueness is checked against
        /// the company the EMAIL DOMAIN resolved to, which is the one the team is
        /// being created in.
        /// </summary>
        private async Task<string> ResolveNewTeamNameAsync(UserUpsertDto createdUser, int profileId)
        {
            // Always generated. The request has no team-name field: the admin is
            // never asked to invent one, and the Company Admin renames the team in
            // team setup if they want something else.
            var owner = $"{createdUser.FirstName} {createdUser.LastName}".Trim();
            if (owner.Length == 0)
                owner = createdUser.Email ?? "New";

            var baseName = TruncateTeamName($"{owner}'s Team");
            var candidate = baseName;

            for (var suffix = 2; suffix <= 50; suffix++)
            {
                var taken = await _accountRegistration.BussinessNameExistsAsync(candidate, profileId);
                if (taken == null)
                    return candidate;

                candidate = TruncateTeamName($"{baseName} {suffix}", suffix.ToString().Length + 1);
            }

            // Fifty managers of the same name in one company is not a real case;
            // fall back to something guaranteed unique rather than looping forever.
            return TruncateTeamName($"{baseName} {Guid.NewGuid():N}", 33);
        }

        // Business unit names are capped in the schema; a long manager name must
        // not turn a valid creation into a database error.
        private static string TruncateTeamName(string value, int reserve = 0) =>
            Truncate(value, DataConstants.OrganisationalUnitNameLength - reserve);

        /// <summary>
        /// Puts the manager on the team, replacing whoever held it.
        ///
        /// A team has ONE manager. A manager may hold several teams, but a team
        /// never has more than one, so assigning a manager to a team that is
        /// already managed is a takeover: the incumbent's row is flipped to
        /// unassigned and kept, so the record of who managed what survives, and
        /// the new row is inserted alongside it. This is the same rule
        /// CompanyTeamService.AssignUserToTeamAsync applies.
        ///
        /// The displaced manager is told: the caller sends them the standard
        /// "you have been unassigned" message once this has committed, so they
        /// know they have stopped receiving the team's reports and alerts.
        ///
        /// Deliberately writes the rows and nothing else — no SaveChanges, no
        /// notification. The caller owns the transaction.
        /// </summary>
        /// <returns>The displaced manager's user id, or null if the team had none.</returns>
        private async Task<int?> AssignManagerToUnitAsync(
            int managerUserId, uint unitId, int createdBy, bool teamWasCreated, CancellationToken ct)
        {
            var incumbents = await _context.UserTeams
                .Where(ut => ut.UnitId == unitId && !ut.IsUnAssigned)
                .ToListAsync(ct);

            int? replacedUserId = null;

            // A replacement still waiting for an invited manager to accept is
            // superseded by this assignment.
            var pendingOnUnit = await _context.UserTeams
                .Where(ut => ut.UnitId == unitId && ut.IsPendingActivation)
                .ToListAsync(ct);
            foreach (var pending in pendingOnUnit)
            {
                pending.IsPendingActivation = false;
                pending.IsUnAssigned = true;
            }

            foreach (var row in incumbents)
            {
                row.IsUnAssigned = true;
                _context.UserTeams.Update(row);

                // Only a DIFFERENT person counts as replaced. A brand-new manager
                // can never be the incumbent, but the guard keeps this method
                // honest if it is ever reused.
                if (row.UserId != managerUserId)
                    replacedUserId = row.UserId;
            }

            _context.UserTeams.Add(new UserTeam
            {
                UserId = managerUserId,
                UnitId = unitId,
                IsUnAssigned = false,
                IsReadOnly = false,
                CreatedBy = createdBy,
                CreatedDate = DateTime.UtcNow,

                // Stored because post-password-landing has to report it later and
                // cannot work it out from anything else.
                IsNewTeam = teamWasCreated
            });

            return replacedUserId;
        }

        // DomainName holds a host, but a few historic rows hold a whole address.
        private static string HostOf(string? domainName)
        {
            var value = (domainName ?? string.Empty).Trim();
            var at = value.LastIndexOf('@');

            return at >= 0 ? value[(at + 1)..] : value;
        }

        // Names and domains are capped in the schema; a long value must not turn a
        // valid creation into a database error.
        private static string Truncate(string value, int max) =>
            value.Length <= max ? value : value[..max];
    }
}
