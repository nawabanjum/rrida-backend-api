using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CloverBackoffice.Infrastructure.Models
{
    [Table("FeedBack")]
    public class Feedback
    {
        [Key]
        [Column("Id")]
        public long Id { get; set; }

        [Column("FeedbackText")]
        public string? FeedbackText { get; set; }

        [Column("UserSubmittedAtUtc")]
        public DateTime SubmittedAt { get; set; }
    }
}
