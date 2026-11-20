using CloverBackoffice.Components;
using CloverBackoffice.Infrastructure.BusinessModel;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Repository.Implementation
{
    public class PartnerCustomerRepository : IPartnerCustomerRepository
    {
        private readonly ApplicationDbContext _context;

        public PartnerCustomerRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<List<PartnerListDto>> GetAllPartnersWithCustomerCountAsync()
        {
            return await _context.Profiles
                .Where(c => c.IsPartner && !c.IsDel)
                .Select(c => new PartnerListDto
                {
                    Id = c.Id,
                    Name = c.ProfileName,
                    ProfileShortCode = c.ProfileShortCode,
                    DomainName = c.DomainName,
                    RegistrationDate = c.RegistrationDate,
                    CustomerCount = c.Customers != null
                        ? c.Customers.Count(cust => !cust.IsDel)
                        : 0
                })
                .OrderBy(c => c.Name)
                .ToListAsync();
        }

        public async Task<List<CustomerListDto>> GetAllCustomersAsync()
        {
            return await _context.Profiles
                .Where(c => !c.IsPartner && !c.IsDel)
                .Select(c => new CustomerListDto
                {
                    Id = c.Id,
                    Name = c.ProfileName,
                    ProfileShortCode = c.ProfileShortCode,
                    DomainName = c.DomainName,
                    RegistrationDate = c.RegistrationDate,
                    PartnerId = c.PartnerId,
                    PartnerName = c.Partner != null ? c.Partner.ProfileName : null
                })
                .OrderBy(c => c.Name)
                .ToListAsync();
        }

        public async Task<List<CustomerListDto>> GetCustomersByPartnerIdAsync(uint partnerId)
        {
            return await _context.Profiles
                .Where(c => c.PartnerId == partnerId && !c.IsPartner && !c.IsDel)
                .Select(c => new CustomerListDto
                {
                    Id = c.Id,
                    Name = c.ProfileName,
                    ProfileShortCode = c.ProfileShortCode,
                    DomainName = c.DomainName,
                    RegistrationDate = c.RegistrationDate,
                    PartnerId = c.PartnerId,
                    PartnerName = c.Partner != null ? c.Partner.ProfileName : null
                })
                .OrderBy(c => c.Name)
                .ToListAsync();
        }

        public async Task<Profile?> GetCompanyByIdAsync(uint id)
        {
            return await _context.Profiles
                .FirstOrDefaultAsync(c => c.Id == id && !c.IsDel);
        }

        public async Task<bool> IsPartnerAsync(uint profileId)
        {
            return await _context.Profiles
                .AnyAsync(c => c.Id == profileId && c.IsPartner && !c.IsDel);
        }

        public async Task<Profile?> GetCustomerByDomainAsync(string domain)
        {
            return await _context.Profiles
                .Include(c => c.Partner)
                .FirstOrDefaultAsync(c => c.DomainName.ToLower() == domain.ToLower() && !c.IsDel);
        }

        public async Task<Profile> CreateCustomerCompanyAsync(CreateCustomerDto dto, uint? partnerId)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var codes = BussinessManager.GenerateCodes();
                var domain = dto.EmailAddress.Split('@').Last();

                var company = new Profile
                {
                    ProfileShortCode = codes.ShortCode,
                    DomainName = domain,
                    ProfileName = dto.CompanyName,
                    RegistrationDate = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc),
                    ContactFirstName = dto.FirstName,
                    ContactLastName = dto.LastName,
                    ContactEmailAddress = dto.EmailAddress,
                    IsPartner = false,
                    PartnerId = partnerId
                };

                await _context.Profiles.AddAsync(company);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return company;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<bool> ReassignCustomerAsync(uint customerId, uint newPartnerId)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var customer = await _context.Profiles
                    .FirstOrDefaultAsync(c => c.Id == customerId && !c.IsDel);

                if (customer == null) return false;

                customer.PartnerId = newPartnerId;
                _context.Profiles.Update(customer);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return true;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task AddAuditLogAsync(PartnerCustomerAuditLog log)
        {
            log.Timestamp = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
            await _context.PartnerCustomerAuditLogs.AddAsync(log);
            await _context.SaveChangesAsync();
        }

        public async Task<List<Profile>> GetCustomersByPartnerForDeletionAsync(uint partnerId)
        {
            return await _context.Profiles
                .Where(c => c.PartnerId == partnerId && !c.IsDel)
                .ToListAsync();
        }

        public async Task<bool> CompanyNameExistsAsync(string name)
        {
            return await _context.Profiles
                .AnyAsync(c => c.ProfileName.ToLower() == name.ToLower() && !c.IsDel);
        }

        public async Task<bool> SoftDeleteCompanyAsync(uint profileId)
        {
            var company = await _context.Profiles
                .FirstOrDefaultAsync(c => c.Id == profileId && !c.IsDel);

            if (company == null) return false;

            company.IsDel = true;
            _context.Profiles.Update(company);
            await _context.SaveChangesAsync();

            return true;
        }
    }
}
