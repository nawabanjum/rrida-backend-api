using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CloverBackoffice.Infrastructure.Models
{
    /// <summary>
    /// One row per trial extension made by a RRIDA Admin (Super Admin) through
    /// POST /api/subscription/apply: who did it, which profile (and unit, for a
    /// single-unit extension), and the trial end date / grace before and after.
    /// CompanySubscriptionAuditLogs keeps the row snapshot; this keeps the action.
    /// </summary>
    [Table("TrialExtensionAuditLogs", Schema = "public")]
    public class TrialExtensionAuditLog
    {
        public const int NotesMaxLength = 500;

        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int AuditId { get; set; }

        public uint ProfileId { get; set; }

        /// <summary>The trial subscription extended. Null on a single-unit extension.</summary>
        public int? SubscriptionId { get; set; }

        /// <summary>Set on a single-unit extension only.</summary>
        public uint? UnitId { get; set; }

        /// <summary>PROFILE or UNIT.</summary>
        [MaxLength(20)]
        public string Scope { get; set; } = string.Empty;

        /// <summary>Days requested (ExtendByDays). Null when an explicit EndDate was sent.</summary>
        public int? DaysAdded { get; set; }

        public DateTime? PreviousTrialEndDate { get; set; }
        public DateTime? NewTrialEndDate { get; set; }

        public int? PreviousGracePeriod { get; set; }
        public int? NewGracePeriod { get; set; }

        /// <summary>Licences whose expiry moved with the trial end date.</summary>
        public int LicensesUpdated { get; set; }

        [MaxLength(NotesMaxLength)]
        public string? Notes { get; set; }

        /// <summary>The RRIDA Admin who extended the trial.</summary>
        public int ExtendedByUserId { get; set; }

        public DateTime LoggedAt { get; set; }
    }
}
