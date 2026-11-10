using HanyingAgent.Contracts.Memory.Models;

namespace HanyingAgent.Contracts.Memory.Operations.Requests;

public sealed record MemoryWriteRequest(
    MemoryOwner Owner,
    MemoryType Type,
    string Content,
    string Source,
    double Confidence,
    double Importance,
    DateTimeOffset? ExpiresAt = null);
