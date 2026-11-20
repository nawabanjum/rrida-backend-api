using System.Runtime.CompilerServices;
using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Services.Implementation;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CloverBackoffice.Tests.Engine
{
    /// <summary>
    /// Eran Chat on a deterministic-layer team goes through the guard before the manager sees any
    /// of it (the brief, section 3.5); every other team streams exactly as before.
    /// </summary>
    public class EranCoachChatGuardTests
    {
        private const uint Unit = 777;
        private const string Action = "Ask one person what slows them down.";
        private const string Bad = "Three people said the handoffs are stalling.";
        private const string Clean = "Most of the team says the handoffs are stalling. Ask one person what slows them down.";

        [Fact]
        public async Task A_breach_is_retried_and_only_the_clean_reply_is_sent_and_remembered()
        {
            var (service, memory, turns) = Build(engineTeam: true, [Bad], [Clean]);

            var sent = await Collect(service);

            Assert.Equal(new[] { Clean }, sent);
            Assert.Equal(2, turns.Count);
            var retryTurn = turns[1].Last();
            Assert.Equal("user", retryTurn.Role);
            var feedback = ((CoachTextBlock)retryTurn.Content[0]).Text;
            Assert.Contains("Your last reply broke these rules: a count of people.", feedback);
            Assert.DoesNotContain("Three people", feedback);
            memory.Verify(m => m.SaveAsync(5, Unit, "How is my team?", Clean, It.IsAny<ModelConfiguration>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Two_breaches_send_the_chat_card_and_nothing_Eran_wrote()
        {
            var (service, memory, _) = Build(engineTeam: true, [Bad], [Bad]);

            var sent = await Collect(service);

            var card = EranGuardFallback.ForChat(Action, "How is my team?");
            Assert.Equal(new[] { card }, sent);
            Assert.DoesNotContain(sent, s => s.Contains("Three people"));
            memory.Verify(m => m.SaveAsync(5, Unit, "How is my team?", card, It.IsAny<ModelConfiguration>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task A_team_not_on_the_engine_still_streams_live_and_unchecked()
        {
            var (service, _, _) = Build(engineTeam: false, ["Three ", "people said so."]);

            var sent = await Collect(service);

            Assert.Equal(new[] { "Three ", "people said so." }, sent);
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private static async Task<List<string>> Collect(EranCoachChatService service)
        {
            var request = new EranCoachChatRequestDto
            {
                UnitId = Unit,
                Messages = [new EranCoachChatMessageDto { Role = "user", Content = "How is my team?" }]
            };
            var sent = new List<string>();
            await foreach (var chunk in service.StreamReplyAsync(request, 5)) sent.Add(chunk);
            return sent;
        }

        private static (EranCoachChatService, Mock<IEranCoachMemoryService>, List<List<CoachMessage>>) Build(
            bool engineTeam, params string[][] replies)
        {
            var queue = new Queue<string[]>(replies);
            var turns = new List<List<CoachMessage>>();
            var client = new Mock<IEranCoachAnthropicClient>();
            client.Setup(c => c.StreamTurnAsync(It.IsAny<IReadOnlyList<CoachSystemBlock>>(), It.IsAny<IReadOnlyList<CoachMessage>>(),
                    It.IsAny<IReadOnlyList<object>?>(), It.IsAny<ModelConfiguration>(), It.IsAny<CancellationToken>()))
                .Returns((IReadOnlyList<CoachSystemBlock> _, IReadOnlyList<CoachMessage> m, IReadOnlyList<object>? _, ModelConfiguration _, CancellationToken _) =>
                {
                    turns.Add(m.ToList());
                    return Stream(queue.Dequeue());
                });

            var models = new Mock<IDocumentEmbeddingRepository>();
            models.Setup(m => m.GetAllModelsAsync(It.IsAny<int?>()))
                .ReturnsAsync([new ModelConfiguration { ModelName = "claude-sonnet-test", IsSelected = true }]);

            var tools = new Mock<IEranCoachTools>();
            tools.SetupGet(t => t.Definitions).Returns(new List<object>());
            tools.SetupProperty(t => t.CurrentUnitId);

            var engine = new Mock<IEngineChatContextService>();
            engine.Setup(e => e.BuildAsync(Unit, 5, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(engineTeam
                    ? new EngineChatContext("## THIS TEAM IS ON THE DETERMINISTIC LAYER", 12, new List<EngineChatTurn>())
                    {
                        Situation = $"The reading on this team is drift. The action reads: \"{Action}\" They have not confirmed it yet.",
                        ActionText = Action
                    }
                    : null);

            var tokens = new Mock<ITokenReaderService>();
            tokens.Setup(t => t.GetCurrentUserAsync()).ReturnsAsync(new User { UserGroupId = 2 });
            var access = new Mock<IUnitAccessService>();
            access.Setup(a => a.GetAccessibleUnitIdsAsync(It.IsAny<User>(), It.IsAny<CancellationToken>())).ReturnsAsync([Unit]);
            var env = new Mock<IWebHostEnvironment>();
            env.SetupGet(e => e.ContentRootPath).Returns(ApiRoot());
            var memory = new Mock<IEranCoachMemoryService>();

            var service = new EranCoachChatService(models.Object, client.Object, tools.Object, new Mock<IEranCoachTeamContextService>().Object,
                memory.Object, engine.Object, tokens.Object, access.Object, env.Object, NullLogger<EranCoachChatService>.Instance);
            return (service, memory, turns);
        }

        private static async IAsyncEnumerable<CoachStreamEvent> Stream(string[] deltas)
        {
            foreach (var d in deltas)
            {
                await Task.Yield();
                yield return new CoachTextDelta(d);
            }
            yield return new CoachTurnEnded("end_turn");
        }

        private static string ApiRoot([CallerFilePath] string source = "")
        {
            for (var dir = new DirectoryInfo(Path.GetDirectoryName(source)!); dir is not null; dir = dir.Parent)
                if (Directory.Exists(Path.Combine(dir.FullName, "CloverBackoffice.API")))
                    return Path.Combine(dir.FullName, "CloverBackoffice.API");
            throw new DirectoryNotFoundException("CloverBackoffice.API not found");
        }
    }
}
