using CloverBackoffice.Infrastructure.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class CompanyDto
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
        public bool IsPartner { get; set; }
        public int? ReferredById { get; set; }
        public string? ReferredName { get; set; }

    }
    // POST /api/profile/CreateTrial. Same registration payload as
    // ProfileCreateDto, but this flow additionally provisions the trial
    // subscription and its licences. Validation rules live on the controller so the
    // failure messages stay close to the user.
    public class TrialProfileCreateDto
    {
        [Required] public string ProfileName { get; set; }
        [Required] public string FirstName { get; set; }
        [Required] public string LastName { get; set; }
        [Required] public string EmailAddress { get; set; }
        public string? Password { get; set; }
    }

    public class CompanyLicenseSummaryDto
    {
        public int Id { get; set; }
        public string LicenseKey { get; set; } = string.Empty;
        public string LicenseType { get; set; } = string.Empty;
        public uint? AssignedBusinessUnitId { get; set; }
    }

    public class TrialProfileResponseDto
    {
        public int UserId { get; set; }
        public string Email { get; set; } = string.Empty;
        public int ProfileId { get; set; }
        public uint BusinessUnitId { get; set; }
        public string BusinessUnitShortCode { get; set; } = string.Empty;

        // Message + IsNew are kept off the wire — Message feeds the wrapper
        // envelope, IsNew is internal flow control. Remove from JSON payload.
        [JsonIgnore]
        public string Message { get; set; } = string.Empty;

        [JsonIgnore]
        public bool IsNew { get; set; }
    }

    public class UserInfo
    {
        public int UserId { get; set; }
        public string Email { get; set; }

        public int ProfileId { get; set; }
    }


    public class UnitCreateDto
    {
        public int ProfileId { get; set; }

        public string UnitName { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string EmailAddress { get; set; }
        public string? Password { get; set; }

    }

}
