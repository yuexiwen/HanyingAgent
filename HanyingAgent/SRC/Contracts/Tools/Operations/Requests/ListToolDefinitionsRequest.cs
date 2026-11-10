using HanyingAgent.Contracts.Sessions.Models;
using HanyingAgent.Contracts.Tasks.Models;

namespace HanyingAgent.Contracts.Tools.Operations.Requests;

public sealed record ListToolDefinitionsRequest(
    SessionId SessionId,
    TaskId TaskId,
    IReadOnlyList<string>? AllowedTools = null);
