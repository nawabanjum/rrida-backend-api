using CloverBackoffice.Infrastructure.BusinessModel;
using CloverBackoffice.Infrastructure.DTO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Interfaces
{
    public interface IPermissionService
    {
        Task<PermissionMatrix> GetPermissionMatrixAsync();
        Task<bool> SetPermissionMatrixAsync(List<PermissionMatrixConfigurationDto> model);
        Task<bool> SetSinglePermissionMatrixAsync(PermissionMatrixConfigurationDto model);
    }
}
