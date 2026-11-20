using HanyingAgent.Contracts.Agent.Capabilities.Memory.Models;

namespace HanyingAgent.Contracts.Agent.Capabilities.Memory.Operations.Requests;

public sealed record MemorySearchRequest(
    string Query,
    MemoryOwner Owner,
    MemoryType? Type = null,
    int TopK = 10);
