using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.Models
{
    public class Subscription
    {
        public uint Id { get; init; }
        public required DateTime EndDate { get; set; }
        public required DateTime StartDate { get; set; }
        public required SubscriptionType SubscriptionType { get; set; }
        public virtual required Profile Profile { get; init; }
        public virtual required BusinessUnit BusinessUnit { get; init; }
    }
}
