using System.Text.Json;
using System.Text.Json.Serialization;

namespace CloverBackoffice.Services.Implementation
{
    /// <summary>
    /// A scripted demo scenario from the engine's reference side: a roster and, for each of
    /// the team's asked days, what each member answered.
    ///
    /// Test data. It exists so a team can be given ninety days of plausible answers in one
    /// call and the engine can be watched doing its job. Nothing in the product reads it.
    /// </summary>
    public sealed class EngineScenarioFile
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Industry { get; set; }
        public string? Arc { get; set; }

        /// <summary>A name from the kit's calendar.json. Defaults to mon-fri.</summary>
        public string? WorkingWeek { get; set; }

        /// <summary>Dates the scenario covers. 124 on a five-day week for ninety asked days.</summary>
        public int Horizon { get; set; }

        public List<ScenarioMember> Roster { get; set; } = [];

        /// <summary>
        /// Keyed by ASKED-DAY index as a string ("1".."90"), which is the same space the
        /// engine's own script uses: its nth entry meets the nth question.
        /// </summary>
        public Dictionary<string, List<ScenarioEntry>> Script { get; set; } = [];

        /// <summary>When the manager confirms each cycle, and whether the condition was met.</summary>
        public List<ScenarioConfirmation> Confirmations { get; set; } = [];

        public ScenarioDefaultConfirmation? DefaultConfirmation { get; set; }

        public sealed class ScenarioMember
        {
            public string Id { get; set; } = string.Empty;
            public string? Name { get; set; }
        }

        public sealed class ScenarioEntry
        {
            public string Member { get; set; } = string.Empty;

            /// <summary>Absent means the individual track, which is all these scripts carry.</summary>
            public string? Track { get; set; }

            /// <summary>red, orange, yellow or green.</summary>
            public string Smiley { get; set; } = string.Empty;

            public List<string> Chips { get; set; } = [];

            public string? Text { get; set; }
        }

        public sealed class ScenarioConfirmation
        {
            public int CycleIndex { get; set; }
            public int Rung { get; set; } = 1;

            /// <summary>Asked days after delivery. The scenario's own pacing.</summary>
            public int? DayOffset { get; set; }

            public bool WorkedIfMet { get; set; } = true;
        }

        public sealed class ScenarioDefaultConfirmation
        {
            public int? DayOffset { get; set; }
            public bool WorkedIfMet { get; set; } = true;
        }

        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };

        public static async Task<EngineScenarioFile> LoadAsync(string path, CancellationToken ct)
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<EngineScenarioFile>(stream, Options, ct)
                   ?? throw new InvalidOperationException($"'{path}' is not a scenario file.");
        }
    }
}
