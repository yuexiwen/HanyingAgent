namespace HanyingAgent.Contracts.Memory.Models;

public sealed record MemoryItem(
    MemoryId Id,
    MemoryOwner Owner,
    MemoryType Type,
    string Content,
    string Source,
    double Confidence,
    double Importance,
    MemoryStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ExpiresAt);
