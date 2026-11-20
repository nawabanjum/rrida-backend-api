using CloverBackoffice.Infrastructure.BusinessModel;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Implementation
{
    public class PermissionService : IPermissionService
    {
        private readonly IPermissionRepository _repository;

        public PermissionService(IPermissionRepository repository)
        {
            _repository = repository;
        }
        public async Task<bool> SetPermissionMatrixAsync(List<PermissionMatrixConfigurationDto> model)
        {
            return await _repository.SetPermissionMatrixAsync(model);
        }
        public async Task<bool> SetSinglePermissionMatrixAsync(PermissionMatrixConfigurationDto model)
        {
            return await _repository.SetSinglePermissionMatrixAsync(model);
        }

        public async Task<PermissionMatrix> GetPermissionMatrixAsync()
        {
            return await _repository.GetPermissionMatrixAsync();
        }
    }
}
