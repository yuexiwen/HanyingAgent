using HanyingAgent.Contracts.Memory.Models;

namespace HanyingAgent.Contracts.Memory.Operations.Responses;

public sealed record MemorySearchResult(
    MemoryItem Item,
    double Score);
