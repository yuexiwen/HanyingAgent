using HanyingAgent.Contracts.Memory.Operations.Requests;
using HanyingAgent.Contracts.Memory.Operations.Responses;

namespace HanyingAgent.Contracts.Memory.Operations;

/// <summary>
/// Manages authoritative long-term memories; asynchronous indexing may delay search visibility.
/// </summary>
public interface ILongTermMemoryManager : IMemoryWriter
{
    Task<IReadOnlyList<MemorySearchResult>> SearchAsync(
        MemorySearchRequest query,
        CancellationToken cancellationToken);
}
