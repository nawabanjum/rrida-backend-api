using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.Models
{
    public class SeedingHistory
    {
        public uint Id { get; init; }
        public required ushort MajorVersionNumber { get; init; }
        public required ushort MinorVersionNumber { get; init; }
    }
}
