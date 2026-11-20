using System.ComponentModel.DataAnnotations;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class PartnerListDto
    {
        public uint Id { get; set; }
        public string Name { get; set; }

        /// Was Companies.CompanyShortCode.
        public string ProfileShortCode { get; set; }
        public string DomainName { get; set; }
        public DateTime RegistrationDate { get; set; }
        public int CustomerCount { get; set; }
    }

    public class CustomerListDto
    {
        public uint Id { get; set; }
        public string Name { get; set; }

        /// Was Companies.CompanyShortCode.
        public string ProfileShortCode { get; set; }
        public string DomainName { get; set; }
        public DateTime RegistrationDate { get; set; }
        public uint? PartnerId { get; set; }
        public string? PartnerName { get; set; }
    }

    // CountryCode is gone from the payload: the column it was stored in was
    // dropped with the Companies -> Profiles redesign.
    public class CreateCustomerDto
    {
        [Required]
        public string CompanyName { get; set; }

        [Required]
        public string FirstName { get; set; }

        [Required]
        public string LastName { get; set; }

        [Required]
        [EmailAddress]
        public string EmailAddress { get; set; }

        public uint? PartnerId { get; set; }
    }

    public class ReassignCustomerDto
    {
        [Required]
        public uint NewPartnerId { get; set; }
    }
}
