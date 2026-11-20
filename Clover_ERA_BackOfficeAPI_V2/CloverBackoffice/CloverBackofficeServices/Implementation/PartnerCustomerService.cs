using CloverBackoffice.Components.Enums;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Services.Implementation
{
    public class PartnerCustomerService : IPartnerCustomerService
    {
        private readonly IPartnerCustomerRepository _repository;
        private readonly ApplicationDbContext _context;

        // Reused for the shared email-duplicate rule (Companies + BusinessUnits +
        // Users) so partner-customer creation cannot mint an email that is already
        // registered through any other flow.
        private readonly IAccountRegistrationRepository _accountRepository;

        public PartnerCustomerService(
            IPartnerCustomerRepository repository,
            ApplicationDbContext context,
            IAccountRegistrationRepository accountRepository)
        {
            _repository = repository;
            _context = context;
            _accountRepository = accountRepository;
        }

        public async Task<List<PartnerListDto>> GetAllPartnersAsync()
        {
            return await _repository.GetAllPartnersWithCustomerCountAsync();
        }

        // BE-04: Role-scoped customer listing
        public async Task<List<CustomerListDto>> GetCustomersAsync(int userId)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);
            if (user == null) return new List<CustomerListDto>();

            return user.UserGroupId switch
            {
                (int)UserGroups.RridaAdmin => await _repository.GetAllCustomersAsync(),
                (int)UserGroups.ProfileAdmin when user.ProfileId.HasValue =>
                    await _repository.GetCustomersByPartnerIdAsync((uint)user.ProfileId.Value),
                _ => new List<CustomerListDto>()
            };
        }

        // BE-05: Create customer with auto-assign + BE-07: Duplicate domain check
        public async Task<(bool Success, string Message, CustomerListDto? Customer)> CreateCustomerAsync(
            CreateCustomerDto dto, int userId)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);
            if (user == null)
                return (false, ResponseMessages.Errors.UserNotFound, null);

            // Determine partner ID based on role
            uint? partnerId;
            if (user.UserGroupId == (int)UserGroups.RridaAdmin)
            {
                // Platform admin may name any partner, or leave it unset.
                partnerId = dto.PartnerId;
            }
            else if (user.UserGroupId == (int)UserGroups.ProfileAdmin)
            {
                // BE-05: a Profile Admin auto-assigns to their own profile
                if (!user.ProfileId.HasValue)
                    return (false, ResponseMessages.Errors.InvalidCompany, null);

                partnerId = (uint)user.ProfileId.Value;
            }
            else
            {
                return (false, ResponseMessages.Errors.PartnerCustomerAccessDenied, null);
            }

            // Validate partner exists if specified
            if (partnerId.HasValue)
            {
                var isPartner = await _repository.IsPartnerAsync(partnerId.Value);
                if (!isPartner)
                    return (false, ResponseMessages.Errors.PartnerNotFound, null);
            }

            // Duplicate company names are allowed — customers are identified by Id
            // and domain, so the old name-uniqueness check was removed here too.

            // The email must be free across every store it can land in: a company
            // contact, a business unit contact, or a login. Without this the
            // Contacts insert below hit the unique index on EmailAddress and
            // surfaced as a generic 500.
            var emailUsage = await _accountRepository.GetEmailUsageAsync(dto.EmailAddress);
            if (emailUsage.Exists)
            {
                return (false, emailUsage.IsVerified
                    ? ResponseMessages.Errors.EmailAlreadyExist
                    : ResponseMessages.Errors.EmailExistsNotVerified, null);
            }

            // BE-07: Duplicate domain check
            var domain = dto.EmailAddress.Split('@').Last();
            var existingCompany = await _repository.GetCustomerByDomainAsync(domain);
            if (existingCompany != null)
            {
                var assignedPartnerName = existingCompany.Partner?.ProfileName ?? "another partner";
                return (false,
                    string.Format(ResponseMessages.Errors.CustomerAlreadyAssigned, assignedPartnerName),
                    null);
            }

            // Create customer company
            var company = await _repository.CreateCustomerCompanyAsync(dto, partnerId);

            // Write audit log
            await _repository.AddAuditLogAsync(new PartnerCustomerAuditLog
            {
                ActorUserId = userId,
                CustomerId = company.Id,
                OldPartnerId = null,
                NewPartnerId = partnerId,
                ActionType = "CREATED"
            });

            var result = new CustomerListDto
            {
                Id = company.Id,
                Name = company.ProfileName,
                ProfileShortCode = company.ProfileShortCode,
                DomainName = company.DomainName,
                RegistrationDate = company.RegistrationDate,
                PartnerId = company.PartnerId,
                PartnerName = null
            };

            // Fetch partner name if assigned
            if (partnerId.HasValue)
            {
                var partner = await _repository.GetCompanyByIdAsync(partnerId.Value);
                result.PartnerName = partner?.ProfileName;
            }

            return (true, ResponseMessages.Success.PartnerCustomerCreated, result);
        }

        // BE-06: Reassign customer
        public async Task<(bool Success, string Message)> ReassignCustomerAsync(
            uint customerId, ReassignCustomerDto dto, int actorUserId)
        {
            var customer = await _repository.GetCompanyByIdAsync(customerId);
            if (customer == null)
                return (false, ResponseMessages.Errors.CompanyNotFound);

            if (customer.IsPartner)
                return (false, ResponseMessages.Errors.CannotReassignPartner);

            var isNewPartner = await _repository.IsPartnerAsync(dto.NewPartnerId);
            if (!isNewPartner)
                return (false, ResponseMessages.Errors.PartnerNotFound);

            var oldPartnerId = customer.PartnerId;

            var success = await _repository.ReassignCustomerAsync(customerId, dto.NewPartnerId);
            if (!success)
                return (false, ResponseMessages.Errors.OperationFail);

            // Write audit log
            await _repository.AddAuditLogAsync(new PartnerCustomerAuditLog
            {
                ActorUserId = actorUserId,
                CustomerId = customerId,
                OldPartnerId = oldPartnerId,
                NewPartnerId = dto.NewPartnerId,
                ActionType = "REASSIGNED"
            });

            return (true, ResponseMessages.Success.PartnerCustomerReassigned);
        }

        // BE-10: Check if partner can be deleted (no child customers)
        public async Task<(bool CanDelete, string Message, List<string>? CustomerNames)> CheckPartnerDeletable(
            uint partnerId)
        {
            var partner = await _repository.GetCompanyByIdAsync(partnerId);
            if (partner == null)
                return (false, ResponseMessages.Errors.PartnerNotFound, null);

            if (!partner.IsPartner)
                return (false, ResponseMessages.Errors.CompanyNotPartner, null);

            var customers = await _repository.GetCustomersByPartnerForDeletionAsync(partnerId);
            if (customers.Any())
            {
                var names = customers.Select(c => c.ProfileName).ToList();
                return (false,
                    string.Format(ResponseMessages.Errors.PartnerHasCustomers, string.Join(", ", names)),
                    names);
            }

            return (true, string.Empty, null);
        }

        // BE-10: Soft delete partner
        public async Task<(bool Success, string Message)> SoftDeletePartnerAsync(uint partnerId, int actorUserId)
        {
            var (canDelete, message, customerNames) = await CheckPartnerDeletable(partnerId);
            if (!canDelete)
                return (false, message);

            var success = await _repository.SoftDeleteCompanyAsync(partnerId);
            if (!success)
                return (false, ResponseMessages.Errors.OperationFail);

            // Write audit log
            await _repository.AddAuditLogAsync(new PartnerCustomerAuditLog
            {
                ActorUserId = actorUserId,
                CustomerId = 0,
                OldPartnerId = partnerId,
                NewPartnerId = null,
                ActionType = "SOFT_DELETED"
            });

            return (true, ResponseMessages.Success.PartnerDeleted);
        }
    }
}
