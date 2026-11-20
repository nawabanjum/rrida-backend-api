using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class CloveraCompanyAdminDto
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int? Id { get; set; }
        public string DomainName { get; set; }
        public string Name { get; set; }

        [Required]
        public string FirstName { get; set; }

        [Required]
        public string LastName { get; set; }

        [Required]
        public string EmailAddress { get; set; }

    }
    public class CloveraCompanyCreateDto
    {
        public string CompanyName { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string EmailAddress { get; set; }
        //public string? Password { get; set; }
        public bool IsPartner { get; set; } = false;
        public int? ReferredBy { get; set; }


    }

    public class CloveraUserInfo
    {
        public int UserId { get; set; }
        public string Email { get; set; }

        public int ProfileId { get; set; }
    }


    public class CloveraUnitCreateDto
    {
        public int ProfileId { get; set; }
        public string CompanyName { get; set; }
        public string UnitName { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string EmailAddress { get; set; }
        public string? Password { get; set; }
    }

    public class UpdateCompanyPartnerStatusDto
    {
        public int ProfileId { get; set; }
        public bool IsPartner { get; set; }
    }
}
