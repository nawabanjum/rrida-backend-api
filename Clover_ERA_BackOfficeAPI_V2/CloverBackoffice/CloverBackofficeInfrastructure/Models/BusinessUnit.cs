using CloverBackoffice.Infrastructure.BusinessModel;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CloverBackoffice.Infrastructure.Models
{
    public class BusinessUnit
    {
        [Key]
        public uint Id { get; init; }

        [MaxLength(DataConstants.CodeLength)]
        public required string BusinessUnitCode { get; init; }

        [MaxLength(DataConstants.ShortCodeLength)]
        public required string BusinessUnitShortCode { get; init; }

        [MaxLength(DataConstants.OrganisationalUnitNameLength)]
        public required string Name { get; set; }

        [MaxLength(DataConstants.OrganisationalUnitNameLength)]
        public required string NameInvariant { get; set; }

        public required DateTime RegistrationDate { get; init; }

        /// <summary>
        /// Owning profile. Was the shadow foreign key <c>ProfileId</c> pointing at
        /// <c>Companies</c>; now an explicit property mapped to column <c>ProfileId</c>.
        /// </summary>
        public uint ProfileId { get; set; }

        [ForeignKey(nameof(ProfileId))]
        public virtual Profile? Profile { get; set; }

        // ── Merged-in contact ────────────────────────────────────────────────
        // Copied from the Contacts row this unit used to point at. The Contacts
        // table is gone, so each unit now owns its own contact details and editing
        // one team's contact no longer edits every team that shared the row.

        [MaxLength(DataConstants.EmailAddressLength)]
        public string? ContactEmailAddress { get; set; }

        [MaxLength(DataConstants.PersonalNameLength)]
        public string? ContactFirstName { get; set; }

        [MaxLength(DataConstants.PersonalNameLength)]
        public string? ContactLastName { get; set; }

        public int? ParentId { get; set; }

        /// <summary>
        /// Number of people in the team. Required when a team is created, optional on edit.
        /// Nullable in the database so teams created before this field existed stay valid.
        /// </summary>
        public int? TeamSize { get; set; }

        public bool? IsDel { get; set; } = false;

        [ForeignKey(nameof(ActiveDailyQuestionSetGroup))]
        public required uint ActiveDailyQuestionSetGroupId { get; init; }
        public virtual required DailyQuestionSetGroup ActiveDailyQuestionSetGroup { get; set; }
    }
}
