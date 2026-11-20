using CloverBackoffice.Infrastructure.BusinessModel;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static CloverBackoffice.Infrastructure.BusinessModel.PermissionMatrix;

namespace CloverBackoffice.Repository.Implementation
{
    public class PermissionRepository : IPermissionRepository
    {
        private readonly ApplicationDbContext _context;

        public PermissionRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<bool> SetPermissionMatrixAsync(List<PermissionMatrixConfigurationDto> model)
        {

            if (model == null) return false;
            var userGroupIds = model.Select(m => m.UserGroupId).ToList();
            var moduleIds = model.Select(m => m.ModuleId).ToList();
            var allOldRights = _context.AssignUserRights
                               .Where(r => userGroupIds.Contains(r.UserGroupId) && moduleIds.Contains(r.SystemModuleId));
            _context.AssignUserRights.RemoveRange(allOldRights);
            await _context.SaveChangesAsync();

            List<AssignUserRight> allNewRights = new();
            foreach (var dto in model)
            {
                if (dto.PromptTypes != null && dto.PromptTypes.Count > 0)
                {
                    var newRights = dto.PromptTypes.Select(pt => new AssignUserRight
                    {
                        UserGroupId = dto.UserGroupId,
                        SystemModuleId = dto.ModuleId,
                        PromptTypeId = pt.PromptTypeId
                    });

                    allNewRights.AddRange(newRights);
                }
            }

            if (allNewRights.Any())
            {
                await _context.AssignUserRights.AddRangeAsync(allNewRights);
                await _context.SaveChangesAsync();
            }
            return true;

        }

        public async Task<bool> SetSinglePermissionMatrixAsync(PermissionMatrixConfigurationDto dto)
        {

            if (dto == null) return false;
            var oldRights = _context.AssignUserRights
                              .Where(r => r.UserGroupId == dto.UserGroupId && r.SystemModuleId == dto.ModuleId);

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
                    SystemModuleId = dto.ModuleId,
                    PromptTypeId = pt.PromptTypeId
                }).ToList();
                await _context.AssignUserRights.AddRangeAsync(newRights);
                await _context.SaveChangesAsync();
            }
            return true;

        }

        public async Task<PermissionMatrix> GetPermissionMatrixAsync()
        {
            var userGroups = await _context.UserGroups
                .Where(g => g.Id != Components.Enums.RoleHierarchy.NoRoleGroupId)
                .ToListAsync();
            var promptTypes = await _context.PromptTypes.ToListAsync();
            var systemModules = await _context.SystemModules.ToListAsync();
            var assignRights = await _context.AssignUserRights.ToListAsync();

            var modules = new List<PermissionMatrix.ModulePermission>();

            foreach (var module in systemModules)
            {
                var modulePromptDict = new Dictionary<string, List<PromptTypesitem>>();
                foreach (var group in userGroups)
                {
                    // Filter rights matching this module + group
                    var matchedPromptTypeIds = assignRights
                        .Where(r => r.SystemModuleId == module.ModuleID && r.UserGroupId == group.Id)
                        .Select(r => r.PromptTypeId)
                        .Distinct()
                        .ToList();

                    var promptDetails = promptTypes
                        .Where(p => matchedPromptTypeIds.Contains(p.PromptTypeId))
                        .Select(p => new PromptTypesitem
                        {
                            PromptTypeTitle = p.PromptTypeTitle,
                            PromptTypeId = p.PromptTypeId
                        })
                        .ToList();

                    modulePromptDict[group.Name] = promptDetails;
                }

                modules.Add(new PermissionMatrix.ModulePermission
                {
                    ModuleId = module.ModuleID,
                    Name = module.ModuleName,
                    Prompts = modulePromptDict
                });
            }

            return new PermissionMatrix
            {
                UserGroups = userGroups.Select(g => new PermissionMatrix.Groupitem
                {
                    Id = g.Id,
                    Name = g.Name
                }).ToList(),

                PromptTypes = promptTypes.Select(p => new PermissionMatrix.PromptTypesitem
                {
                    PromptTypeId = p.PromptTypeId,
                    PromptTypeTitle = p.PromptTypeTitle
                }).ToList(),

                Modules = modules
            };
        }
    }
}