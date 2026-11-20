using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.Models
{
    public class DailyQuestionSpecification
    {
        public uint Id { get; init; }

        [ForeignKey(nameof(DailyQuestionSetGroup))]
        public required uint DailyQuestionSetGroupId { get; set; }

        public virtual required DailyQuestionSetGroup DailyQuestionSetGroup { get; set; }

        public required DailyQuestionSet DailyQuestionSet { get; set; }

        public required CloverCategory CloverCategory { get; set; }

        public required ushort Ordinal { get; set; }

        [ForeignKey(nameof(ContentCulture))]
        public required uint ContentCultureId { get; set; }

        public virtual required ContentCulture ContentCulture { get; set; }

        public virtual required DailyQuestion Question { get; set; }

        public virtual required ICollection<DailyReason> Reasons { get; set; }

    }
}
