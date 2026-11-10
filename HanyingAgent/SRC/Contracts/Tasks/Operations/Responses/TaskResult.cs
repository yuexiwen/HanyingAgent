using HanyingAgent.Contracts.Tasks.Models;
using TaskStatus = HanyingAgent.Contracts.Tasks.Models.TaskStatus;

namespace HanyingAgent.Contracts.Tasks.Operations.Responses;

public sealed record TaskResult(
    TaskId TaskId,
    TaskStatus Status,
    string? FinalAnswer,
    string? Error,
    DateTimeOffset CompletedAt);
