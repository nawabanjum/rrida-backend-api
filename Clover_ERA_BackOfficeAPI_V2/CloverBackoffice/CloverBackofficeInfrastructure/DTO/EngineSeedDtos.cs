using System;
using System.Collections.Generic;

namespace CloverBackoffice.Infrastructure.DTO
{
    /// <summary>Seed one team from a scripted demo scenario, so the engine has something to run on.</summary>
    public class EngineSeedRequestDto
    {
        public uint BusinessUnitId { get; set; }

        /// <summary>e.g. "team-platform". List them with GET api/rrida/engine/scenarios.</summary>
        public string ScenarioId { get; set; } = string.Empty;

        /// <summary>
        /// Day 1 of the team's engine clock. Left unset, the scenario is placed so that its
        /// last scripted day lands on today, which is what makes the team look live.
        /// </summary>
        public DateOnly? StartDate { get; set; }

        /// <summary>
        /// Write the manager's confirmations too, on the days the scenario says, so the
        /// cycles ladder and close exactly as the reference host shows them. Off means the
        /// cycles are delivered and never confirmed.
        /// </summary>
        public bool AutoConfirm { get; set; } = true;

        /// <summary>
        /// Required when the team already holds engine data. Deletes that team's answers,
        /// members, confirmations and every result derived from them, then seeds again.
        /// </summary>
        public bool Reset { get; set; }
    }

    /// <summary>What the seed wrote, and what the engine then made of it.</summary>
    public class EngineSeedResultDto
    {
        public uint BusinessUnitId { get; set; }
        public string ScenarioId { get; set; } = string.Empty;
        public string ScenarioName { get; set; } = string.Empty;
        public DateOnly StartDate { get; set; }
        public DateOnly ThroughDate { get; set; }
        public string WorkingWeek { get; set; } = string.Empty;

        public int MembersSeeded { get; set; }
        public int AnswersSeeded { get; set; }
        public int ChipsSeeded { get; set; }
        public int ConfirmationsSeeded { get; set; }

        /// <summary>The replay that followed the seed.</summary>
        public EngineRunResultDto? Run { get; set; }

        /// <summary>Cycle outcomes after the run: closed, failed, running.</summary>
        public Dictionary<string, int> CycleOutcomes { get; set; } = new();
    }

    /// <summary>One scenario available to seed from.</summary>
    public class EngineScenarioDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Industry { get; set; }
        public int Roster { get; set; }
        public string? WorkingWeek { get; set; }
        public int ScriptedDays { get; set; }
        public int Horizon { get; set; }
        public string? Arc { get; set; }
    }
}
