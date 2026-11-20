using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Repository.Interfaces
{
    public interface ISystemSettingRepository
    {
        Task<List<PromptTypes>> GetPromptTypes();
        Task<List<UserGroup>> GetUserGroups();
        Task<List<SystemModule>> GetSystemModules();
        Task<bool> SaveAssignUserRight(GroupConfigurationDto dto);
    }
}
