using CloverBackoffice.Components;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Repository.Implementation
{
    public class CompanyTeamRepository : ICompanyTeamRepository
    {
        private readonly ApplicationDbContext _context;

        public CompanyTeamRepository(ApplicationDbContext context)
        {
            _context = context;
        }
        public async Task<BusinessUnit> GetByIdAsync(int id)
        {
            return await _context.BusinessUnits
                .FirstOrDefaultAsync(x => x.Id == id);
        }

        public async Task AddAsync(BusinessUnit unit)
        {
            await _context.BusinessUnits.AddAsync(unit);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(BusinessUnit unit)
        {
            _context.BusinessUnits.Update(unit);
            await _context.SaveChangesAsync();
        }

        public async Task<bool> DeleteAsync(uint id)
        {
            try
            {
                var existing = await _context.BusinessUnits
                    .Include(b => b.Profile)
                    .FirstOrDefaultAsync(a => a.Id == id);

                if (existing == null || existing.IsDel == true) return false;

                await SoftDeleteUnit(existing);
                await SoftDeleteChildren(id);

                await _context.SaveChangesAsync();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private async Task SoftDeleteUnit(BusinessUnit existing)
        {
            _context.Entry(existing).State = EntityState.Detached;
            var datePrefix = DateTime.UtcNow.ToString("ddMMyyyy");
            var dailyQuestionSetGroup = await _context.DailyQuestionSetGroups.FirstOrDefaultAsync(g => g.Id == 1);

            var unit = new BusinessUnit
            {
                Id = existing.Id,
                BusinessUnitCode = existing.BusinessUnitCode,
                BusinessUnitShortCode = existing.BusinessUnitShortCode,
                Name = $"del_{datePrefix}_{existing.Name}",
                NameInvariant = $"del_{datePrefix}_{existing.NameInvariant}",
                RegistrationDate = DateTime.SpecifyKind(existing.RegistrationDate, DateTimeKind.Utc),
                Profile = existing.Profile,
                ContactFirstName = existing.ContactFirstName,
                ContactLastName = existing.ContactLastName,
                ContactEmailAddress = SoftDeletedEmail(existing.ContactEmailAddress),
                ParentId = existing.ParentId,
                IsDel = true,
                ActiveDailyQuestionSetGroupId = dailyQuestionSetGroup.Id,
                ActiveDailyQuestionSetGroup = dailyQuestionSetGroup
            };

            _context.BusinessUnits.Update(unit);
        }

        private async Task SoftDeleteChildren(uint parentId)
        {
            var children = await _context.BusinessUnits
                .Include(b => b.Profile)
                .Where(b => b.ParentId == parentId && b.IsDel != true)
                .ToListAsync();

            foreach (var child in children)
            {
                await SoftDeleteUnit(child);
                await SoftDeleteChildren(child.Id);
            }
        }

        /// <summary>
        /// Prefixes a soft-deleted unit's contact address the same way the unit's name
        /// is prefixed. This used to update a shared Contacts row, which renamed the
        /// address on every other unit pointing at it; the address now lives on the
        /// unit, so only this unit is affected.
        /// </summary>
        private static string? SoftDeletedEmail(string? emailAddress)
        {
            if (string.IsNullOrWhiteSpace(emailAddress) || emailAddress.StartsWith("del_"))
                return emailAddress;

            var datePrefix = DateTime.UtcNow.ToString("ddMMyyyy");
            return $"del_{datePrefix}_{emailAddress}";
        }

        public async Task<bool> UserExistsAsync(int userId)
        {
            return await _context.Users.AnyAsync(u => u.UserId == userId);
        }

        public async Task<bool> UnitExistsAsync(int UnitId)
        {
            return await _context.BusinessUnits.AnyAsync(t => t.Id == UnitId);
        }

        public async Task AddUserTeamAsync(UserTeam userTeam)
        {
            _context.UserTeams.Add(userTeam);
            // Don't save here - let the service handle transaction
        }
        
        public async Task AddUserTeamAndSaveAsync(UserTeam userTeam)
        {
            _context.UserTeams.Add(userTeam);
            await _context.SaveChangesAsync();
        }
        public async Task<bool> IsUserAlreadyAssigned(int userId, int teamId)
        {
            return await _context.UserTeams
                .AnyAsync(ut => ut.UserId == userId && ut.UnitId == teamId);
        }
        public async Task<UserTeam?> GetUserActiveAssignmentAsync(int userId, int unitId)
        {
            return await _context.UserTeams
                .FirstOrDefaultAsync(ut => ut.UserId == userId && ut.UnitId == unitId && ut.IsUnAssigned == false);
        }
        public async Task<UserTeam?> GetUnitActiveAssignmentAsync(int unitId)
        {
            return await _context.UserTeams
                .FirstOrDefaultAsync(ut => ut.UnitId == unitId && ut.IsUnAssigned == false);
        }
        public async Task<UserTeam?> GetUserCurrentAssignmentAsync(int userId)
        {
            return await _context.UserTeams
                .FirstOrDefaultAsync(ut => ut.UserId == userId && ut.IsUnAssigned == false);
        }
        public async Task UpdateUserTeamAsync(UserTeam userTeam)
        {
            _context.UserTeams.Update(userTeam);
            // Don't save here - let the service handle transaction
        }
        
        public async Task UpdateUserTeamAndSaveAsync(UserTeam userTeam)
        {
            _context.UserTeams.Update(userTeam);
            await _context.SaveChangesAsync();
        }

        // CompanyTeams is the one table left out of the CompanyId -> ProfileId
        // rename: it is dead (0 rows, nothing writes it), so it keeps its original
        // column and the entity keeps CompanyId to match. The parameter is named
        // profileId because that is what the caller has; only the column differs.
        public async Task<List<CompanyTeam>> GetTeamsByProfileIdAsync(int profileId)
        {
            return await _context.CompanyTeams
                .Where(t => t.CompanyId == profileId && !t.IsDeleted)
                .ToListAsync();
        }
        public async Task<UserTeam?> GetUserTeamAsync(long userId, int unitId)
        {
            return await _context.UserTeams
                .FirstOrDefaultAsync(x => x.UserId == userId && x.UnitId == unitId);
        }

        public async Task<TeamNotificationDto> GetTeamNotificationsAsync(int profileId, List<int>? filteredUnitIds = null)
        {
            var query = _context.BusinessUnits
                .Where(bu => bu.Profile.Id == profileId && bu.IsDel != true);

            if (filteredUnitIds != null && filteredUnitIds.Any())
                query = query.Where(bu => filteredUnitIds.Contains((int)bu.Id));

            var allUnitIds = await query
                .Select(bu => bu.Id)
                .ToListAsync();

            if (!allUnitIds.Any())
                return new TeamNotificationDto();

            // Units that have an active manager assignment
            var unitsWithManager = await _context.UserTeams
                .Where(ut => allUnitIds.Contains(ut.UnitId) && !ut.IsUnAssigned)
                .Select(ut => ut.UnitId)
                .Distinct()
                .ToListAsync();

            var noManagerCount = allUnitIds.Count(id => !unitsWithManager.Contains(id));

            // Check ALL teams for no feedback in last 48 hours
            var cutoff48h = DateTime.UtcNow.AddHours(-48);

            var unitsWithRecentFeedback = await _context.DailyFeedbacks
                .Where(df => allUnitIds.Contains(df.BusinessUnitId)
                    && df.SubmissionDateTime >= cutoff48h)
                .Select(df => df.BusinessUnitId)
                .Distinct()
                .ToListAsync();

            var noResponseIn48hCount = allUnitIds.Count(id => !unitsWithRecentFeedback.Contains(id));

            var teamWord1 = noManagerCount == 1 ? "team has" : "teams have";
            var teamWord2 = noResponseIn48hCount == 1 ? "team has" : "teams have";

            return new TeamNotificationDto
            {
                NoManagerCount = noManagerCount,
                NoManagerMessage = noManagerCount > 0
                    ? $"{noManagerCount} {teamWord1} no assigned manager. Teams without managers can't receive response analysis or trend tracking."
                    : null,
                NoResponseIn48hCount = noResponseIn48hCount,
                NoResponseIn48hMessage = noResponseIn48hCount > 0
                    ? $"{noResponseIn48hCount} {teamWord2} had no responses for 48+ hours. Trend data is going stale."
                    : null
            };
        }
    }
}
