using System.Text.Json.Serialization;
using HanyingAgent.Contracts.Management.Tasks.Models;
using HanyingAgent.Contracts.Management.Users.Models;

namespace HanyingAgent.Contracts.Management.Sessions.Models;

public sealed record SessionInfo(
    SessionId Id,
    [property: JsonRequired] UserId UserId,
    string Name,
    SessionStatus Status,
    TaskId? CurrentTaskId,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastActiveAt);
