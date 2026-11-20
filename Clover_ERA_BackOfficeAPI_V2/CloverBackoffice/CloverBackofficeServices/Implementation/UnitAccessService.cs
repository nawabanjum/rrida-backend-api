using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CloverBackoffice.Components.Enums;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Services.Implementation
{
    /// <summary>
    /// Role-based unit-access resolution. Lifted from AggregateDashboardService.GetAccessibleUnitIds
    /// so the dashboard and EranCoach share one implementation; the logic is unchanged.
    /// </summary>
    public class UnitAccessService : IUnitAccessService
    {
        private readonly ApplicationDbContext _context;

        public UnitAccessService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<List<long>> GetAccessibleUnitIdsAsync(User user, CancellationToken ct = default)
        {
            switch (user.UserGroupId)
            {
                // Platform admin: every unit in the system, no profile filter.
                case (int)UserGroups.RridaAdmin:
                    return await _context.BusinessUnits
                        .Where(b => b.IsDel != true)
                        .Select(x => (long)x.Id)
                        .Distinct()
                        .ToListAsync(ct);

                // No permission spec yet, so it reaches nothing rather than
                // silently inheriting a manager's units.
                case (int)UserGroups.PortfolioManager:
                    return new List<long>();

                case (int)UserGroups.ProfileAdmin:
                    return await _context.BusinessUnits
                        .Where(b => b.Profile.Id == user.ProfileId && b.IsDel != true)
                        .Select(b => (long)b.Id)
                        .ToListAsync(ct);

                case (int)UserGroups.TeamManager:
                    {
                        // Directly assigned root units.
                        var rootUnitIds = await _context.UserTeams
                            .Where(ut => ut.UserId == user.UserId && ut.IsUnAssigned == false)
                            .Select(ut => (long)ut.UnitId)
                            .Distinct()
                            .ToListAsync(ct);

                        if (!rootUnitIds.Any()) return new List<long>();

                        // All units, to walk the parent/child hierarchy in memory.
                        var allUnits = await _context.BusinessUnits
                            .Where(bu => bu.IsDel != true)
                            .Select(bu => new { Id = (long)bu.Id, ParentId = bu.ParentId })
                            .ToListAsync(ct);

                        // BFS from the assigned roots to collect roots + all descendants.
                        var result = new HashSet<long>(rootUnitIds);
                        var queue = new Queue<long>(rootUnitIds);

                        while (queue.Count > 0)
                        {
                            var parentId = queue.Dequeue();
                            var children = allUnits
                                .Where(u => u.ParentId == (int?)parentId)
                                .Select(u => u.Id);

                            foreach (var child in children)
                            {
                                if (result.Add(child))
                                    queue.Enqueue(child);
                            }
                        }

                        return result.ToList();
                    }

                default:
                    throw new UnauthorizedAccessException();
            }
        }
    }
}
