using HanyingAgent.Capabilities.Memory;
using HanyingAgent.Contracts.Memory.Models;
using HanyingAgent.Contracts.Memory.Operations.Requests;

namespace HanyingAgent.Infrastructure.Persistence;

public sealed class InMemoryWorkingMemoryStore : IWorkingMemoryStore
{
    private readonly object _gate = new();
    private readonly Dictionary<MemoryId, MemoryItem> _items = new();

    public Task AddAsync(MemoryItem item, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(item.Id.Value, nameof(item));
        ArgumentNullException.ThrowIfNull(item.Owner);

        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_items.TryAdd(item.Id, item))
            {
                throw new InvalidOperationException($"Memory '{item.Id.Value}' already exists.");
            }
        }

        return Task.CompletedTask;
    }

    public Task<MemoryItem?> GetAsync(MemoryId memoryId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(memoryId.Value, nameof(memoryId));

        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _items.TryGetValue(memoryId, out var item);
            return Task.FromResult<MemoryItem?>(item);
        }
    }

    public Task<MemoryItem> UpdateAsync(
        MemoryId memoryId,
        MemoryUpdateRequest update,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(memoryId.Value, nameof(memoryId));
        ArgumentNullException.ThrowIfNull(update);

        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_items.TryGetValue(memoryId, out var existing))
            {
                throw new KeyNotFoundException($"Memory '{memoryId.Value}' does not exist.");
            }

            var updated = existing with
            {
                Content = update.Content,
                Source = update.Source,
                Confidence = update.Confidence,
                Importance = update.Importance,
                Status = update.Status,
                UpdatedAt = updatedAt > existing.UpdatedAt ? updatedAt : existing.UpdatedAt,
                ExpiresAt = update.ExpiresAt
            };

            _items[memoryId] = updated;
            return Task.FromResult(updated);
        }
    }

    public Task DeleteAsync(MemoryId memoryId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(memoryId.Value, nameof(memoryId));

        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_items.Remove(memoryId))
            {
                throw new KeyNotFoundException($"Memory '{memoryId.Value}' does not exist.");
            }
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MemoryItem>> ListByOwnerAsync(
        MemoryOwner owner,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(owner);

        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<MemoryItem> snapshot = _items.Values
                .Where(item => item.Owner == owner)
                .ToArray();

            return Task.FromResult(snapshot);
        }
    }
}
