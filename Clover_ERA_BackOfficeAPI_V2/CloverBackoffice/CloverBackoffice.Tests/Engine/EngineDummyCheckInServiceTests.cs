using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Services.Implementation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CloverBackoffice.Tests.Engine
{
    public class EngineDummyCheckInServiceTests
    {
        private sealed class FixedOptions(EngineDummyCheckInSettings value) : IOptionsMonitor<EngineDummyCheckInSettings>
        {
            public EngineDummyCheckInSettings CurrentValue => value;
            public EngineDummyCheckInSettings Get(string? name) => value;
            public IDisposable? OnChange(Action<EngineDummyCheckInSettings, string?> listener) => null;
        }

        // Disabled or unconfigured runs return before touching the database or the
        // check-in service, so neither is needed here.
        private static EngineDummyCheckInService Service(EngineDummyCheckInSettings settings) =>
            new(null!, null!, null!, null!, new FixedOptions(settings), NullLogger<EngineDummyCheckInService>.Instance);

        [Fact]
        public async Task Disabled_does_nothing()
        {
            var result = await Service(new EngineDummyCheckInSettings { Enabled = false, BusinessUnitIds = "307,308" }).RunAsync();

            Assert.False(result.Enabled);
            Assert.Empty(result.Teams);
            Assert.Contains("disabled", result.Message);
        }

        [Fact]
        public async Task Enabled_without_team_ids_does_nothing()
        {
            var result = await Service(new EngineDummyCheckInSettings { Enabled = true, BusinessUnitIds = " , " }).RunAsync();

            Assert.True(result.Enabled);
            Assert.Empty(result.Teams);
            Assert.Contains("No team ids", result.Message);
        }

        [Theory]
        [InlineData("307,308, 309", new uint[] { 307, 308, 309 })]
        [InlineData("307,,abc,307,0", new uint[] { 307 })]
        [InlineData("", new uint[0])]
        [InlineData(null, new uint[0])]
        public void ParseIds_keeps_valid_distinct_ids(string? csv, uint[] expected) =>
            Assert.Equal(expected, EngineDummyCheckInService.ParseIds(csv));

        [Fact]
        public void TokenFor_is_stable_per_team_and_member()
        {
            Assert.Equal("rrida-dummy-307-sam-o-neil", EngineDummyCheckInService.TokenFor(307, " Sam O'Neil "));
            Assert.Equal(EngineDummyCheckInService.TokenFor(307, "Sam"), EngineDummyCheckInService.TokenFor(307, "sam"));
            Assert.NotEqual(EngineDummyCheckInService.TokenFor(307, "Sam"), EngineDummyCheckInService.TokenFor(308, "Sam"));
        }

        [Fact]
        public void Shipped_workbook_has_eight_usable_team_sheets()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            string? path = null;
            while (dir is not null && path is null)
            {
                var candidate = Path.Combine(dir.FullName, "CloverBackoffice.API", "ExcelFiles", "RRIDA_Engine_Test_Data_8_teams.xlsx");
                if (File.Exists(candidate)) path = candidate;
                dir = dir.Parent;
            }
            Assert.NotNull(path);

            using var stream = File.OpenRead(path!);
            var sheets = EngineWorkbookReader.Read(stream).Where(s => s.Error is null && s.Rows.Count > 0).ToList();

            Assert.Equal(8, sheets.Count);
            Assert.All(sheets, s => Assert.NotEmpty(s.Rows.Select(r => r.Member).Distinct()));
        }
    }
}
