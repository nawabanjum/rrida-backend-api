using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class ResetPasswordDto
    {
        public string Param { get; set; }
        public string NewPassword { get; set; }
    }
}
