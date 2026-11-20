using System.Collections.Generic;
using System.Threading;
using CloverBackoffice.Infrastructure.DTO;

namespace CloverBackoffice.Services.Interfaces
{
    /// <summary>
    /// EranCoach chat orchestration: selects the model, loads the coach system prompt,
    /// and streams the reply back token by token. Phase 1 is a plain question -> answer
    /// coach (no retrieval tools, no team context, no memory). Those layer on later.
    /// </summary>
    public interface IEranCoachChatService
    {
        // portfolioContext: the Portfolio View block (IPortfolioViewService), already scope-checked
        // by the caller; used when request.Portfolio is true, in place of any team.
        IAsyncEnumerable<string> StreamReplyAsync(EranCoachChatRequestDto request, int? managerUserId = null, string? portfolioContext = null, CancellationToken ct = default);
    }
}
