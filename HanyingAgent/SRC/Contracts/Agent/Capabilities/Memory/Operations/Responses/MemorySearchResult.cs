using HanyingAgent.Contracts.Agent.Capabilities.Memory.Models;

namespace HanyingAgent.Contracts.Agent.Capabilities.Memory.Operations.Responses;

public sealed record MemorySearchResult(
    MemoryItem Item,
    double Score);
