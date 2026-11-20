using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class UserCompanyResponseDto
    {
        public int UserId { get; set; }
        public string Email { get; set; }
        public int ProfileId { get; set; }
        public string Message { get; set; }
        [JsonIgnore]
        public bool IsNew { get; set; }
    }
}

