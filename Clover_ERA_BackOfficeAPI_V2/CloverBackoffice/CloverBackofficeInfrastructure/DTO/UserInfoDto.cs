using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class UserInfoDto
    {
        public int UserId { get; set; }
        public string TenantId { get; set; }
        public string Username { get; set; }
        public string Name { get; set; }
        public string? CreatedDate { get; set; }
        public string? ModifiedDate { get; set; }
        public string? CreatedBy { get; set; }
        public string? ModifiedBy { get; set; }
        public bool? IsActive { get; set; }


    }


}
