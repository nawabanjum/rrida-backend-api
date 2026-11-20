using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class ResetPasswordPayload
    {
        public string Email { get; set; }
        public int UserId { get; set; }
        public DateTime RequestTime { get; set; }
        public bool IsManuallyReset { get; set; } = false;

        /// <summary>
        /// Minted by an INVITATION link (SendEmailToUserAsync), not by
        /// forgot-password.
        ///
        /// The two share this payload and the reset-password endpoint, but they
        /// are not the same thing: a forgot-password link is a short-lived
        /// response to something the user just did, while an invitation is sent
        /// to somebody who is not expecting it and may not open their mail for
        /// hours. Without this flag an invitation inherited the 30-minute
        /// forgot-password window, so a manager who read their email the next
        /// morning could never set a password.
        ///
        /// Absent from links minted before this existed, which deserialise to
        /// false and keep the old window.
        /// </summary>
        public bool IsInvitation { get; set; } = false;
    }
}
