using CloverBackoffice.Infrastructure.BusinessModel;
using CloverBackoffice.Infrastructure.DTO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Repository.Interfaces
{
    public interface IPermissionRepository
    {
        Task<PermissionMatrix> GetPermissionMatrixAsync();

        Task<bool> SetPermissionMatrixAsync(List<PermissionMatrixConfigurationDto> model);
        Task<bool> SetSinglePermissionMatrixAsync(PermissionMatrixConfigurationDto dto);
    }
}
