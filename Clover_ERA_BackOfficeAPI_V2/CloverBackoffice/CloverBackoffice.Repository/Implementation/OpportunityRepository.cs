using CloverBackoffice.BusinessModel;
using CloverBackoffice.Infrastructure.BusinessModel;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CloverBackoffice.Repository
{
    public class OpportunityRepository : IOpportunityRepository
    {
        private readonly ApplicationDbContext _context;

        public OpportunityRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<Opportunity> CreateAsync(Opportunity opportunity)
        {
            try
            {
                opportunity.CreatedAt = System.DateTime.UtcNow;
                opportunity.UpdatedAt = System.DateTime.UtcNow;

                _context.Opportunities.Add(opportunity);
                await _context.SaveChangesAsync();
                return opportunity;
            }
            catch (Exception ex)
            {

                throw;
            }
        }


        public async Task<Opportunity> UpdateAsync(Opportunity opportunity)
        {
            opportunity.UpdatedAt = System.DateTime.UtcNow;

            _context.Opportunities.Update(opportunity);
            await _context.SaveChangesAsync();
            return opportunity;
        }

        public async Task<Opportunity> GetByIdAsync(int id)
        {
            return await _context.Opportunities
                .FirstOrDefaultAsync(o => o.Id == id);
        }

        public async Task<IEnumerable<Opportunity>> GetAllAsync(int partnerId, int? userId = null)
        {
            var query = _context.Opportunities.AsQueryable();

            if (userId.HasValue)
                query = query.Where(o => o.CreatedBy == userId.Value);
            else
                query = query.Where(o => o.CreatedBy == partnerId);

            return await query.ToListAsync();
        }

        public async Task<bool> CheckCompanyExistsAsync(int profileId)
        {
            return await _context.Profiles.AnyAsync(c => c.Id == profileId && c.IsPartner);
        }
        public async Task<bool> CheckOpportunityExistsAsync(int OpportunityId)
        {
            return await _context.Opportunities.AnyAsync(c => c.Id == OpportunityId);
        }



        public async Task<IEnumerable<LookupItem>> GetIndustriesAsync() =>
            await _context.Industries
                .Select(x => new LookupItem { Id = x.Id, Name = x.Name })
                .ToListAsync();

        public async Task<IEnumerable<LookupItem>> GetOpportunityTypesAsync() =>
            await _context.OpportunityTypes
                .Select(x => new LookupItem { Id = x.Id, Name = x.Name })
                .ToListAsync();

        public async Task<IEnumerable<LookupItem>> GetCurrentRelationshipStatusesAsync() =>
            await _context.CurrentRelationshipStatuses
                .Select(x => new LookupItem { Id = x.Id, Name = x.Name })
                .ToListAsync();

        public async Task<IEnumerable<LookupItem>> GetNumberOfEmployeesAsync() =>
            await _context.NumberOfEmployees
                .Select(x => new LookupItem { Id = x.Id, Name = x.Name })
                .ToListAsync();

        public async Task<List<OpportunityListDto>> GetOpportunities(OpportunityFilterRequest filter)
        {
            var today = DateTime.UtcNow.Date;

            var query = _context.Opportunities
       .Where(o => !o.IsDel)
       .Select(o => new OpportunityListDto
       {
           Id = o.Id,
           CompanyName = o.CompanyName,
           ContactName = o.PrimaryContactName,
           ContactTitle = o.PrimaryContactTitle,
           AdditionalNotes =  o.AdditionalNotes,

           StageName = _context.OpportunityTypes
               .Where(s => s.Id == o.stageid)
               .Select(s => s.Name)
               .FirstOrDefault(),

           ExpectedCloseDate = o.ExpectedCloseDate,
           DaysLeft = (o.ExpectedCloseDate - today).Days,   // ← FIXED

           LastUpdate = o.UpdatedAt
       });
            if (filter.StageId.HasValue && filter.StageId.Value > 0)
                query = query.Where(x => x.StageName != null && _context.OpportunityTypes
                    .Any(s => s.Id == filter.StageId && s.Name == x.StageName));
            if (filter.Status == "Active")
                query = query.Where(x => x.ExpectedCloseDate >= today);

            if (filter.Status == "15days")
                query = query.Where(x => x.DaysLeft <= 15 && x.DaysLeft >= 0);

            if (filter.Status == "Closed")
                query = query.Where(x => x.ExpectedCloseDate < today);

            if (!string.IsNullOrEmpty(filter.CompanyName))
                query = query.Where(x => x.CompanyName.Contains(filter.CompanyName));

            return await query
               .OrderByDescending(x => x.LastUpdate)
               .ToListAsync();
        }

        public async Task<bool> ApplyOpportunityChangesAsync(Opportunity opportunity)
        {
            try
            {
                _context.Opportunities.Update(opportunity);
                await _context.SaveChangesAsync();
                return true;
            }
            catch (Exception)
            {

                throw;
            }

        }

        public async Task<OpportunityDetailDto?> GetOpportunityByIdAsync(int id)
        {
            var opp = await _context.Opportunities
                .Where(o => o.Id == id)
                .Select(o => new OpportunityDetailDto
                {
                    Id = o.Id,
                    CompanyName = o.CompanyName,
                    CompanyWebsite = o.CompanyWebsite,
                    PrimaryContactName = o.PrimaryContactName,
                    PrimaryContactTitle = o.PrimaryContactTitle,
                    PrimaryContactEmail = o.PrimaryContactEmail,
                    PrimaryContactPhone = o.PrimaryContactPhone,
                    CompanyLocation = o.CompanyLocation,
                    ExpectedCloseDate = o.ExpectedCloseDate,
                    EstimatedDealValue = o.EstimatedDealValue,
                    EstimatedRevenue = o.EstimatedRevenue,
                    EstimatedTeams = o.EstimatedTeams,
                    BusinessPainPoints = o.BusinessPainPoints,
                    AdditionalNotes = o.AdditionalNotes,
                    AcceptTerms1 = o.AcceptTerms1,
                    AcceptTerms2 = o.AcceptTerms2,

                    // IDs
                    IndustryId = o.Industryid,
                    NumberOfEmployeesId = o.NumberOfEmployeesid,
                    RelationshipStatusId = o.CurrentRelationshipStatusid,
                    StageId = o.stageid,

                    // JOINED NAMES
                    IndustryName = _context.Industries
                        .Where(i => i.Id == o.Industryid)
                        .Select(i => i.Name)
                        .FirstOrDefault(),

                    NumberOfEmployeesName = _context.NumberOfEmployees
                        .Where(e => e.Id == o.NumberOfEmployeesid)
                        .Select(e => e.Name)
                        .FirstOrDefault(),

                    RelationshipStatusName = _context.CurrentRelationshipStatuses
                        .Where(r => r.Id == o.CurrentRelationshipStatusid)
                        .Select(r => r.Name)
                        .FirstOrDefault(),

                    StageName = _context.OpportunityTypes
                        .Where(s => s.Id == o.stageid)
                        .Select(s => s.Name)
                        .FirstOrDefault(),

                    // COMPANY DETAILS
                    Company = _context.Profiles
                        .Where(c => c.Id == o.ProfileId)
                        .Select(c => new CompaniesDto
                        {
                            Id = c.Id,
                            Name = c.ProfileName,
                            DomainName = c.DomainName,
                            ProfileShortCode = c.ProfileShortCode,
                            IsPartner = c.IsPartner
                        })
                        .FirstOrDefault()
                })
                .FirstOrDefaultAsync();

            return opp;
        }

        public async Task<OpportunityDashboardDto?> GetOpportunityDashboardAsync(int profileId)
        {
            var today = DateTime.UtcNow;

            // Check if company has any opportunity
            bool companyExists = await _context.Opportunities
                .AnyAsync(x => x.ProfileId == profileId && x.IsDel == false);

            if (!companyExists)
                return null;

            // Query base
            var query = _context.Opportunities
                .Where(x => x.ProfileId == profileId && x.IsDel == false);

            var totalOpportunities = await query.CountAsync();
            var totalRevenue = await query.SumAsync(x => x.EstimatedRevenue);

            // Active Pipeline → ExpectedCloseDate more than 15 days away from today
            var activePipelineCount = await query
                .CountAsync(x => x.ExpectedCloseDate > today.AddDays(15));

            // Expiring Soon → ExpectedCloseDate within next 15 days
            var expiringSoonCount = await query
                .CountAsync(x => x.ExpectedCloseDate <= today.AddDays(15));

            var closedWon = await query.CountAsync(x => x.stageid == 5);

            return new OpportunityDashboardDto
            {
                TotalOpportunities = totalOpportunities,
                TotalEstimatedMonthlyValue = totalRevenue,
                TotalActivePipeline = activePipelineCount,
                TotalExpiringSoon = expiringSoonCount,
                TotalClosedWon = closedWon
            };
        }

        public async Task<Profile> DoesDomainExistAsync(string domainName)
        {
            try
            {
                // Compare only the domain part
                return await _context.Profiles
                    .FirstOrDefaultAsync(c => c.DomainName.ToLower() == domainName.ToLower());
            }
            catch (Exception)
            {
                throw;
            }
        }
        public async Task<Profile?> DoesBaseDomainExistAsync(string baseDomain)
        {
            return await _context.Profiles
                .FirstOrDefaultAsync(c => c.DomainName.ToLower().EndsWith(baseDomain));
        }

        // Check domain in Opportunities table
        public async Task<bool> IsDomainActiveInOpportunitiesAsync(uint ProfileId)
        {
            try
            {
                var today = DateTime.UtcNow.Date;

                // Check if there is an active opportunity for this domain
                // stageid = 6 and ExpectedCloseDate >= today
                return await _context.Opportunities.OrderByDescending(a => a.CreatedAt).AnyAsync(o =>
                    o.stageid == 6 && o.ProfileId == ProfileId &&
                    o.ExpectedCloseDate.Date <= today
                );
            }
            catch (Exception)
            {
                throw;
            }
        }
        public async Task<int> AutoCloseOpportunitiesAsync()
        {
            var currentDate = DateTime.UtcNow;

            // Step 1: Get all opportunities where StageId != 6 AND ExpectedCloseDate <= today
            var opportunitiesToUpdate = await _context.Opportunities
                .Where(o => o.stageid != 6 && o.ExpectedCloseDate <= currentDate)
                .ToListAsync();

            if (opportunitiesToUpdate.Count == 0)
                return 0;

            // Step 2: Update StageId to 6
            foreach (var opp in opportunitiesToUpdate)
            {
                opp.stageid = 6;
                opp.UpdatedAt = DateTime.UtcNow;
            }

            // Step 3: Save all updates
            await _context.SaveChangesAsync();

            return opportunitiesToUpdate.Count;
        }

        public async Task<OpportunityCombinedResponseDto> GetOpportunitiesWithStatsAsync(int userId, int? profileId, OpportunityFilterRequest filter)
        {
            var today = DateTime.UtcNow.Date;

            // 1) Get user + role
            var user = await _context.Users
                .Where(x => x.UserId == userId)
                .Select(x => new { x.UserGroupId })
                .FirstOrDefaultAsync();

            bool isAdmin = user != null && user.UserGroupId == (int)CloverBackoffice.Components.Enums.UserGroups.RridaAdmin;

            IQueryable<Opportunity> query;

            // 2) Base: non-deleted
            query = _context.Opportunities.Where(o => !o.IsDel);

            // 3) NON-ADMIN → must filter by profile
            if (!isAdmin)
            {
                if (!profileId.HasValue || profileId.Value <= 0)
                {
                    return new OpportunityCombinedResponseDto
                    {
                        Opportunities = new List<OpportunityListDto>(),
                        Stats = new OpportunityDashboardDto()
                    };
                }

                query = query.Where(o => o.ProfileId == profileId.Value);
            }

            // 4) Apply filters
            if (filter != null)
            {
                if (filter.StageId.HasValue && filter.StageId.Value > 0)
                    query = query.Where(o => o.stageid == filter.StageId.Value);

                if (!string.IsNullOrWhiteSpace(filter.Status))
                {
                    switch (filter.Status.ToLower())
                    {
                        case "active":
                            query = query.Where(o => o.ExpectedCloseDate >= today);
                            break;

                        case "15days":
                            query = query.Where(o =>
                                (o.ExpectedCloseDate - today).Days <= 15 &&
                                (o.ExpectedCloseDate - today).Days >= 0);
                            break;

                        case "closed":
                            query = query.Where(o => o.ExpectedCloseDate < today);
                            break;
                    }
                }

                if (!string.IsNullOrWhiteSpace(filter.CompanyName))
                {
                    var profileIds = await _context.Profiles
                        .Where(c => c.ProfileName.Contains(filter.CompanyName))
                        .Select(c => (int)c.Id)
                        .ToListAsync();

                    query = query.Where(o => profileIds.Contains(o.ProfileId));
                }
                // New OpportunityName filter
                if (!string.IsNullOrWhiteSpace(filter.OpportunityName))
                {
                    query = query.Where(o =>
                        o.CompanyName.Contains(filter.OpportunityName));
                }

            }

            // 5) GET LIST (Admin gets extra PartnerName)
            var opportunities = await query
                .Select(o => new OpportunityListDto
                {
                    Id = o.Id,
                    CompanyName = o.CompanyName,
                    ContactName = o.PrimaryContactName,
                    ContactTitle = o.PrimaryContactTitle,
                    AdditionalNotes = o.AdditionalNotes,

                    StageName = _context.OpportunityTypes
                        .Where(s => s.Id == o.stageid)
                        .Select(s => s.Name)
                        .FirstOrDefault(),

                    ExpectedCloseDate = o.ExpectedCloseDate,
                    DaysLeft = (o.ExpectedCloseDate - today).Days,
                    LastUpdate = o.UpdatedAt,

                    // ONLY ADMINS GET THIS
                    PartnerName = isAdmin
                        ? _context.Profiles
                            .Where(c => c.Id == o.ProfileId)
                            .Select(c => c.ProfileName)
                            .FirstOrDefault()
                        : null
                })
                .OrderByDescending(x => x.LastUpdate)
                .ToListAsync();

            // 6) Stats
            var stats = new OpportunityDashboardDto
            {
                TotalOpportunities = await query.CountAsync(),
                TotalEstimatedMonthlyValue = await query.SumAsync(x => x.EstimatedRevenue),
                TotalActivePipeline = await query.CountAsync(x => x.ExpectedCloseDate > today.AddDays(15)),
                TotalExpiringSoon = await query.CountAsync(x => x.ExpectedCloseDate <= today.AddDays(15)),
                TotalClosedWon = await query.CountAsync(x => x.stageid == 5)
            };

            return new OpportunityCombinedResponseDto
            {
                Opportunities = opportunities,
                Stats = stats
            };
        }


    }
}
