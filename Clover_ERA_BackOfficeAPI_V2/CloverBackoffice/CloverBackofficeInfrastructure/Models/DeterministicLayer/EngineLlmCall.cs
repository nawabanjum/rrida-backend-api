using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CloverBackoffice.Infrastructure.Models.DeterministicLayer
{
    /// <summary>
    /// One call to Eran, kept in full.
    ///
    /// Eran's output is shown to managers and to teams, so being able to say exactly what
    /// was generated, from what, and by which model is worth the storage. It is also the
    /// only way to answer "why did it say that" about a sentence nobody wrote.
    /// </summary>
    public class EngineLlmCall
    {
        public long Id { get; init; }

        [ForeignKey(nameof(BusinessUnit))]
        public uint BusinessUnitId { get; set; }
        public virtual BusinessUnit? BusinessUnit { get; set; }

        /// <summary>The feed item this phrased, when it phrased one.</summary>
        [MaxLength(20)]
        public string? FeedKey { get; set; }

        /// <summary>daily, outcome, note, weekly, monthly, escalation, cross_team.</summary>
        [MaxLength(50)]
        public required string PromptType { get; set; }

        public int Day { get; set; }

        public required string SystemPrompt { get; set; }

        /// <summary>The payload as sent: what the layer had already decided.</summary>
        public required string UserContent { get; set; }

        public string? Response { get; set; }

        [MaxLength(200)]
        public string? Model { get; set; }

        public int? DurationMs { get; set; }

        /// <summary>Set when the call failed; the feed item then keeps no prose and can be retried.</summary>
        [MaxLength(1000)]
        public string? Error { get; set; }

        /// <summary>The guard's verdict on <see cref="Response"/>: passed, retried, passed_on_retry or fallback.</summary>
        [MaxLength(30)]
        public string? GuardOutcome { get; set; }

        /// <summary>The rule labels it broke, e.g. "a count of people; system vocabulary". Labels only.</summary>
        [MaxLength(500)]
        public string? GuardRules { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
