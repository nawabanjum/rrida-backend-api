using CloverBackoffice.Infrastructure.BusinessModel;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CloverBackoffice.Infrastructure.Models
{
    /// <summary>
    /// An organisation. Formerly <c>Company</c> / table <c>Companies</c>; renamed to
    /// Profile so the same table can carry a parent-child hierarchy of organisations.
    ///
    /// The old one-to-one <c>Contacts</c> row is now merged in as the three
    /// <c>Contact*</c> columns below — there is no separate Contact entity any more.
    /// </summary>
    public class Profile
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public uint Id { get; init; }

        /// <summary>Was <c>Companies.CompanyShortCode</c>.</summary>
        [MaxLength(DataConstants.ShortCodeLength)]
        public required string ProfileShortCode { get; init; }

        [MaxLength(DataConstants.DomainNameLength)]
        public required string DomainName { get; set; }

        /// <summary>Was <c>Companies.Name</c>.</summary>
        [MaxLength(DataConstants.OrganisationalUnitNameLength)]
        public required string ProfileName { get; set; }

        public required DateTime RegistrationDate { get; init; }

        // ── Merged-in contact ────────────────────────────────────────────────
        // Copied from the Contacts row this profile used to point at. Nullable
        // because a profile can be created before its contact is known; the old
        // Contacts columns were NOT NULL only because the row was created in the
        // same unit of work.

        [MaxLength(DataConstants.EmailAddressLength)]
        public string? ContactEmailAddress { get; set; }

        [MaxLength(DataConstants.PersonalNameLength)]
        public string? ContactFirstName { get; set; }

        [MaxLength(DataConstants.PersonalNameLength)]
        public string? ContactLastName { get; set; }

        // ── Hierarchy ────────────────────────────────────────────────────────

        /// <summary>
        /// Self-referencing parent. NULL for a root profile. Distinct from
        /// <see cref="PartnerId"/>, which is the commercial partner relationship.
        /// </summary>
        public uint? ParentId { get; set; }

        [ForeignKey(nameof(ParentId))]
        public virtual Profile? Parent { get; set; }

        public virtual ICollection<Profile>? Children { get; set; }

        // ── Partner (unchanged) ──────────────────────────────────────────────

        public bool IsPartner { get; set; } = false;
        public int? ReferredBy { get; set; }
        public uint? PartnerId { get; set; }

        [ForeignKey(nameof(PartnerId))]
        public virtual Profile? Partner { get; set; }
        public virtual ICollection<Profile>? Customers { get; set; }

        public bool IsDel { get; set; } = false;

        public virtual ICollection<BusinessUnit>? BusinessUnits { get; set; }
    }
}
