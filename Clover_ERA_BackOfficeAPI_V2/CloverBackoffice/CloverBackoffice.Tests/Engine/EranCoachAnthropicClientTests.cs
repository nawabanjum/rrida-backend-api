using System.Net;
using System.Text;
using System.Text.Json;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Services.Implementation;
using CloverBackoffice.Services.Interfaces;
using Moq;
using Xunit;

namespace CloverBackoffice.Tests.Engine
{
    /// <summary>
    /// Claude Opus 5.5 (and Opus 4.7+, Sonnet 5+) reject temperature/top_p ("`top_p` is deprecated
    /// for this model.") and require thinking blocks to be sent back unchanged in a tool loop.
    /// </summary>
    public class EranCoachAnthropicClientTests
    {
        [Theory]
        [InlineData("claude-opus-5-5", false, false)]
        [InlineData("claude-opus-4-8", false, false)]
        [InlineData("claude-opus-4-7", false, false)]
        [InlineData("claude-sonnet-5-5", false, false)]
        [InlineData("claude-sonnet-5", false, false)]
        [InlineData("claude-haiku-5-5", false, false)]
        [InlineData("claude-fable-5-1", false, false)]
        [InlineData("claude-sonnet-4-6", true, false)]
        [InlineData("claude-opus-4-6", true, false)]
        [InlineData("claude-sonnet-4-5-20250929", true, true)]
        [InlineData("claude-haiku-4-5", true, true)]
        [InlineData("claude-sonnet-4-20250514", true, true)]
        [InlineData("claude-3-7-sonnet-20250219", true, true)]
        [InlineData("claude-3-5-sonnet-20241022", true, false)]
        [InlineData("us.anthropic.claude-opus-4-6-v1:0", true, false)]
        [InlineData("gpt-4o", false, false)]
        public void Sampling_and_thinking_budgets_only_go_to_models_that_take_them(string model, bool sampling, bool budget)
        {
            Assert.Equal(sampling, AnthropicModelRules.AcceptsSampling(model));
            Assert.Equal(budget, AnthropicModelRules.UsesThinkingBudget(model));
        }

        [Fact]
        public async Task An_Opus_5_5_turn_sends_no_sampling_and_its_thinking_goes_back_unchanged()
        {
            var handler = new SseHandler();
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler, disposeHandler: false));
            var client = new EranCoachAnthropicClient(factory.Object);
            var model = new ModelConfiguration
            {
                ModelName = "claude-opus-5-5", Endpoint = "https://example.services.ai.azure.com/anthropic/v1/messages",
                ApiKey = "k", MaxTokens = 4096, Temperature = 0.7, TopP = 0.9
            };
            var system = new List<CoachSystemBlock> { new("You are Eran.", "1h") };
            var question = new List<CoachMessage> { new("user", new List<CoachBlock> { new CoachTextBlock("How is my team?") }) };

            var events = new List<CoachStreamEvent>();
            await foreach (var e in client.StreamTurnAsync(system, question, new List<object>(), model)) events.Add(e);

            using (var sent = JsonDocument.Parse(handler.Bodies[0]))
            {
                Assert.False(sent.RootElement.TryGetProperty("top_p", out _));
                Assert.False(sent.RootElement.TryGetProperty("temperature", out _));
            }

            var turn = Assert.Single(events.OfType<CoachAssistantContent>());
            Assert.Collection(turn.Blocks,
                b => Assert.Equal(new CoachThinkingBlock("", "sig-1"), b),
                b => Assert.Equal(new CoachTextBlock("Let me check."), b),
                b => Assert.Equal(new CoachToolUseBlock("tu-1", "get_team_report", "{\"unitId\":7}"), b));
            Assert.Equal("tool_use", events.OfType<CoachTurnEnded>().Single().StopReason);

            // The tool loop's next request carries the assistant turn as the model wrote it.
            var next = new List<CoachMessage>(question)
            {
                new("assistant", turn.Blocks),
                new("user", new List<CoachBlock> { new CoachToolResultBlock("tu-1", "{}") })
            };
            await foreach (var _ in client.StreamTurnAsync(system, next, new List<object>(), model)) { }

            using var second = JsonDocument.Parse(handler.Bodies[1]);
            var assistant = second.RootElement.GetProperty("messages")[1].GetProperty("content");
            Assert.Equal("thinking", assistant[0].GetProperty("type").GetString());
            Assert.Equal("sig-1", assistant[0].GetProperty("signature").GetString());
            Assert.False(assistant[0].TryGetProperty("cache_control", out _));
            Assert.Equal("tool_use", assistant[2].GetProperty("type").GetString());
        }

        /// <summary>Answers every request with one streamed turn: thinking, text, then a tool call.</summary>
        private sealed class SseHandler : HttpMessageHandler
        {
            public List<string> Bodies { get; } = new();

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                Bodies.Add(await request.Content!.ReadAsStringAsync(ct));
                var sse = string.Join("\n", new[]
                {
                    """data: {"type":"message_start","message":{"id":"m1"}}""",
                    """data: {"type":"content_block_start","index":0,"content_block":{"type":"thinking","thinking":"","signature":""}}""",
                    """data: {"type":"content_block_delta","index":0,"delta":{"type":"signature_delta","signature":"sig-1"}}""",
                    """data: {"type":"content_block_stop","index":0}""",
                    """data: {"type":"content_block_start","index":1,"content_block":{"type":"text","text":""}}""",
                    """data: {"type":"content_block_delta","index":1,"delta":{"type":"text_delta","text":"Let me check."}}""",
                    """data: {"type":"content_block_stop","index":1}""",
                    """data: {"type":"content_block_start","index":2,"content_block":{"type":"tool_use","id":"tu-1","name":"get_team_report","input":{}}}""",
                    """data: {"type":"content_block_delta","index":2,"delta":{"type":"input_json_delta","partial_json":"{\"unitId\":7}"}}""",
                    """data: {"type":"content_block_stop","index":2}""",
                    """data: {"type":"message_delta","delta":{"stop_reason":"tool_use"}}""",
                    """data: {"type":"message_stop"}""",
                    ""
                });
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(sse, Encoding.UTF8, "text/event-stream") };
            }
        }
    }
}
