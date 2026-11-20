using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CloverBackoffice.Infrastructure.Models.DeterministicLayer
{
    // ═══════════════════════════════════════════════════════════════════════════
    // Deterministic layer — the facts.
    //
    // Everything the engine consumes is replayed from these tables: who is enrolled,
    // what they answered, and what the manager did about it. The engine keeps its
    // running state in memory and cannot be rehydrated, so the facts are the record
    // and the state is always derived from them.
    //
    // ANONYMITY. The engine needs identity to work: one contribution per member per
    // track per day, and the floor counts DISTINCT CONTRIBUTORS. The product promises
    // the manager can never see it. Both are true at once, so identity lives here and
    // no manager-facing read path may reach EngineAnswers or EngineAnswerChips at
    // member grain. Treat that as a schema rule, not an application one.
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A person who answers. Deliberately thin: no name, no email, no link to Users.
    /// A member is not a back-office user and must not become one by accident.
    /// </summary>
    public class EngineMember
    {
        public long Id { get; init; }

        [ForeignKey(nameof(Profile))]
        public uint ProfileId { get; set; }
        public virtual Profile? Profile { get; set; }

        /// <summary>
        /// SHA-256 of the token the app holds. The token itself is never stored, so this
        /// table cannot be read back into a device identifier.
        /// </summary>
        [MaxLength(64)]
        public required string TokenHash { get; set; }

        /// <summary>
        /// The opaque id handed to the engine, e.g. "m-000042". Stable for the member's
        /// life and meaningless outside this database, so a payload carrying it names
        /// nobody.
        /// </summary>
        [MaxLength(30)]
        public required string EngineMemberKey { get; set; }

        public DateTime CreatedAt { get; set; }
        public bool IsDel { get; set; }
    }

    /// <summary>
    /// A member's time on a team, as a period rather than a flag. The floor is a
    /// percentage of ENROLLED members, so the denominator moves when somebody joins or
    /// leaves, and a window has to ask who was enrolled on a given date rather than who
    /// is enrolled now.
    /// </summary>
    public class EngineTeamMembership
    {
        public long Id { get; init; }

        [ForeignKey(nameof(Member))]
        public long MemberId { get; set; }
        public virtual EngineMember? Member { get; set; }

        [ForeignKey(nameof(BusinessUnit))]
        public uint BusinessUnitId { get; set; }
        public virtual BusinessUnit? BusinessUnit { get; set; }

        public DateOnly EffectiveFrom { get; set; }

        /// <summary>Null while the member is still on the team.</summary>
        public DateOnly? EffectiveTo { get; set; }

        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// What the engine needs to know about a team before it can run: when its clock
    /// starts and which weekdays it works.
    /// </summary>
    public class EngineTeamSettings
    {
        public long Id { get; init; }

        [ForeignKey(nameof(BusinessUnit))]
        public uint BusinessUnitId { get; set; }
        public virtual BusinessUnit? BusinessUnit { get; set; }

        /// <summary>
        /// Day 1 of this team's engine clock. Every stored Day is counted from here, so
        /// it is set once and never edited: moving it renumbers history.
        /// </summary>
        public DateOnly StartDate { get; set; }

        /// <summary>
        /// A name from the kit's calendar.json — mon-fri, sun-thu, wed-sun, sat-wed,
        /// tue-sat, every-day. Sunday is 0. Windows count the days a team is ASKED, not
        /// dates, so this decides the second clock.
        /// </summary>
        [MaxLength(30)]
        public string WorkingWeek { get; set; } = "mon-fri";

        /// <summary>False until the team is switched on, so enabling is deliberate.</summary>
        public bool IsEnabled { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public int? UpdatedBy { get; set; }
    }

    /// <summary>
    /// One member's answer on one track on one day: the level from the smiley, and either
    /// chips or free text. Append-only — the record of what was said.
    /// </summary>
    public class EngineAnswer
    {
        public long Id { get; init; }

        [ForeignKey(nameof(BusinessUnit))]
        public uint BusinessUnitId { get; set; }
        public virtual BusinessUnit? BusinessUnit { get; set; }

        [ForeignKey(nameof(Member))]
        public long MemberId { get; set; }
        public virtual EngineMember? Member { get; set; }

        /// <summary>The calendar date the answer was given, in the team's own terms.</summary>
        public DateOnly AnswerDate { get; set; }

        /// <summary>
        /// Days since the team's StartDate, 1-based. A DATE index.
        /// </summary>
        public int Day { get; set; }

        /// <summary>
        /// How many days this team has been asked anything, 1-based. An ASKED-DAY index,
        /// and the one every window counts. Stored beside Day because the two are both
        /// plain integers and the reference implementation confused them five times.
        /// </summary>
        public int AskedDayIndex { get; set; }

        /// <summary>"individual" or "team".</summary>
        [MaxLength(20)]
        public required string Track { get; set; }

        /// <summary>The question the rotation put in front of them that day.</summary>
        public int QuestionKey { get; set; }

        /// <summary>red, orange, yellow or green.</summary>
        [MaxLength(20)]
        public required string Level { get; set; }

        /// <summary>
        /// The member's own words when no chip fitted. Carries a level, casts no vote and
        /// has a neutral locus. The most identifying field in the system: never shown to a
        /// manager, never quoted in a team note.
        /// </summary>
        [MaxLength(2000)]
        public string? FreeText { get; set; }

        public DateTime CreatedAt { get; set; }

        public virtual ICollection<EngineAnswerChip> Chips { get; set; } = new List<EngineAnswerChip>();
    }

    /// <summary>One chip on an answer. Zero to three per answer; each is a vote.</summary>
    public class EngineAnswerChip
    {
        public long Id { get; init; }

        [ForeignKey(nameof(Answer))]
        public long AnswerId { get; set; }
        public virtual EngineAnswer? Answer { get; set; }

        /// <summary>The kit's chip id, e.g. "191-N3". Resolved against the reference version.</summary>
        [MaxLength(50)]
        public required string ChipKey { get; set; }

        /// <summary>The order the member picked them in, 1-3.</summary>
        public int Position { get; set; }
    }
}
