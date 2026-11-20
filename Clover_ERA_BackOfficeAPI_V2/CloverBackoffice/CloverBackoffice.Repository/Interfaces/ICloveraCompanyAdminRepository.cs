using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Repository.Interfaces
{
    public interface ICloveraCompanyAdminRepository
    {
        Task<UserCompanyResponseDto> AddCompanyWithUserAsync(CloveraCompanyCreateDto dto);

        Task<Profile?> GetByIdAsync(int profileId);
        Task UpdateAsync(Profile company);
        Task ClearPartnerReferencesAsync(int partnerId);
    }
}
