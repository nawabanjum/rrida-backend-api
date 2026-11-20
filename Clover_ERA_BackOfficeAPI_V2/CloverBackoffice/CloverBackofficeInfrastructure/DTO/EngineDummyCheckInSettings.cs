using System;
using System.Collections.Generic;

namespace CloverBackoffice.Infrastructure.DTO
{
    /// <summary>
    /// "EngineDummyCheckIn" in appsettings: TEST DATA. Answers today's engine check-in for
    /// a fixed set of test teams, picking answers at random from the test-data workbook,
    /// through the same enrol → today → answer flow the member app uses.
    ///
    /// Does nothing unless Enabled is true AND the team's id is in BusinessUnitIds. The
    /// wake-up cadence is under "JobSchedules:EngineDummyCheckIn".
    /// </summary>
    public class EngineDummyCheckInSettings
    {
        public bool Enabled { get; set; }

        /// <summary>Comma-separated team (business unit) ids, e.g. "307,308,309".</summary>
        public string BusinessUnitIds { get; set; } = string.Empty;

        /// <summary>The workbook, relative to the API's content root or absolute.</summary>
        public string ExcelFilePath { get; set; } = "ExcelFiles/RRIDA_Engine_Test_Data_8_teams.xlsx";

        /// <summary>
        /// Chance (0-100) that each workbook member checks in on a given day. 100 = every
        /// member answers every asked day.
        /// </summary>
        public int ParticipationPercent { get; set; } = 100;
    }

    /// <summary>What one run of the dummy check-in did.</summary>
    public class EngineDummyCheckInResultDto
    {
        public DateOnly Date { get; set; }
        public bool Enabled { get; set; }
        public string? Message { get; set; }
        public List<EngineDummyCheckInTeamResultDto> Teams { get; set; } = new();
    }

    public class EngineDummyCheckInTeamResultDto
    {
        public uint BusinessUnitId { get; set; }
        public string? TeamName { get; set; }
        public string? SheetName { get; set; }

        /// <summary>Done, NotAskedToday, NotFound, NoJoinCode or NoSheet.</summary>
        public string Status { get; set; } = "Done";

        public int Members { get; set; }
        public int CheckInsSubmitted { get; set; }
        public int AlreadyAnswered { get; set; }
        public int SkippedByParticipation { get; set; }
        public List<string> Errors { get; set; } = new();
    }
}
