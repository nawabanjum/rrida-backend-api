using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Services.Implementation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Interfaces
{
    public interface ICloveraCompanyAdminService
    {
        Task<UserCompanyResponseDto> CreateCompanyWithUserAsync(CloveraCompanyCreateDto dto);
        Task<bool> UpdatePartnerStatusAsync(int profileId, bool isPartner);
    }
}
