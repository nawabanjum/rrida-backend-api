using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.Models
{
    public class FeedbackAnalysisResult
    {
        [Key]
        public int Id { get; set; }

        public DateTime AnalysisDate { get; set; }
        public DateTime SubmissionDate { get; set; }

        public string CategoryName { get; set; }

        public double SadPercentage { get; set; }
        public double HappyPercentage { get; set; }
        public double IndifferentPercentage { get; set; }
        public double AngryPercentage { get; set; }

        public uint BusinessUnitId { get; set; }
    }
}
