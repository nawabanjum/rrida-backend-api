using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Infrastructure.Models.TeamSetups;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace CloverBackoffice.Repository.Implementation
{
    /// <summary>
    /// The dashboard's two edit modals, each saving ONE section of an existing team:
    ///
    ///   Edit attributes  name, size, team type   — context and goals untouched
    ///   Edit team goals  the goal list           — unit, type and context untouched
    ///
    /// upsert-team-setup rewrites everything and requires context and goals on every
    /// call, so a modal that only knows its own fields could not use it without
    /// overwriting (or failing on) the other section. Validation and the goal
    /// reconciliation are the same code the wizard uses.
    /// </summary>
    public partial class TeamSetupRepository
    {
        // First key of pg_advisory_xact_lock(int, int) for goal edits; the second is the
        // unit id. Two people saving the same team's goals at once would otherwise race
        // on UX_TeamGoals_Unit_Order_Active and one of them would get a 500.
        private const int GoalEditLockNamespace = 16019;

        // First key of pg_advisory_xact_lock(int, int) for attribute edits; the second is
        // the unit id. Two first saves of a team with no setup row would otherwise both
        // insert one and the second would hit UX_TeamSetups_Unit as a 500.
        private const int AttributeEditLockNamespace = 16026;

        public async Task<UpsertTeamSetupResultDto> UpdateTeamAttributesAsync(UpdateTeamAttributesDto dto, int? actorUserId)
        {
            var teamName = (dto.TeamName ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(teamName))
            {
                return Failed("TeamName is required.");
            }

            if (teamName.Length > MaxTeamNameLength)
            {
                return Failed($"TeamName cannot be longer than {MaxTeamNameLength} characters.");
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

            var (unit, unitError) = await LoadEditableUnitAsync(dto.ProfileId, dto.UnitId);

            if (unitError != null)
            {
                return Failed(unitError);
            }

            var profileId = (uint)dto.ProfileId;

            if (await IsTeamNameTakenAsync(profileId, teamName, unit!.Id))
            {
                return Failed("Bussiness name must be unique.");
            }

            var now = DateTime.UtcNow;

            await using var transaction = await _context.Database.BeginTransactionAsync();

            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({AttributeEditLockNamespace}, {(int)unit.Id})");

            // Team type lives on the setup row. Teams created before the setup wizard
            // existed have none, so the first attribute save creates it with an empty
            // context; the wizard still asks for the context (20+ characters), and the
            // team reads as not set up until it has one.
            var setup = await _context.TeamSetups.FirstOrDefaultAsync(s => s.UnitId == unit.Id && !s.IsDel);

            if (setup == null)
            {
                _context.TeamSetups.Add(new TeamSetup
                {
                    ProfileId = unit.ProfileId,
                    UnitId = unit.Id,
                    TeamType = teamType,
                    Context = string.Empty,
                    CreatedBy = actorUserId,
                    UpdatedBy = actorUserId,
                    CreatedAt = now,
                    UpdatedAt = now,
                    IsDel = false
                });
            }
            else
            {
                setup.TeamType = teamType;
                setup.UpdatedBy = actorUserId;
                setup.UpdatedAt = now;
            }

            // Parent never changes here: the modal has no parent field.
            UpdateUnit(unit, teamName, dto.TeamSize, parentId: null, parentSpecified: false);

            // Unit and setup row in one SaveChanges, so both land or neither does.
            await _context.SaveChangesAsync();

            await transaction.CommitAsync();

            return Updated(await ReadRecordAsync(unit.Id));
        }

        public async Task<UpsertTeamSetupResultDto> UpdateTeamGoalsAsync(UpdateTeamGoalsDto dto, int? actorUserId)
        {
            var goalError = NormalizeGoals(dto.Goals, out var goals);

            if (goalError != null)
            {
                return Failed(goalError);
            }

            var (unit, unitError) = await LoadEditableUnitAsync(dto.ProfileId, dto.UnitId);

            if (unitError != null)
            {
                return Failed(unitError);
            }

            await using var transaction = await _context.Database.BeginTransactionAsync();

            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({GoalEditLockNamespace}, {(int)unit!.Id})");

            // Read under the lock, so the list compared against is the one being replaced.
            var existingGoals = await _context.TeamGoals
                .Where(g => g.UnitId == unit.Id && !g.IsDel)
                .ToListAsync();

            var storedGoalIds = existingGoals.Select(g => g.Id).ToHashSet();

            var foreign = goals.FirstOrDefault(g => g.TeamGoalId > 0 && !storedGoalIds.Contains((uint)g.TeamGoalId));

            if (foreign != null)
            {
                return Failed($"Goal {foreign.TeamGoalId} does not belong to team {dto.UnitId}.");
            }

            await ReconcileGoalsAsync(unit.Id, (uint)dto.ProfileId, existingGoals, goals, actorUserId, DateTime.UtcNow);

            await transaction.CommitAsync();

            return Updated(await ReadRecordAsync(unit.Id));
        }

        /// <summary>
        /// The live team <paramref name="unitId" /> of company <paramref name="profileId" />,
        /// tracked for update, or the reason it cannot be edited. Ids are range-checked
        /// before the uint casts, which would otherwise wrap onto somebody else's team.
        /// </summary>
        private async Task<(BusinessUnit? Unit, string? Error)> LoadEditableUnitAsync(long profileId, long unitId)
        {
            if (profileId <= 0 || profileId > uint.MaxValue)
            {
                return (null, "ProfileId is required.");
            }

            if (unitId <= 0 || unitId > uint.MaxValue)
            {
                return (null, "UnitId is required.");
            }

            var unit = await _context.BusinessUnits
                .Include(u => u.Profile)
                .FirstOrDefaultAsync(u => u.Id == (uint)unitId && u.IsDel != true);

            if (unit == null)
            {
                return (null, $"Team {unitId} was not found.");
            }

            if (unit.Profile == null || unit.Profile.Id != (uint)profileId)
            {
                return (null, $"Team {unitId} does not belong to company {profileId}.");
            }

            return (unit, null);
        }

        private static UpsertTeamSetupResultDto Updated(TeamSetupRecordDto? record) =>
            new() { IsSuccess = true, IsNewTeam = false, TeamSetup = record };
    }
}
