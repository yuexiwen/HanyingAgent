using HanyingAgent.Contracts.Management.Tasks.Models;
using TaskStatus = HanyingAgent.Contracts.Management.Tasks.Models.TaskStatus;

namespace HanyingAgent.Contracts.Management.Tasks.Operations.Responses;

public sealed record TaskResult(
    TaskId TaskId,
    TaskStatus Status,
    string? FinalAnswer,
    string? Error,
    DateTimeOffset CompletedAt);
