using HanyingAgent.Contracts.Agent.Capabilities.Memory.Models;

namespace HanyingAgent.Contracts.Agent.Capabilities.Memory.Operations.Requests;

public sealed record MemoryWriteRequest(
    MemoryOwner Owner,
    MemoryType Type,
    string Content,
    string Source,
    double Confidence,
    double Importance,
    DateTimeOffset? ExpiresAt = null);
