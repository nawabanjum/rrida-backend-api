using System;

namespace CloverBackoffice.Infrastructure.DTO
{
    /// <summary>What one replay of one team did.</summary>
    public class EngineRunResultDto
    {
        public uint BusinessUnitId { get; set; }
        public DateOnly StartDate { get; set; }
        public DateOnly Through { get; set; }

        /// <summary>Dates covered, from the team's start.</summary>
        public int Days { get; set; }

        /// <summary>Days the team was actually asked — what every window counts.</summary>
        public int AskedDays { get; set; }

        public int Enrolled { get; set; }
        public int StateRecords { get; set; }
        public int Deliveries { get; set; }
        public int Resolutions { get; set; }

        /// <summary>Null while the team is still calibrating.</summary>
        public string? State { get; set; }

        public string? ActiveCycleKey { get; set; }

        /// <summary>Which thresholds produced this run.</summary>
        public string ConfigId { get; set; } = string.Empty;

        public int ElapsedMs { get; set; }
    }

    /// <summary>A team's engine settings: when its clock starts and which days it works.</summary>
    public class EngineTeamSettingsDto
    {
        public uint BusinessUnitId { get; set; }

        /// <summary>Day 1 of the team's engine clock. Fixed once answers exist.</summary>
        public DateOnly StartDate { get; set; }

        /// <summary>A name from the kit's calendar.json: mon-fri, sun-thu, wed-sun, sat-wed, tue-sat, every-day.</summary>
        public string WorkingWeek { get; set; } = "mon-fri";

        public bool IsEnabled { get; set; }
    }

    /// <summary>A manager confirming the action was taken.</summary>
    public class EngineConfirmRequestDto
    {
        public uint BusinessUnitId { get; set; }
        public string CycleKey { get; set; } = string.Empty;

        /// <summary>
        /// The manager's own observation. It must be able to fail honestly: performing the
        /// action and changing nothing is a false, not a true.
        /// </summary>
        public bool WorkedIfMet { get; set; }
    }

    /// <summary>A manager declining a stated contraindication.</summary>
    public class EngineDeclineRequestDto
    {
        public uint BusinessUnitId { get; set; }
        public string CycleKey { get; set; } = string.Empty;
        public string? Reason { get; set; }
    }

    /// <summary>"I'll do it" on the move in front of the manager.</summary>
    public class EngineAcknowledgeRequestDto
    {
        public uint BusinessUnitId { get; set; }
        public string CycleKey { get; set; } = string.Empty;
    }
}
