using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class RefreshTokenDto
    {
        public string accessToken { get; set; }
        public string refreshToken { get; set; }
        public int expiresIn { get; set; }

        [JsonIgnore]
        public bool isVaild { get; set; }
    }
}
