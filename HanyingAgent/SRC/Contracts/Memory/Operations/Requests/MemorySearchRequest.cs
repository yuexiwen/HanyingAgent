using HanyingAgent.Contracts.Memory.Models;

namespace HanyingAgent.Contracts.Memory.Operations.Requests;

public sealed record MemorySearchRequest(
    string Query,
    MemoryOwner Owner,
    MemoryType? Type = null,
    int TopK = 10);
