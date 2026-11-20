using System;

namespace CloverBackoffice.Infrastructure.DTO
{
    /// <summary>Body of POST api/profile/resend-invitation and POST api/profile/invitation-link.</summary>
    public class UserInvitationRequestDto
    {
        public int UserId { get; set; }
    }

    /// <summary>Returned by POST api/profile/resend-invitation and POST api/profile/invitation-link.</summary>
    public class UserInvitationResultDto
    {
        public int UserId { get; set; }

        public string Email { get; set; } = string.Empty;

        /// <summary>The invitation (set-password) URL, the same one the invitation email carries. Single-use.</summary>
        public string InviteUrl { get; set; } = string.Empty;

        /// <summary>UTC time after which the link no longer works (3 days from when it was minted).</summary>
        public DateTime ExpiresAt { get; set; }

        /// <summary>resend-invitation only: whether the invitation email was sent.</summary>
        public bool? InvitationSent { get; set; }
    }

    /// <summary>An invitation link minted or found by IAccountRegistrationService.</summary>
    public record InvitationLinkResult(string InviteUrl, DateTime ExpiresAt, bool? EmailSent);
}
