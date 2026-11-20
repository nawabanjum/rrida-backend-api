using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Implementation;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Implementation
{
    public class SystemSettingService : ISystemSettingService
    {
        private readonly ISystemSettingRepository _repository;

        public SystemSettingService(ISystemSettingRepository repository)
        {
            _repository = repository;
        }

        public async Task<List<PromptTypes>> GetPromptTypes()
        {
            return await _repository.GetPromptTypes();
        }

        public async Task<List<UserGroup>> GetUserGroups()
        {
            return await _repository.GetUserGroups();
        }

        public async Task<List<SystemModule>> GetSystemModules()
        {
            return await _repository.GetSystemModules();
        }

        public async Task<bool> SaveAssignUserRight(GroupConfigurationDto dto)
        {
            return await _repository.SaveAssignUserRight(dto);
        }

    }
}
