using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.BusinessModel
{
    public class AppSettings
    {
        public string LogFolder { get; set; } = string.Empty;
        public string LogFilePath { get; set; } = string.Empty;
        public int ServiceSleep { get; set; }
        public int StateAnalysisDays { get; set; }
    }
}
