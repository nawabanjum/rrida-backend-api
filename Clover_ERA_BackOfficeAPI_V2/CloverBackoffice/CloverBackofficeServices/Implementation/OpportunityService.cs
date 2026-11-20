using CloverBackoffice.BusinessModel;
using CloverBackoffice.Components;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using SendGrid.Helpers.Mail;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Implementation
{
    public class OpportunityService : IOpportunityService
    {
        private readonly IOpportunityRepository _opportunityRepository;

        public OpportunityService(IOpportunityRepository opportunityRepository)
        {
            _opportunityRepository = opportunityRepository;
        }

        // === Upsert (Create or Update) ===
        public async Task<Opportunity> UpsertAsync(Opportunity opportunity)
        {
            if (opportunity.Id == 0) // Guid.Empty => 0 for int IDs
            {
                opportunity.CreatedAt = DateTime.UtcNow;
                opportunity.UpdatedAt = DateTime.UtcNow;
                return await _opportunityRepository.CreateAsync(opportunity);
            }
            else
            {
                opportunity.UpdatedAt = DateTime.UtcNow;
                return await _opportunityRepository.UpdateAsync(opportunity);
            }
        }

        // === Get all opportunities for a partner (and optional user) ===
        public async Task<IEnumerable<Opportunity>> GetAllAsync(int partnerId, int? userId = null)
        {
            return await _opportunityRepository.GetAllAsync(partnerId, userId);
        }

        // === Get opportunity by ID ===
        public async Task<Opportunity> GetByIdAsync(int id)
        {
            return await _opportunityRepository.GetByIdAsync(id);
        }

        public async Task<bool> CheckCompanyExistsAsync(int profileId)
        {
            return await _opportunityRepository.CheckCompanyExistsAsync(profileId);
        }
        public async Task<bool> CheckOpportunityExistsAsync(int OpportunityId)
        {
            return await _opportunityRepository.CheckOpportunityExistsAsync(OpportunityId);
        }

        // === Get all lookup data for dropdowns ===
        public async Task<object> GetAllLookupsAsync()
        {
            var industries = await _opportunityRepository.GetIndustriesAsync();
            var opportunityTypes = await _opportunityRepository.GetOpportunityTypesAsync();
            var relationshipStatuses = await _opportunityRepository.GetCurrentRelationshipStatusesAsync();
            var numberOfEmployees = await _opportunityRepository.GetNumberOfEmployeesAsync();

            return new
            {
                Industries = industries,
                OpportunityTypes = opportunityTypes,
                CurrentRelationshipStatuses = relationshipStatuses,
                NumberOfEmployees = numberOfEmployees
            };
        }
        public async Task<List<OpportunityListDto>> GetOpportunities(OpportunityFilterRequest filter)
        {
            return await _opportunityRepository.GetOpportunities(filter);
        }

        public async Task<bool> UpdateOpportunityAsync(UpdateOpportunityDTO dto)
        {
            var opportunity = await _opportunityRepository.GetByIdAsync(dto.OpportunityId);

            if (opportunity == null)
                return false;

            opportunity.stageid = dto.stageid;
            opportunity.AdditionalNotes = dto.AdditionalNotes;
            // opportunity.ExpectedCloseDate = Conv  dto.ExpectedCloseDate.ToUniversalTime();
            opportunity.ExpectedCloseDate = BussinessManager.ToUtc(dto.ExpectedCloseDate);
            opportunity.CreatedAt = BussinessManager.ToUtc(opportunity.CreatedAt);
            opportunity.UpdatedAt = DateTime.UtcNow;
            opportunity.UpdatedBy = dto.UpdatedBy;
            return await _opportunityRepository.ApplyOpportunityChangesAsync(opportunity);
        }

        public async Task<OpportunityDetailDto?> GetOpportunityDetailAsync(int id)
        {
            return await _opportunityRepository.GetOpportunityByIdAsync(id);
        }
        public async Task<OpportunityDashboardDto?> GetOpportunityDashboardAsync(int profileId)
        {
            return await _opportunityRepository.GetOpportunityDashboardAsync(profileId);
        }
        public async Task<DomainCheckResponse> CheckDomainExistsAsync(string domainName)
        {
            // Extract domain part if an email is provided
            domainName = domainName.Contains("@")
                ? domainName.Split('@')[1].ToLower()
                : domainName.ToLower();

            // Extract base domain (last 2 parts)
            var domainParts = domainName.Split('.');
            string baseDomain = domainParts.Length >= 2
                ? $"{domainParts[^2]}.{domainParts[^1]}"
                : domainName; // fallback

            // FIRST CHECK → Does base domain exist in Companies table ?
            var baseDomainExists = await _opportunityRepository.DoesBaseDomainExistAsync(baseDomain);

            if (baseDomainExists != null)
            {
                return new DomainCheckResponse
                {
                    Domain = baseDomain,
                    IsAvailable = false,
                    Message = ResponseMessages.Errors.DomainAlreadyUsed
                };
            }


            // Step 1: Check in Companies table
            var existsInCompany = await _opportunityRepository.DoesDomainExistAsync(domainName);

            if (existsInCompany == null)
            {
                return new DomainCheckResponse
                {
                    Domain = domainName,
                    IsAvailable = true,
                    Message = ResponseMessages.Success.CompanyEmailAvailable
                };
            }

            // Step 2: Check if domain has active opportunities (not expired)
            var isActiveInOpportunities = await _opportunityRepository.IsDomainActiveInOpportunitiesAsync(existsInCompany.Id);

            if (!isActiveInOpportunities)
            {
                return new DomainCheckResponse
                {
                    Domain = domainName,
                    IsAvailable = false,
                    Message = ResponseMessages.Errors.DomainAlreadyUsed
                };
            }

            // SUCCESS
            return new DomainCheckResponse
            {
                Domain = domainName,
                IsAvailable = true,
                Message = ResponseMessages.Success.CompanyEmailAvailable
            };
        }
        public async Task<OpportunityCombinedResponseDto> GetOpportunitiesWithStatsAsync(int userId, int profileId, OpportunityFilterRequest filter)
        {
            await _opportunityRepository.AutoCloseOpportunitiesAsync();
            return await _opportunityRepository.GetOpportunitiesWithStatsAsync(userId, profileId, filter);
        }
    }
}
