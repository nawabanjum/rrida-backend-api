namespace CloverBackoffice.Infrastructure.DTO
{
    /// <summary>
    /// The organisation contact, as it is now stored: three inline columns on
    /// <c>Profiles</c> and on <c>BusinessUnits</c> rather than a row in the old
    /// <c>Contacts</c> table. Carries no Id, because there is no contact row to
    /// point at any more — the owning profile or unit is the identity.
    /// </summary>
    public class ContactInfoDto
    {
        public string EmailAddress { get; set; } = string.Empty;
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
    }
}
