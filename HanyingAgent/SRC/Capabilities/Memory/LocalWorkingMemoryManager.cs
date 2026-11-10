using HanyingAgent.Contracts.Memory.Models;
using HanyingAgent.Contracts.Memory.Operations;
using HanyingAgent.Contracts.Memory.Operations.Requests;

namespace HanyingAgent.Capabilities.Memory;

public sealed class LocalWorkingMemoryManager : IWorkingMemoryManager
{
    private readonly IWorkingMemoryStore _store;
    private readonly TimeProvider _timeProvider;

    public LocalWorkingMemoryManager(IWorkingMemoryStore store, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _store = store;
        _timeProvider = timeProvider;
    }

    public async Task<MemoryItem> WriteAsync(
        MemoryWriteRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Owner);
        if (!Enum.IsDefined(request.Type))
        {
            throw new ArgumentOutOfRangeException(nameof(request.Type), request.Type, "Unknown memory type.");
        }

        ValidateContent(request.Content, request.Source, request.Confidence, request.Importance);

        var now = _timeProvider.GetUtcNow();
        var item = new MemoryItem(
            new MemoryId(Guid.NewGuid().ToString("N")),
            request.Owner,
            request.Type,
            request.Content,
            request.Source,
            request.Confidence,
            request.Importance,
            MemoryStatus.Active,
            now,
            now,
            request.ExpiresAt);

        await _store.AddAsync(item, cancellationToken);
        return item;
    }

    public Task<MemoryItem> UpdateAsync(
        MemoryId memoryId,
        MemoryUpdateRequest update,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(memoryId.Value, nameof(memoryId));
        ArgumentNullException.ThrowIfNull(update);
        ValidateContent(update.Content, update.Source, update.Confidence, update.Importance);
        if (!Enum.IsDefined(update.Status))
        {
            throw new ArgumentOutOfRangeException(nameof(update.Status), update.Status, "Unknown memory status.");
        }

        return _store.UpdateAsync(memoryId, update, _timeProvider.GetUtcNow(), cancellationToken);
    }

    public Task ForgetAsync(MemoryId memoryId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(memoryId.Value, nameof(memoryId));

        return _store.DeleteAsync(memoryId, cancellationToken);
    }

    public async Task<IReadOnlyList<MemoryItem>> ListRecentAsync(
        MemoryReadRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Owner);
        if (request.Type is { } type && !Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(request.Type), type, "Unknown memory type.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.Limit, nameof(request.Limit));

        var candidates = await _store.ListByOwnerAsync(request.Owner, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var now = _timeProvider.GetUtcNow();

        return candidates
            .Where(item => request.Type is null || item.Type == request.Type)
            .Where(item => item.Status is MemoryStatus.Active or MemoryStatus.Confirmed)
            .Where(item => item.ExpiresAt is null || item.ExpiresAt > now)
            .OrderByDescending(item => item.UpdatedAt)
            .ThenBy(item => item.Id.Value, StringComparer.Ordinal)
            .Take(request.Limit)
            .ToArray();
    }

    private static void ValidateContent(string content, string source, double confidence, double importance)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ValidateScore(confidence, nameof(confidence));
        ValidateScore(importance, nameof(importance));
    }

    private static void ValidateScore(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value < 0 || value > 1)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "The score must be finite and between 0 and 1.");
        }
    }
}
