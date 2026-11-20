using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class DailySentimentAnalysisDto
    {
        public DateTime AnalysisDate { get; set; }
        public double SadPercentage { get; set; }
        public double HappyPercentage { get; set; }
        public double IndifferentPercentage { get; set; }
        public double AngryPercentage { get; set; }
    }
}
