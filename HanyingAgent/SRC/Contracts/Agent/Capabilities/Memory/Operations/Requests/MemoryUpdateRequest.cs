using HanyingAgent.Contracts.Agent.Capabilities.Memory.Models;

namespace HanyingAgent.Contracts.Agent.Capabilities.Memory.Operations.Requests;

public sealed record MemoryUpdateRequest(
    string Content,
    string Source,
    double Confidence,
    double Importance,
    MemoryStatus Status,
    DateTimeOffset? ExpiresAt);
