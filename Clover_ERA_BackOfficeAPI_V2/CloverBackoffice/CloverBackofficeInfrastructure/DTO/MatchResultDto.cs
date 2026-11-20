using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class MatchResultDto
    {
        public bool IsMatch { get; set; }
        public double MatchPercentage { get; set; }
        public int MatchedFields { get; set; }
        public int TotalFieldsWithValues { get; set; }
        public string? Message { get; set; }
        public UserSystemDetailsDto? MatchingRecord { get; set; }
        public string? OneSignalPlayerId { get; set; }
    }
}
