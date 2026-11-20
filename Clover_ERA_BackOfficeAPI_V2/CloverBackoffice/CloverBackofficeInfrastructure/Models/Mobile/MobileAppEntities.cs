using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CloverBackoffice.Infrastructure.Models.Mobile
{
    // ═════════════════════════════════════════════════════════════════════════
    // The three tables the mobile app writes that the back office did not model.
    //
    // All three already exist in the database and hold live data, so these
    // classes are shaped to the columns that are there rather than to what a
    // fresh design would produce. Two columns in particular do not follow the
    // house convention and are pinned explicitly in MobileModelConfiguration:
    //
    //   DailyReflections.PersonalBusinessUnitId  is integer, not bigint
    //   FeedBack.UserSubmittedAtUtc              is timestamp WITHOUT time zone
    //
    // Getting either wrong throws at runtime rather than at build time, which
    // is why they are called out here as well as in the configuration.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// One day's productivity reflection submitted from the mobile app: where the
    /// time went, what got done, what did not, and what is planned for tomorrow.
    /// </summary>
    [Table("DailyReflections", Schema = "public")]
    public class DailyReflection
    {
        public long Id { get; init; }

        /// <summary>The team the reflection was filed against.</summary>
        [ForeignKey(nameof(BusinessUnit))]
        public required uint BusinessUnitId { get; set; }

        public virtual BusinessUnit? BusinessUnit { get; set; }

        /// <summary>
        /// The individual's own unit. Held as int because the column is integer —
        /// see the note at the top of this file.
        /// </summary>
        public required int PersonalBusinessUnitId { get; set; }

        /// <summary>
        /// The date the reflection is about, in the member's local calendar. A date
        /// rather than a timestamp on purpose: "which day was this" must not shift
        /// when read back in another timezone.
        /// </summary>
        public required DateOnly ReflectionLocalDate { get; init; }

        /// <summary>How the day's hours were split across productivity options.</summary>
        public virtual ICollection<TimeAllocation>? TimeAllocations { get; set; }

        // ── The three free-text lists, stored as Postgres text[] ──
        public string[]? AchievementsLines { get; init; }
        public string[]? NotDoneLines { get; init; }
        public string[]? BlockersLines { get; init; }

        [MaxLength(500)]
        public string? TomorrowReminderText { get; init; }

        /// <summary>
        /// Kept as a string, not a TimeOnly: the app sends a local wall-clock time
        /// with no date and no offset, and storing it as typed time would imply a
        /// precision the value does not have.
        ///
        /// The live column is varchar(20). Stated here so EF knows the real limit —
        /// the old API left it unconstrained, which meant an over-long value surfaced
        /// as a Postgres truncation error instead of a clean validation failure.
        /// </summary>
        [MaxLength(20)]
        public string? TomorrowReminderLocalTime { get; init; }

        /// <summary>When the handset says it submitted. Client clock, so not authoritative.</summary>
        public DateTime? ClientSubmittedAtUtc { get; init; }

        public bool IsDel { get; init; }
    }

    /// <summary>
    /// Hours booked against one productivity option within a day's reflection.
    /// </summary>
    [Table("TimeAllocation", Schema = "public")]
    public class TimeAllocation
    {
        public long Id { get; set; }

        [ForeignKey(nameof(DailyReflection))]
        public long DailyReflectionId { get; set; }

        public virtual DailyReflection? DailyReflection { get; set; }

        /// <summary>The productivity option chosen. The app owns this vocabulary.</summary>
        public int OptionId { get; set; }

        public decimal Hours { get; set; }
    }

    /// <summary>
    /// Free-text product feedback ("suggestions") sent from the mobile app. Unrelated
    /// to <see cref="DailyFeedback" />, which is an answer to a daily question.
    /// </summary>
    [Table("FeedBack", Schema = "public")]
    public class FeedBack
    {
        public long Id { get; set; }

        [ForeignKey(nameof(BusinessUnit))]
        public required uint BusinessUnitId { get; set; }

        public virtual BusinessUnit? BusinessUnit { get; set; }

        public required string FeedbackText { get; set; }

        /// <summary>
        /// When the handset says it submitted. The column is timestamp WITHOUT time
        /// zone, so the value written must carry DateTimeKind.Unspecified — see the
        /// note at the top of this file.
        /// </summary>
        public DateTime UserSubmittedAtUtc { get; init; }
    }
}
