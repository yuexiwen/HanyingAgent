using HanyingAgent.Contracts.Agent.Capabilities.Memory.Models;

namespace HanyingAgent.Contracts.Agent.Capabilities.Memory.Operations.Requests;

public sealed record MemoryReadRequest(
    MemoryOwner Owner,
    MemoryType? Type = null,
    int Limit = 20);
