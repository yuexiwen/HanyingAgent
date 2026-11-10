using HanyingAgent.Contracts.Memory.Models;
using HanyingAgent.Contracts.Memory.Operations.Requests;

namespace HanyingAgent.Capabilities.Memory;

/// <summary>
/// Defines the internal storage boundary for working memory, not a remote service contract.
/// Implementations must provide atomic mutations and materialized, detached collection snapshots.
/// Business validation, readable states, expiration, ordering and limits belong to the manager.
/// </summary>
public interface IWorkingMemoryStore
{
    /// <summary>
    /// Atomically adds a validated item without replacing an existing ID.
    /// </summary>
    /// <exception cref="InvalidOperationException">The ID already exists.</exception>
    Task AddAsync(MemoryItem item, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the stored item regardless of status or expiration, or null if the ID is unknown.
    /// </summary>
    Task<MemoryItem?> GetAsync(MemoryId memoryId, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically applies a validated full update, preserving Id, Owner, Type and CreatedAt.
    /// UpdatedAt becomes the later of the stored timestamp and the supplied candidate timestamp.
    /// Concurrent updates use last-applied-wins semantics, not optimistic version checks.
    /// A null ExpiresAt clears expiration; expired timestamps are retained as supplied.
    /// </summary>
    /// <exception cref="KeyNotFoundException">The ID is unknown.</exception>
    Task<MemoryItem> UpdateAsync(
        MemoryId memoryId,
        MemoryUpdateRequest update,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken);

    /// <summary>
    /// Atomically deletes the item rather than changing its status.
    /// </summary>
    /// <exception cref="KeyNotFoundException">The ID is unknown.</exception>
    Task DeleteAsync(MemoryId memoryId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns a snapshot for the exact owner, including inactive and expired items.
    /// No ordering or limit is applied; changing the returned collection must not affect storage.
    /// </summary>
    Task<IReadOnlyList<MemoryItem>> ListByOwnerAsync(
        MemoryOwner owner,
        CancellationToken cancellationToken);
}
