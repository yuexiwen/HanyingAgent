using HanyingAgent.Contracts.Memory.Models;

namespace HanyingAgent.Contracts.Memory.Operations.Requests;

public sealed record MemoryReadRequest(
    MemoryOwner Owner,
    MemoryType? Type = null,
    int Limit = 20);
