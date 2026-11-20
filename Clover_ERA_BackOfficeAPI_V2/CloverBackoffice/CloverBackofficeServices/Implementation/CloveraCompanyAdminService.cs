using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Implementation
{
    public class CloveraCompanyAdminService : ICloveraCompanyAdminService
    {
        private readonly ICloveraCompanyAdminRepository _repository;

        public CloveraCompanyAdminService(ICloveraCompanyAdminRepository repository)
        {
            _repository = repository;
        }
        public async Task<UserCompanyResponseDto> CreateCompanyWithUserAsync(CloveraCompanyCreateDto dto)
        {
            return await _repository.AddCompanyWithUserAsync(dto);

        }
        public async Task<bool> UpdatePartnerStatusAsync(int profileId, bool isPartner)
        {
            var company = await _repository.GetByIdAsync(profileId);
            if (company == null)
                return false;
            company.IsPartner = isPartner;
            await _repository.UpdateAsync(company);

            // If partner is removed, clear ReferredBy/PartnerId on all companies linked to this partner
            if (!isPartner)
            {
                await _repository.ClearPartnerReferencesAsync(profileId);
            }

            return true;
        }
    }
}
