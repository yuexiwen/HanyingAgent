using HanyingAgent.Contracts.Management.Sessions.Models;
using TaskStatus = HanyingAgent.Contracts.Management.Tasks.Models.TaskStatus;

namespace HanyingAgent.Contracts.Management.Tasks.Operations.Requests;

public sealed record ListSessionTasksRequest(
    SessionId SessionId,
    TaskStatus? Status = null,
    int PageSize = 20,
    string? ContinuationToken = null);
