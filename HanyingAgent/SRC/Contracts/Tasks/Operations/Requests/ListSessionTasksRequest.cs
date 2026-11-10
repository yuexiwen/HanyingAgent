using HanyingAgent.Contracts.Sessions.Models;
using TaskStatus = HanyingAgent.Contracts.Tasks.Models.TaskStatus;

namespace HanyingAgent.Contracts.Tasks.Operations.Requests;

public sealed record ListSessionTasksRequest(
    SessionId SessionId,
    TaskStatus? Status = null,
    int PageSize = 20,
    string? ContinuationToken = null);
