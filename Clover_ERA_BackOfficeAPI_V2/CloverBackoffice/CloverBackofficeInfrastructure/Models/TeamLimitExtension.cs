using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CloverBackoffice.Infrastructure.Models
{
    /// <summary>
    /// One row per team limit extension made by a RRIDA Admin (Super Admin) through
    /// POST /api/license-assignments/extend-team-limit. The rows are both the audit
    /// trail and the source of the extra teams: a licence allows
    /// LicenseTeamLimits.MaxTeamsPerLicense + the sum of its profile's PROFILE rows
    /// + the sum of its own LICENSE rows.
    ///   * PROFILE — every Team Manager licence of the profile allows AdditionalTeams more.
    ///   * LICENSE — only CompanyLicenseId (the manager's licence) allows AdditionalTeams more.
    /// </summary>
    [Table("TeamLimitExtensions", Schema = "public")]
    public class TeamLimitExtension
    {
        public const int NotesMaxLength = 500;

        public const string ProfileScope = "PROFILE";
        public const string LicenseScope = "LICENSE";

        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        public uint ProfileId { get; set; }

        /// <summary>PROFILE or LICENSE.</summary>
        [MaxLength(20)]
        public string Scope { get; set; } = string.Empty;

        /// <summary>The extended licence. Null on a PROFILE extension.</summary>
        public int? CompanyLicenseId { get; set; }

        /// <summary>The Team Manager holding the licence when it was extended, if any.</summary>
        public int? ManagerUserId { get; set; }

        /// <summary>Teams added by this extension (always positive).</summary>
        public int AdditionalTeams { get; set; }

        /// <summary>
        /// Teams allowed before / after: per licence for PROFILE, for the licence for LICENSE.
        /// </summary>
        public int PreviousLimit { get; set; }
        public int NewLimit { get; set; }

        [MaxLength(NotesMaxLength)]
        public string? Notes { get; set; }

        /// <summary>The RRIDA Admin who extended the limit.</summary>
        public int ExtendedByUserId { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
