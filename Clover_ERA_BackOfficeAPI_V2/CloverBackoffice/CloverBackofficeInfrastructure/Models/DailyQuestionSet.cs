using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.Models
{
    public class DailyQuestionSet
    {
        public uint Id { get; init; }

        public required uint VersionNumber { get; set; }

        [MaxLength(240)]
        public required string Name { get; set; }

        public virtual ICollection<DailyQuestionSpecification>? Specifications { get; set; }

        public virtual required ContentCulture ContentCulture { get; set; }

        public virtual required DailyQuestionSetGroup DailyQuestionSetGroup { get; set; }

    }
}
