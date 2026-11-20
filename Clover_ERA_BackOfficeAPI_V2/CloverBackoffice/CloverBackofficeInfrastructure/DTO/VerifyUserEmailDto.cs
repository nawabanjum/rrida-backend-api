using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class VerifyUserEmailDto
    {
        public string Email { get; set; }
        public int UserId { get; set; }
        public DateTime RequestTime { get; set; }

        // Set on invitation links (BussinessManager.BuildInvitationParam); false on
        // the registration verification link.
        public bool IsInvitation { get; set; }
    }
}
