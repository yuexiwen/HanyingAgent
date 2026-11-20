using HanyingAgent.Contracts.Management.Sessions.Models;

namespace HanyingAgent.Contracts.Management.Tasks.Models;

public sealed record TaskInfo(
    TaskId Id,
    SessionId SessionId,
    string Input,
    TaskStatus Status,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt);
