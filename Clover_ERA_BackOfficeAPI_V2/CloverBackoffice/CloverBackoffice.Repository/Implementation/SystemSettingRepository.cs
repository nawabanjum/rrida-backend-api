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
    public class SystemSettingRepository : ISystemSettingRepository
    {
        private readonly ApplicationDbContext _context;

        public SystemSettingRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        private readonly Dictionary<string, List<int>> _userAssignments = new();

        public async Task<List<PromptTypes>> GetPromptTypes()
        {

            return await _context.PromptTypes.ToListAsync();
        }

        public async Task<List<UserGroup>> GetUserGroups()
        {
            return await _context.UserGroups
                .Where(g => g.Id != Components.Enums.RoleHierarchy.NoRoleGroupId)
                .ToListAsync();
        }

        public async Task<List<SystemModule>> GetSystemModules()
        {
            return await _context.SystemModules.ToListAsync();
        }

        public async Task<bool> SaveAssignUserRight(GroupConfigurationDto dto)
        {
            if (dto == null) return false;
            var oldRights = _context.AssignUserRights
                                    .Where(r => r.UserGroupId == dto.UserGroupId);

            if (oldRights.Any())
            {
                _context.AssignUserRights.RemoveRange(oldRights);
                await _context.SaveChangesAsync();
            }
            if (dto.PromptTypes != null && dto.PromptTypes.Count > 0)
            {
                var newRights = dto.PromptTypes.Select(pt => new AssignUserRight
                {
                    UserGroupId = dto.UserGroupId,
                    SystemModuleId = dto.ModuleID,
                    PromptTypeId = pt.PromptTypeId
                }).ToList();
                await _context.AssignUserRights.AddRangeAsync(newRights);
                await _context.SaveChangesAsync();
            }

            return true;
        }
    }
}
