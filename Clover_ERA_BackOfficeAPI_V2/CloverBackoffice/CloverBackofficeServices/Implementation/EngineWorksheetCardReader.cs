using System.Text.Json;
using CloverBackoffice.Infrastructure.DTO;

namespace CloverBackoffice.Services.Implementation
{
    /// <summary>Reads the stored worksheet card back. A row written before it existed has none.</summary>
    public static class EngineWorksheetCardReader
    {
        public static EngineWorksheetCard? Read(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                return JsonSerializer.Deserialize<EngineWorksheetCard>(json);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
