using HanyingAgent.Contracts.Memory.Models;
using HanyingAgent.Contracts.Memory.Operations.Requests;

namespace HanyingAgent.Contracts.Memory.Operations;

/// <summary>
/// Manages working memory; successful mutations must be visible to subsequent reads.
/// </summary>
public interface IWorkingMemoryManager : IMemoryWriter
{
    /// <summary>
    /// Returns Active or Confirmed, unexpired memories for the exact owner, newest updated first,
    /// without content search or relevance scoring.
    /// </summary>
    Task<IReadOnlyList<MemoryItem>> ListRecentAsync(
        MemoryReadRequest request,
        CancellationToken cancellationToken);
}
