using HanyingAgent.Contracts.Memory.Models;
using HanyingAgent.Contracts.Memory.Operations.Requests;

namespace HanyingAgent.Contracts.Memory.Operations;

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
