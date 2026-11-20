using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.Models
{
    public class DailyMicroAction
    {
        public uint Id { get; init; }

        [MaxLength(1000)]
        public required string Text { get; set; }

        [ForeignKey(nameof(ContentCulture))]
        public required uint ContentCultureId { get; set; }

        public virtual required ContentCulture ContentCulture { get; set; }

        [ForeignKey(nameof(Reason))]
        public required uint ReasonId { get; set; }

        public virtual required DailyReason Reason { get; set; }
        
        //  Soft delete flag
       // public bool? IsDel { get; set; } = false;
    }
}
