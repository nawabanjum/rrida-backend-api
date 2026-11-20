using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class TeamsSectionDto
    {
        // Overall teams in scope (licensed + unlicensed).
        public int TotalTeams { get; set; }

        // Teams that currently hold an active license (subset of TotalTeams).
        public int ActiveLicensedTeams { get; set; }

        // How many of ActiveLicensedTeams hold a complimentary license rather
        // than a purchased one (subset of ActiveLicensedTeams, not an addition
        // to it).
        public int ComplimentaryLicenseCount { get; set; }
    }
}
