using HanyingAgent.Contracts.Sessions.Models;

namespace HanyingAgent.Contracts.Tasks.Models;

public sealed record TaskInfo(
    TaskId Id,
    SessionId SessionId,
    string Input,
    TaskStatus Status,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt);
