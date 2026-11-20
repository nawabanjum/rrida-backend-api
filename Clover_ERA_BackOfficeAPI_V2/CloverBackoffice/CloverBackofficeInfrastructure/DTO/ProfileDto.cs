using System.ComponentModel.DataAnnotations;

namespace CloverBackoffice.Infrastructure.DTO
{
    /// <summary>
    /// Registration payload for POST api/profile/Create.
    ///
    /// Replaces CompanyCreateDto, which served the retired POST api/company/Create.
    /// The organisation field is <see cref="ProfileName"/> rather than CompanyName,
    /// matching the Profiles table it is written to.
    /// </summary>
    public class ProfileCreateDto
    {
        [Required(ErrorMessage = "Profile name is required.")]
        [MaxLength(140)]
        public string ProfileName { get; set; } = string.Empty;

        [Required(ErrorMessage = "First name is required.")]
        [MaxLength(60)]
        public string FirstName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Last name is required.")]
        [MaxLength(60)]
        public string LastName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Email address is required.")]
        [EmailAddress(ErrorMessage = "Email is not valid.")]
        [MaxLength(254)]
        public string EmailAddress { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password is required.")]
        public string Password { get; set; } = string.Empty;
    }

    /// <summary>
    /// What POST api/profile/Create returns once the profile and its user are
    /// committed.
    /// </summary>
    public class ProfileCreateResponseDto
    {
        public int ProfileId { get; set; }
        public string ProfileName { get; set; } = string.Empty;

        /// <summary>
        /// Stored on the profile as an attribute, taken from the registrant's address.
        /// It is not an identifier: profiles may share a domain, and nothing is looked
        /// up or associated by it.
        /// </summary>
        public string DomainName { get; set; } = string.Empty;

        public int UserId { get; set; }
        public string Email { get; set; } = string.Empty;
    }
}
