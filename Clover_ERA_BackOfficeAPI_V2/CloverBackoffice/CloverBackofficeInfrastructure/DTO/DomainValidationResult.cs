using CloverBackoffice.Infrastructure.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class DomainValidationResult
    {
        public bool IsValid { get; set; }
        public bool ExistsInDb { get; set; }
        public ExistingCompany ExistingCompany { get; set; }
        public uint? ProfileId { get; set; }
    }

    public class ExistingCompany
    {
        public uint Id { get; set; }
        public string DomainName { get; set; }
        public string Name { get; set; }
        public bool emailExist { get; set; }
        public bool domainExist { get; set; }
        public bool? IsVerified { get; set; } //updated
    }

    // Outcome of the registration-time email duplicate check across Companies,
    // BusinessUnits and Users. IsVerified only carries meaning when Exists is true.
    public class EmailUsageResult
    {
        public bool Exists { get; set; }
        public bool IsVerified { get; set; }
    }

    public class ValidateDomainAndRegisterEmail
    {
        public bool IsValid { get; set; } = true;
        public int ProfileId { get; set; }
        public int UserId { get; set; }
        public int UnitId { get; set; }
        public bool? IsVerified { get; set; }  //updated

        [JsonIgnore]
        public bool IsDomainValid { get; set; } = true;
        [JsonIgnore]
        public bool IsEmailValid { get; set; } = true;
        [JsonIgnore]
        public bool CompanyNotVerified { get; set; } = false;



    }

}
