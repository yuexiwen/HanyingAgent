using HanyingAgent.Contracts.Tasks.Models;

namespace HanyingAgent.Contracts.Tasks.Operations.Responses;

public sealed record TaskListResponse(
    IReadOnlyList<TaskInfo> Items,
    string? ContinuationToken);
