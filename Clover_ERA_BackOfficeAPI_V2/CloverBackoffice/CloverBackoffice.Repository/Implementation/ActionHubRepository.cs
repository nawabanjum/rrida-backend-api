using CloverBackoffice.Components.Enums;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Repository.Implementation
{
    public class ActionHubRepository : IActionHubRepository
    {
        private readonly ApplicationDbContext _context;

        public ActionHubRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<ActionEvent?> GetByIdAsync(int id)
        {
            return await _context.ActionEvents.FindAsync(id);
        }

        public async Task<ActionEvent> UpsertAsync(ActionEvent actionEvent)
        {
            if (actionEvent.Id == 0)
            {
                await _context.ActionEvents.AddAsync(actionEvent);
            }
            else
            {
                _context.ActionEvents.Update(actionEvent);
            }

            await _context.SaveChangesAsync();
            return actionEvent;
        }

        public async Task<ActionEvent> UpdateAsync(ActionEvent actionEvent)
        {
            _context.ActionEvents.Update(actionEvent);
            await _context.SaveChangesAsync();
            return actionEvent;
        }
        public async Task<List<ActionHub>> GetActionEventListAsync()
        {
            return await _context.ActionHubs.ToListAsync();
        }
        public async Task<List<ActionHub>> GetActionHubWithEventsAsync(int id, string type)
        {
            var query = _context.ActionHubs
                         .Include(x => x.ActionEvents)
                         .Where(x => !x.IsDel && x.UnitId == id);

            if (!string.IsNullOrEmpty(type))
            {
                query = query.Where(x => x.Type.ToLower() == type.ToLower());
            }

            return await query.ToListAsync();
        }
        public async Task<List<ActionHubSummaryRowDto>> GetActionHubSummaryRowsAsync(int unitId, string? type)
        {
            var query = _context.ActionHubs
                         .Where(x => !x.IsDel && x.UnitId == unitId);

            if (!string.IsNullOrEmpty(type))
            {
                query = query.Where(x => x.Type.ToLower() == type.ToLower());
            }

            return await query
                .Select(x => new ActionHubSummaryRowDto
                {
                    Id = x.Id,
                    ActionTitle = x.ActionTitle,
                    Type = x.Type,
                    CreatedAt = x.CreatedAt,
                    UpdatedAt = x.UpdatedAt
                })
                .ToListAsync();
        }

        public async Task<string?> GetWorksheetFileByReportId(long reportId)
        {
            var report = await _context.LLMReports
                .Where(x => x.Id == reportId)
                .Select(x => x.WorksheetFile)
                .FirstOrDefaultAsync();

            return report;
        }
        public async Task<TopAction?> GetTopActionMatchAsync(long unitId, string actionTitle)
        {
            return await _context.TopActions
                .Where(x => x.UnitId == unitId && x.Action.ToLower() == actionTitle.ToLower())
                .FirstOrDefaultAsync();
        }


        public async Task<ActionHub?> GetByIdActionHubAsync(int id)
        {
            return await _context.ActionHubs.FindAsync(id);
        }
        public async Task<Profile> CompanyExistsAsync(uint profileId)
        {
            return await _context.Profiles.FirstOrDefaultAsync(c => c.Id == profileId);
        }

        // Implementation
        public async Task<ActionHub?> GetActionHubByIdAsync(int id)
        {
            return await _context.ActionHubs.FirstOrDefaultAsync(x => x.Id == id);
        }

        public async Task<BusinessUnit> UnitExistsAsync(uint unitId)
        {
            return await _context.BusinessUnits
        .FirstOrDefaultAsync(u => u.Id == unitId && (u.IsDel == false || u.IsDel == null));
        }
        public async Task<ActionHub> UpsertActionHubAsync(ActionHub actionHub)
        {
            if (actionHub.Id == 0)
            {
                await _context.ActionHubs.AddAsync(actionHub);
            }
            else
            {
                _context.ActionHubs.Update(actionHub);
            }

            await _context.SaveChangesAsync();
            return actionHub;
        }

        public async Task<ActionHub> UpdateActionHubAsync(ActionHub actionHub)
        {
            _context.ActionHubs.Update(actionHub);
            await _context.SaveChangesAsync();
            return actionHub;
        }
        public async Task<List<ActionEvent>> GetActionEventsByHubIdAsync(int actionHubId)
        {
            return await _context.ActionEvents
                .Where(e => e.ActionHubId == actionHubId && !e.IsDel)
                .ToListAsync();
        }

        public async Task UpdateActionEventsAsync(IEnumerable<ActionEvent> events)
        {
            _context.ActionEvents.UpdateRange(events);
            await _context.SaveChangesAsync();
        }


    }
}
