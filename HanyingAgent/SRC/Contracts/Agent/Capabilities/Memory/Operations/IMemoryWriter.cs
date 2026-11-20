using HanyingAgent.Contracts.Agent.Capabilities.Memory.Models;
using HanyingAgent.Contracts.Agent.Capabilities.Memory.Operations.Requests;

namespace HanyingAgent.Contracts.Agent.Capabilities.Memory.Operations;

public interface IMemoryWriter
{
    Task<MemoryItem> WriteAsync(
        MemoryWriteRequest request,
        CancellationToken cancellationToken);

    Task<MemoryItem> UpdateAsync(
        MemoryId memoryId,
        MemoryUpdateRequest update,
        CancellationToken cancellationToken);

    Task ForgetAsync(
        MemoryId memoryId,
        CancellationToken cancellationToken);
}
