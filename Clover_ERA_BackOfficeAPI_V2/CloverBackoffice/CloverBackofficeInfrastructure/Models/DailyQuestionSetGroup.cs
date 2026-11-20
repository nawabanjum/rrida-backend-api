using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.Models
{
    public class DailyQuestionSetGroup
    {
        public required uint Id { get; init; }
        public required DailyQuestionSetType DailyQuestionSetType { get; set; }
    }
}
