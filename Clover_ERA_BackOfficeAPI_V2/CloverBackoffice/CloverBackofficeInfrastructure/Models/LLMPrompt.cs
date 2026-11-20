using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.Models
{
    public class LLMPrompt
    {
        [Key]
        public int Id { get; set; }
        public string InitialPrompt { get; set; }
    }
}
