using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class UpdatePasswordDto
    {
        public int UserId { get; set; }
        public string currentPassword { get; set; }
        public string newPassword { get; set; }
    }
}