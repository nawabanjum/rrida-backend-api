using CloverBackoffice.Services.Implementation;
using Xunit;

namespace CloverBackoffice.Tests.Engine
{
    /// <summary>D31's outward fields, read off a daily payload for the board's card (all six or none).</summary>
    public class EngineOutwardConditionTests
    {
        [Fact]
        public void The_six_fields_are_read_from_the_daily_payload()
        {
            const string payload = """
                {"situation":"action_reminder","outward_condition":"Tooling outside the team keeps failing",
                 "outward_strength":"came through clearly, from more than one direction","outward_days_above":4,
                 "outward_move":"Raise the tooling with the platform lead.","outward_goes_to":"the platform lead",
                 "outward_how_you_will_know":"The platform lead names a date."}
                """;
            var outward = EngineManagerService.OutwardFrom(7, 21, payload);

            Assert.NotNull(outward);
            Assert.Equal(21, outward!.Day);
            Assert.Equal("Tooling outside the team keeps failing", outward.Condition);
            Assert.Equal(4, outward.DaysAbove);
            Assert.Equal("the platform lead", outward.GoesTo);
            Assert.Equal("The platform lead names a date.", outward.HowYouWillKnow);
        }

        [Theory]
        [InlineData("""{"situation":"nothing_cleared"}""")]
        [InlineData("""{"outward_condition":null,"outward_move":null}""")]
        [InlineData("[]")]
        [InlineData("")]
        public void No_condition_means_no_card(string payload) =>
            Assert.Null(EngineManagerService.OutwardFrom(7, 3, payload));

        [Fact]
        public void A_condition_without_a_registered_move_still_shows()
        {
            var outward = EngineManagerService.OutwardFrom(7, 3, """{"outward_condition":"x","outward_move":null,"outward_days_above":null}""");
            Assert.NotNull(outward);
            Assert.Null(outward!.Move);
            Assert.Null(outward.DaysAbove);
        }
    }
}
