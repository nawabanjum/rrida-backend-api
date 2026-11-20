using System;
using System.Collections.Generic;

namespace CloverBackoffice.Infrastructure.DTO
{
    /// <summary>What a reference-data load wrote.</summary>
    public class RefDataLoadResultDto
    {
        public long VersionId { get; set; }
        public string SpecVersion { get; set; } = string.Empty;
        public string? KitVersion { get; set; }
        public string? DataVersion { get; set; }
        public string? BaselineTag { get; set; }
        public string KitPath { get; set; } = string.Empty;
        public DateTime LoadedAt { get; set; }

        /// <summary>Rows written, by table.</summary>
        public Dictionary<string, int> Counts { get; set; } = new();

        /// <summary>The previous active version, now superseded but untouched.</summary>
        public long? SupersededVersionId { get; set; }
    }

    /// <summary>The active reference-data version.</summary>
    public class RefDataVersionDto
    {
        public long VersionId { get; set; }
        public string SpecVersion { get; set; } = string.Empty;
        public string? KitVersion { get; set; }
        public string? DataVersion { get; set; }
        public string? BaselineTag { get; set; }
        public string TallyKey { get; set; } = string.Empty;
        public int RunMinutesCap { get; set; }
        public DateTime LoadedAt { get; set; }
        public Dictionary<string, int> Counts { get; set; } = new();
    }

    /// <summary>A question with its three chip sets — what the check-in screen needs.</summary>
    public class RefQuestionDto
    {
        public int QuestionKey { get; set; }
        public string Text { get; set; } = string.Empty;
        public string Element { get; set; } = string.Empty;
        public string Track { get; set; } = string.Empty;

        /// <summary>Keyed by polarity: negative, indifferent, positive. Authored order.</summary>
        public Dictionary<string, List<RefChipDto>> Chips { get; set; } = new();
    }

    /// <summary>
    /// One chip as the check-in shows it. The routing columns and the locus tag stay on the
    /// server: they are what the engine decides with, and neither is the member's business.
    /// </summary>
    public class RefChipDto
    {
        public string ChipKey { get; set; } = string.Empty;
        public int Ordinal { get; set; }
        public string Text { get; set; } = string.Empty;
    }
}
