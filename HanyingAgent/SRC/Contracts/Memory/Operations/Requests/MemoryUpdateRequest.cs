using HanyingAgent.Contracts.Memory.Models;

namespace HanyingAgent.Contracts.Memory.Operations.Requests;

public sealed record MemoryUpdateRequest(
    string Content,
    string Source,
    double Confidence,
    double Importance,
    MemoryStatus Status,
    DateTimeOffset? ExpiresAt);
