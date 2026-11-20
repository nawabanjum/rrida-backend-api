using CloverBackoffice.Infrastructure.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class UserInfoResponseDto
    {
        public List<User> Users { get; set; }
        public List<Profile> Profiles { get; set; }
        public bool IsVerified { get; set; }
        public string Token { get; set; }
    }

    public class LoginUserInfoResponseDto
    {
        public List<User> Users { get; set; }
        public List<Profile> Profiles { get; set; }
        public bool IsVerified { get; set; }
        public string Token { get; set; }
    }
}
