using HanyingAgent.Contracts.Management.Tasks.Models;

namespace HanyingAgent.Contracts.Management.Tasks.Operations.Responses;

public sealed record TaskListResponse(
    IReadOnlyList<TaskInfo> Items,
    string? ContinuationToken);
