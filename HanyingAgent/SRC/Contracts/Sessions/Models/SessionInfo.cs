using System.Text.Json.Serialization;
using HanyingAgent.Contracts.Tasks.Models;
using HanyingAgent.Contracts.Users.Models;

namespace HanyingAgent.Contracts.Sessions.Models;

public sealed record SessionInfo(
    SessionId Id,
    [property: JsonRequired] UserId UserId,
    string Name,
    SessionStatus Status,
    TaskId? CurrentTaskId,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastActiveAt);
