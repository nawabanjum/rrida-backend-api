using CloverBackoffice.Components;
using CloverBackoffice.Components.Enums;
using CloverBackoffice.Infrastructure.BusinessModel;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Infrastructure.Models.TeamSetups;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CloverBackoffice.Repository.Implementation
{
    /// <summary>
    /// Team Setup — the whole of the five-step setup screen, stored as one operation.
    ///
    /// The unit keeps carrying what it always carried (name, size, parent, invite code);
    /// the team type and the manager's context go to TeamSetups, and the goals to
    /// TeamGoals with a snapshot of every change in TeamGoalAudits. See
    /// TeamSetupEntities.cs for why those are separate tables rather than columns on
    /// BusinessUnits.
    ///
    /// The unit-creating half deliberately mirrors CompanyTeamService.UpsertBusinessUnitAsync:
    /// same generated codes, same placeholder contact, same question set group, same
    /// licensing decision (a new team consumes no licence, must fit the profile's team
    /// capacity of licences x 3, and is given licence coverage after commit). Setting a team up through
    /// this endpoint and through upsert-team must produce the same unit.
    /// </summary>
    public partial class TeamSetupRepository : ITeamSetupRepository
    {
        private readonly ApplicationDbContext _context;
        private readonly ICompanyLicenseRepository _licenseRepo;

        // First key of pg_advisory_xact_lock(int, int); the second is the profile id.
        // Shared with Add User and upsert-team so every team-creating path queues
        // behind the same lock when they race for the last free licence.
        private const int LicenseLockNamespace = 16016;

        /// <summary>
        /// RRIDA only ever reports at team level, so a team of one could never keep a
        /// response anonymous. The setup screen says the same thing in words.
        /// </summary>
        private const int MinTeamSize = 2;

        /// <summary>Enough text to be worth reading back to the manager.</summary>
        private const int MinContextLength = 20;

        private const int MaxContextLength = 4000;
        private const int MinGoals = 2;
        private const int MaxGoals = 5;
        private const int MaxGoalTextLength = 1000;
        private const int MaxTeamNameLength = 140;

        public TeamSetupRepository(ApplicationDbContext context, ICompanyLicenseRepository licenseRepo)
        {
            _context = context;
            _licenseRepo = licenseRepo;
        }

        public async Task<UpsertTeamSetupResultDto> UpsertTeamSetupAsync(UpsertTeamSetupDto dto, int? actorUserId)
        {
            // ── Shape of the request ──
            var teamName = (dto.TeamName ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(teamName))
            {
                return Failed("TeamName is required.");
            }

            if (teamName.Length > MaxTeamNameLength)
            {
                return Failed($"TeamName cannot be longer than {MaxTeamNameLength} characters.");
            }

            // Range-checked, not just sign-checked, because these are cast to uint below.
            // An unchecked cast of a value past uint.MaxValue WRAPS — 4294967297 would
            // become 1 and quietly act on somebody else's team. CompanyTenantGuardFilter
            // does not catch it either: it drops out-of-range ids and leaves them to the
            // endpoint, which is exactly here.
            if (dto.ProfileId <= 0 || dto.ProfileId > uint.MaxValue)
            {
                return Failed("ProfileId is required.");
            }

            if (dto.UnitId < 0 || dto.UnitId > uint.MaxValue)
            {
                return Failed("UnitId is not valid.");
            }

            var isNewTeam = dto.UnitId == 0;

            // Required on create, optional on edit — the same rule upsert-team applies —
            // except that setup will not accept a team of one whichever way it arrives.
            if (isNewTeam && !dto.TeamSize.HasValue)
            {
                return Failed("TeamSize is required.");
            }

            if (dto.TeamSize.HasValue && dto.TeamSize.Value < MinTeamSize)
            {
                return Failed($"TeamSize must be at least {MinTeamSize}. RRIDA reports at team level, " +
                              "so a single response could never stay anonymous.");
            }

            if (!TryParseTeamType(dto.TeamType, out var teamType))
            {
                return Failed("TeamType is required and must be Interdependent or IndependentContributors.");
            }

            var context = (dto.Context ?? string.Empty).Trim();

            if (context.Length < MinContextLength)
            {
                return Failed($"Context is required and must be at least {MinContextLength} characters.");
            }

            if (context.Length > MaxContextLength)
            {
                return Failed($"Context cannot be longer than {MaxContextLength} characters.");
            }

            var goalError = NormalizeGoals(dto.Goals, out var goals);

            if (goalError != null)
            {
                return Failed(goalError);
            }

            // ── The tenant the request names ──
            var profileId = (uint)dto.ProfileId;

            var company = await _context.Profiles.FirstOrDefaultAsync(c => c.Id == profileId);

            if (company == null)
            {
                return Failed($"Company {dto.ProfileId} was not found.");
            }

            // Three states, not two. Null means "leave the parent alone" — the setup screen
            // has no parent step, so an edit that simply omits the field must not silently
            // re-root the team. 0 means "clear it", which is the value upsert-team treats as
            // no parent. Anything else names the parent.
            if (dto.ParentId is > int.MaxValue or < 0)
            {
                return Failed("ParentId is not valid.");
            }

            var parentSpecified = dto.ParentId.HasValue;
            var parentId = dto.ParentId is null or 0 ? null : (int?)dto.ParentId;

            if (parentId.HasValue)
            {
                var parentExists = await _context.BusinessUnits
                    .AnyAsync(u => u.Id == (uint)parentId.Value
                                   && u.Profile.Id == profileId
                                   && u.IsDel != true);

                if (!parentExists)
                {
                    return Failed($"Parent team {parentId.Value} was not found in company {dto.ProfileId}.");
                }
            }

            BusinessUnit? existingUnit = null;

            if (!isNewTeam)
            {
                var unitId = (uint)dto.UnitId;

                existingUnit = await _context.BusinessUnits
                    .Include(u => u.Profile)
                    .FirstOrDefaultAsync(u => u.Id == unitId && u.IsDel != true);

                if (existingUnit == null)
                {
                    return Failed($"Team {dto.UnitId} was not found.");
                }

                // Checked against the company as well as by id, for the same reason
                // CustomQuestions checks it: a team edited under the wrong company would be
                // invisible to both tenants.
                if (existingUnit.Profile == null || existingUnit.Profile.Id != profileId)
                {
                    return Failed($"Team {dto.UnitId} does not belong to company {dto.ProfileId}.");
                }

                if (parentId.HasValue && parentId.Value == dto.UnitId)
                {
                    return Failed("A team cannot be its own parent.");
                }
            }

            // Same uniqueness rule the existing team endpoint applies, checked here as well
            // so this endpoint cannot be used to get around it.
            if (await IsTeamNameTakenAsync(profileId, teamName, existingUnit?.Id))
            {
                return Failed("Bussiness name must be unique.");
            }

            // The goals that are already stored, read before anything is written so a bad
            // goal id is a failed result with a reason rather than a rolled-back 500.
            var existingGoals = isNewTeam
                ? new List<TeamGoal>()
                : await _context.TeamGoals
                    .Where(g => g.UnitId == existingUnit!.Id && !g.IsDel)
                    .ToListAsync();

            var storedGoalIds = existingGoals.Select(g => g.Id).ToHashSet();

            foreach (var goal in goals.Where(g => g.TeamGoalId > 0))
            {
                if (!storedGoalIds.Contains((uint)goal.TeamGoalId))
                {
                    return Failed(isNewTeam
                        ? $"Goal {goal.TeamGoalId} cannot be edited while the team is being created."
                        : $"Goal {goal.TeamGoalId} does not belong to team {dto.UnitId}.");
                }
            }

            // A Team Manager creating a team becomes its manager (AssignSetupManagerAsync),
            // so the new team must fit their manager licence team limit.
            if (isNewTeam && actorUserId.HasValue && await IsSetupTeamManagerAsync(actorUserId.Value, profileId))
            {
                var limitError = await _context.ManagerTeamLimitErrorAsync(actorUserId.Value);
                if (limitError != null)
                {
                    return Failed(limitError);
                }
            }

            var now = DateTime.UtcNow;

            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // Teams do not consume a licence (a Team Manager does). A new team
                // only has to fit the profile's team capacity (licences x 3), checked
                // under the per-profile advisory lock, held for this transaction.
                if (isNewTeam)
                {
                    await _context.Database.ExecuteSqlInterpolatedAsync(
                        $"SELECT pg_advisory_xact_lock({LicenseLockNamespace}, {(int)profileId})");

                    var capacityError = (await _licenseRepo.GetLicenseUsageAsync(profileId)).TeamCapacityError();
                    if (capacityError != null)
                    {
                        await transaction.RollbackAsync();
                        return Failed(capacityError);
                    }
                }

                var unit = isNewTeam
                    ? await CreateUnitAsync(company, teamName, dto.TeamSize, parentId)
                    : UpdateUnit(existingUnit!, teamName, dto.TeamSize, parentId, parentSpecified);

                await _context.SaveChangesAsync();

                // Only a team this request created. An edit never changes who manages the
                // team — that is Add User's and assign-user-to-team's job.
                var assignedManagerUserId = isNewTeam
                    ? await AssignSetupManagerAsync(unit.Id, profileId, actorUserId, now)
                    : null;

                await UpsertSetupRowAsync(unit.Id, profileId, teamType, context, actorUserId, now);

                await ReconcileGoalsAsync(unit.Id, profileId, existingGoals, goals, actorUserId, now);

                await transaction.CommitAsync();

                // Licence coverage for the new team, only from its manager's licence
                // when they already have one. A free licence is never taken here; the
                // admin assigns one explicitly. Best-effort: the team is already saved.
                if (isNewTeam)
                {
                    try
                    {
                        await _licenseRepo.EnsureTeamCoverageAsync(unit.Id, actorUserId, allowFreeLicense: false);
                    }
                    catch
                    {
                        // Swallowed by design; the next manager change on the team retries it.
                    }
                }

                var record = await ReadRecordAsync(unit.Id);

                return new UpsertTeamSetupResultDto
                {
                    IsSuccess = true,
                    IsNewTeam = isNewTeam,
                    AssignedManagerUserId = assignedManagerUserId,
                    TeamSetup = record
                };
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<TeamSetupResultDto> GetTeamSetupByUnitIdAsync(long unitId)
        {
            // Upper bound as well as lower: the cast below is unchecked and would wrap an
            // oversized id onto a real team's id, which the tenant guard lets past because
            // it drops out-of-range values for the endpoint to answer for.
            if (unitId <= 0 || unitId > uint.MaxValue)
            {
                return new TeamSetupResultDto { IsSuccess = false, ErrorMessage = "UnitId is required." };
            }

            var record = await ReadRecordAsync((uint)unitId);

            if (record == null)
            {
                return new TeamSetupResultDto { IsSuccess = false, ErrorMessage = $"Team {unitId} was not found." };
            }

            return new TeamSetupResultDto { IsSuccess = true, TeamSetup = record };
        }

        // ── The unit ────────────────────────────────────────────────────────────

        private async Task<BusinessUnit> CreateUnitAsync(Profile company, string teamName, int? teamSize, int? parentId)
        {
            var businessUnitCodes = BussinessManager.GenerateCodes();

            // The placeholder contact every unit gets, exactly as upsert-team mints it —
            // except that the name is capped. BusinessUnits.ContactFirstName / ContactLastName
            // are varchar(60) while a team name may be 140, so a long name would fail the
            // insert with a bare 22001.
            var contactName = Truncate(teamName, DataConstants.PersonalNameLength);
            var contactEmailAddress = $"default{businessUnitCodes}@{company.DomainName}";

            // Group 1 is the seeded question set group every unit points at. Setup does not
            // choose it — TeamType drives the RRIDA team questions, which are a separate
            // bank; this column belongs to the legacy CLOVER chain.
            var dailyQuestionSetGroup = await _context.DailyQuestionSetGroups.FirstAsync(g => g.Id == 1);

            var unit = new BusinessUnit
            {
                BusinessUnitCode = businessUnitCodes.Code,
                BusinessUnitShortCode = businessUnitCodes.ShortCode,
                Name = teamName,
                // Composed the way upsert-team composes it, then capped: the codes add 48
                // characters and the column holds 140.
                NameInvariant = Truncate(teamName + businessUnitCodes, MaxTeamNameLength),
                RegistrationDate = DateTime.UtcNow,
                Profile = company,
                ContactFirstName = contactName,
                ContactLastName = contactName,
                ContactEmailAddress = contactEmailAddress,
                ParentId = parentId,
                TeamSize = teamSize,
                ActiveDailyQuestionSetGroupId = dailyQuestionSetGroup.Id,
                ActiveDailyQuestionSetGroup = dailyQuestionSetGroup
            };

            // The licence is reserved and assigned by the caller (UpsertTeamSetupAsync),
            // inside the same transaction, so the unit is never left without one.
            _context.BusinessUnits.Add(unit);

            return unit;
        }

        private BusinessUnit UpdateUnit(
            BusinessUnit unit,
            string teamName,
            int? teamSize,
            int? parentId,
            bool parentSpecified)
        {
            unit.Name = teamName;
            unit.NameInvariant = Truncate(teamName, MaxTeamNameLength);

            // Only when the caller actually said something about the parent. The setup
            // screen never asks for one, so an omitted field has to mean "unchanged" —
            // assigning null here would move the team to the root of the hierarchy on
            // every save.
            if (parentSpecified)
            {
                unit.ParentId = parentId;
            }

            // Optional on edit: a null leaves the stored team size untouched, as it does on
            // upsert-team.
            if (teamSize.HasValue)
            {
                unit.TeamSize = teamSize;
            }

            return unit;
        }

        /// <summary>
        /// Makes the person setting up a NEW team its manager, when they are a Team Manager
        /// of the same company. This is the "New Manager, New Team" journey: the manager was
        /// added without a team, and setup is where that team comes into existence. Without
        /// this row the team reads as unmanaged everywhere (assignable-teams, login's
        /// managedTeams).
        ///
        /// Anyone else — a Profile Admin without the role, a token without a user id — gets
        /// the team created exactly as before, with no manager. A brand-new unit has no
        /// incumbent, so nothing needs unassigning first.
        ///
        /// IsNewTeam stays false: post-password-landing reads it to choose the journey, and
        /// once setup has run the team exists and is set up.
        /// </summary>
        private async Task<int?> AssignSetupManagerAsync(uint unitId, uint profileId, int? actorUserId, DateTime now)
        {
            if (!actorUserId.HasValue)
            {
                return null;
            }

            var userId = actorUserId.Value;

            if (!await IsSetupTeamManagerAsync(userId, profileId))
            {
                return null;
            }

            _context.UserTeams.Add(new UserTeam
            {
                UserId = userId,
                UnitId = unitId,
                IsUnAssigned = false,
                IsReadOnly = false,
                CreatedBy = userId,
                CreatedDate = now
            });

            await _context.SaveChangesAsync();

            return userId;
        }

        /// <summary>
        /// True when the user is a live Team Manager of the profile. UserRoles is the source
        /// of truth; the derived Users.UserGroupId answers only for a user created before
        /// UserRoles was backfilled (no rows at all).
        /// </summary>
        private Task<bool> IsSetupTeamManagerAsync(int userId, uint profileId)
        {
            var pid = (int)profileId;
            var teamManager = (int)UserGroups.TeamManager;

            return _context.Users
                .AnyAsync(u => u.UserId == userId
                               && u.ProfileId == pid
                               && !u.IsDel
                               && (u.Roles.Any(r => r.UserGroupId == teamManager)
                                   || (!u.Roles.Any() && u.UserGroupId == teamManager)));
        }

        /// <summary>
        /// Another live team of the same company already carries this name
        /// (case-insensitive). <paramref name="excludeUnitId" /> is the team being edited.
        /// </summary>
        private Task<bool> IsTeamNameTakenAsync(uint profileId, string teamName, uint? excludeUnitId)
        {
            var normalizedName = teamName.ToLowerInvariant();

            // excludeUnitId is a plain parameter, not existingUnit.Id inside the predicate:
            // on the create path there is no unit, and EF would fail building the parameter.
            return _context.BusinessUnits
                .AnyAsync(u => u.Profile.Id == profileId
                               && u.IsDel != true
                               && u.Name.ToLower() == normalizedName
                               && (excludeUnitId == null || u.Id != excludeUnitId.Value));
        }

        /// <summary>
        /// Keeps a value inside its column. NameInvariant is composed from the team name
        /// plus the generated codes, which is 48 characters on its own — a long team name
        /// would otherwise overflow varchar(140) and fail the insert with a bare 22001.
        /// </summary>
        private static string Truncate(string value, int maxLength) =>
            value.Length <= maxLength ? value : value[..maxLength];

        // ── The setup row ───────────────────────────────────────────────────────

        private async Task UpsertSetupRowAsync(
            uint unitId,
            uint profileId,
            TeamType teamType,
            string context,
            int? actorUserId,
            DateTime now)
        {
            var setup = await _context.TeamSetups.FirstOrDefaultAsync(s => s.UnitId == unitId && !s.IsDel);

            if (setup == null)
            {
                _context.TeamSetups.Add(new TeamSetup
                {
                    ProfileId = profileId,
                    UnitId = unitId,
                    TeamType = teamType,
                    Context = context,
                    CreatedBy = actorUserId,
                    UpdatedBy = actorUserId,
                    CreatedAt = now,
                    UpdatedAt = now,
                    IsDel = false
                });
            }
            else
            {
                setup.ProfileId = profileId;
                setup.TeamType = teamType;
                setup.Context = context;
                setup.UpdatedBy = actorUserId;
                setup.UpdatedAt = now;
            }

            await _context.SaveChangesAsync();
        }

        // ── The goals ───────────────────────────────────────────────────────────

        /// <summary>
        /// Brings the stored goals in line with the ones sent, and writes one audit row
        /// per change.
        ///
        /// Order matters here. Removals go first so the positions they held are free;
        /// kept goals whose position moved are then parked on temporary negative
        /// positions before their real ones are written, because
        /// UX_TeamGoals_Unit_Order_Active is not deferrable and a straight swap of two
        /// positions would collide halfway through. New goals go in last, into slots
        /// nothing is holding any more.
        /// </summary>
        private async Task ReconcileGoalsAsync(
            uint unitId,
            uint profileId,
            List<TeamGoal> existing,
            List<NormalizedGoal> goals,
            int? actorUserId,
            DateTime now)
        {
            // Every id in `goals` was checked against this set before the transaction
            // opened, so the lookups below cannot miss.
            var byId = existing.ToDictionary(g => g.Id);

            var keptIds = goals.Where(g => g.TeamGoalId > 0)
                               .Select(g => (uint)g.TeamGoalId)
                               .ToHashSet();

            // 1. Removals — a goal stored before and absent from the request.
            foreach (var removed in existing.Where(g => !keptIds.Contains(g.Id)))
            {
                removed.IsDel = true;
                removed.UpdatedBy = actorUserId;
                removed.UpdatedAt = now;

                await AddGoalAuditAsync(removed, "DELETE", "Removed from the team's goals.", now);
            }

            await _context.SaveChangesAsync();

            // 2. Park moved goals out of the way of the unique index. The position each one
            //    held is recorded first — once parked it is gone, and the audit line says
            //    where the goal moved from.
            var moved = goals
                .Where(g => g.TeamGoalId > 0 && byId[(uint)g.TeamGoalId].DisplayOrder != g.DisplayOrder)
                .ToList();

            if (moved.Count > 0)
            {
                short parking = -1;

                foreach (var goal in moved)
                {
                    var stored = byId[(uint)goal.TeamGoalId];

                    goal.PreviousDisplayOrder = stored.DisplayOrder;
                    stored.DisplayOrder = parking--;
                }

                await _context.SaveChangesAsync();
            }

            var movedIds = moved.Select(g => g.TeamGoalId).ToHashSet();

            // 3. Updates to the goals being kept.
            foreach (var goal in goals.Where(g => g.TeamGoalId > 0))
            {
                var stored = byId[(uint)goal.TeamGoalId];

                var textChanged = stored.GoalText != goal.GoalText;
                var targetChanged = stored.TargetDate != goal.TargetDate;
                var activeChanged = stored.IsActive != goal.IsActive;
                var orderChanged = movedIds.Contains(goal.TeamGoalId);

                if (!textChanged && !targetChanged && !activeChanged && !orderChanged)
                {
                    continue;
                }

                var previousOrder = orderChanged ? goal.PreviousDisplayOrder : goal.DisplayOrder;

                stored.GoalText = goal.GoalText;
                stored.DisplayOrder = goal.DisplayOrder;
                stored.TargetDate = goal.TargetDate;
                stored.IsActive = goal.IsActive;
                stored.UpdatedBy = actorUserId;
                stored.UpdatedAt = now;

                // Only the position moved: that is a reorder, and saying so keeps a drag of
                // the goal list from reading as an edit of its wording.
                var operation = orderChanged && !textChanged && !targetChanged && !activeChanged
                    ? "REORDER"
                    : "UPDATE";

                await AddGoalAuditAsync(
                    stored,
                    operation,
                    DescribeChange(textChanged, targetChanged, activeChanged, orderChanged, previousOrder, goal.DisplayOrder),
                    now);
            }

            await _context.SaveChangesAsync();

            // 4. Additions.
            var added = new List<TeamGoal>();

            foreach (var goal in goals.Where(g => g.TeamGoalId == 0))
            {
                var created = new TeamGoal
                {
                    ProfileId = profileId,
                    UnitId = unitId,
                    GoalText = goal.GoalText,
                    DisplayOrder = goal.DisplayOrder,
                    TargetDate = goal.TargetDate,
                    IsActive = goal.IsActive,
                    CreatedBy = actorUserId,
                    UpdatedBy = actorUserId,
                    CreatedAt = now,
                    UpdatedAt = now,
                    IsDel = false
                };

                _context.TeamGoals.Add(created);
                added.Add(created);
            }

            // Saved before the audit rows are built: the snapshot carries the goal id, and
            // the identity value only exists after the insert.
            await _context.SaveChangesAsync();

            foreach (var created in added)
            {
                await AddGoalAuditAsync(created, "INSERT", "Added to the team's goals.", now);
            }

            await _context.SaveChangesAsync();
        }

        /// <summary>
        /// One row-snapshot audit entry. VersionNumber counts from what the goal already
        /// has, so the history of a single goal reads 1, 2, 3 regardless of how many other
        /// goals the team has.
        /// </summary>
        private async Task AddGoalAuditAsync(TeamGoal goal, string operationType, string changeSummary, DateTime now)
        {
            var lastVersion = await _context.TeamGoalAudits
                .Where(a => a.TeamGoalId == goal.Id)
                .Select(a => (int?)a.VersionNumber)
                .MaxAsync();

            _context.TeamGoalAudits.Add(new TeamGoalAudit
            {
                TeamGoalId = goal.Id,
                ProfileId = goal.ProfileId,
                UnitId = goal.UnitId,
                GoalText = goal.GoalText,
                DisplayOrder = goal.DisplayOrder,
                TargetDate = goal.TargetDate,
                IsActive = goal.IsActive,
                ChangeSummary = changeSummary,
                CreatedBy = goal.CreatedBy,
                UpdatedBy = goal.UpdatedBy,
                CreatedAt = goal.CreatedAt,
                UpdatedAt = goal.UpdatedAt,
                IsDel = goal.IsDel,
                LoggedAt = now,
                VersionNumber = (lastVersion ?? 0) + 1,
                OperationType = operationType
            });
        }

        private static string DescribeChange(
            bool textChanged,
            bool targetChanged,
            bool activeChanged,
            bool orderChanged,
            short previousOrder,
            short newOrder)
        {
            var parts = new List<string>();

            if (textChanged) parts.Add("wording changed");
            if (targetChanged) parts.Add("target date changed");
            if (activeChanged) parts.Add("active flag changed");
            if (orderChanged) parts.Add($"moved from position {previousOrder} to {newOrder}");

            return parts.Count == 0 ? "No change." : char.ToUpperInvariant(parts[0][0]) + string.Join(", ", parts)[1..] + ".";
        }

        // ── Reading ─────────────────────────────────────────────────────────────

        private async Task<TeamSetupRecordDto?> ReadRecordAsync(uint unitId)
        {
            var unit = await _context.BusinessUnits
                .AsNoTracking()
                .Include(u => u.Profile)
                .FirstOrDefaultAsync(u => u.Id == unitId && u.IsDel != true);

            if (unit == null)
            {
                return null;
            }

            var setup = await _context.TeamSetups
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.UnitId == unitId && !s.IsDel);

            var goals = await _context.TeamGoals
                .AsNoTracking()
                .Where(g => g.UnitId == unitId && !g.IsDel)
                .OrderBy(g => g.DisplayOrder)
                .ToListAsync();

            return new TeamSetupRecordDto
            {
                UnitId = unit.Id,
                ProfileId = unit.Profile?.Id ?? 0,
                TeamName = unit.Name,
                TeamSize = unit.TeamSize,
                ParentId = unit.ParentId,
                TeamCode = unit.BusinessUnitShortCode,

                TeamSetupId = setup?.Id,
                TeamType = setup == null ? null : setup.TeamType.ToString(),
                Context = setup?.Context,
                // A row with no context was created by an attribute edit, not the wizard.
                IsSetupComplete = setup != null && !string.IsNullOrWhiteSpace(setup.Context),
                CreatedBy = setup?.CreatedBy,
                UpdatedBy = setup?.UpdatedBy,
                CreatedAt = setup?.CreatedAt,
                UpdatedAt = setup?.UpdatedAt,

                Goals = goals.Select(g => new TeamGoalRecordDto
                {
                    TeamGoalId = g.Id,
                    UnitId = g.UnitId,
                    ProfileId = g.ProfileId,
                    GoalText = g.GoalText,
                    DisplayOrder = g.DisplayOrder,
                    TargetDate = g.TargetDate,
                    IsActive = g.IsActive,
                    CreatedBy = g.CreatedBy,
                    UpdatedBy = g.UpdatedBy,
                    CreatedAt = g.CreatedAt,
                    UpdatedAt = g.UpdatedAt
                }).ToList()
            };
        }

        // ── Request normalisation ───────────────────────────────────────────────

        /// <summary>A goal from the request, trimmed and with its position settled.</summary>
        private sealed class NormalizedGoal
        {
            public long TeamGoalId { get; init; }
            public string GoalText { get; init; } = string.Empty;
            public short DisplayOrder { get; set; }
            public short PreviousDisplayOrder { get; set; }
            public DateOnly? TargetDate { get; init; }
            public bool IsActive { get; init; }
        }

        /// <summary>
        /// Validates the goal list and settles each goal's position. Returns the reason the
        /// list is unusable, or null when <paramref name="normalized" /> is good to store.
        /// </summary>
        private static string? NormalizeGoals(List<TeamGoalInputDto>? input, out List<NormalizedGoal> normalized)
        {
            normalized = new List<NormalizedGoal>();

            var supplied = (input ?? new List<TeamGoalInputDto>())
                .Where(g => !string.IsNullOrWhiteSpace(g.GoalText))
                .ToList();

            if (supplied.Count < MinGoals)
            {
                return $"At least {MinGoals} goals are required.";
            }

            if (supplied.Count > MaxGoals)
            {
                return $"No more than {MaxGoals} goals can be stored for a team.";
            }

            var position = (short)1;

            foreach (var goal in supplied)
            {
                var text = goal.GoalText.Trim();

                if (text.Length > MaxGoalTextLength)
                {
                    return $"A goal cannot be longer than {MaxGoalTextLength} characters.";
                }

                // Range-checked for the same reason the unit and company ids are: the value
                // is cast to uint before it is looked up, and an unchecked cast wraps.
                if (goal.TeamGoalId < 0 || goal.TeamGoalId > uint.MaxValue)
                {
                    return "TeamGoalId is not valid.";
                }

                normalized.Add(new NormalizedGoal
                {
                    TeamGoalId = goal.TeamGoalId,
                    GoalText = text,
                    // A caller that sends explicit positions gets them; one that just sends
                    // the goals in order gets the order it sent.
                    DisplayOrder = goal.DisplayOrder > 0 ? goal.DisplayOrder : position,
                    TargetDate = goal.TargetDate,
                    IsActive = goal.IsActive
                });

                position++;
            }

            // The same goal twice would leave the caller guessing which edit won.
            var duplicateId = normalized.Where(g => g.TeamGoalId > 0)
                                        .GroupBy(g => g.TeamGoalId)
                                        .FirstOrDefault(g => g.Count() > 1);

            if (duplicateId != null)
            {
                return $"Goal {duplicateId.Key} appears more than once in the request.";
            }

            // Two active goals cannot share a position — UX_TeamGoals_Unit_Order_Active
            // would refuse them, and a constraint violation is a worse answer than this.
            var duplicateOrder = normalized.Where(g => g.IsActive)
                                           .GroupBy(g => g.DisplayOrder)
                                           .FirstOrDefault(g => g.Count() > 1);

            if (duplicateOrder != null)
            {
                return $"Two goals cannot share display order {duplicateOrder.Key}.";
            }

            return null;
        }

        /// <summary>
        /// Accepts the enum name or its numeric value, so a client holding the raw value
        /// still works. Anything else, including an empty value, is a failure — the team
        /// type is what decides which questions the team is asked.
        /// </summary>
        private static bool TryParseTeamType(string? value, out TeamType teamType)
        {
            teamType = default;

            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return Enum.TryParse(value.Trim(), ignoreCase: true, out teamType)
                   && Enum.IsDefined(typeof(TeamType), teamType);
        }

        private static UpsertTeamSetupResultDto Failed(string message) =>
            new() { IsSuccess = false, ErrorMessage = message };
    }
}
