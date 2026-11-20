using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.Models
{
    public class ContentCulture
    {
        public uint Id { get; init; }

        [MaxLength(5)]
        public required string CultureName { get; init; }
    }
}
