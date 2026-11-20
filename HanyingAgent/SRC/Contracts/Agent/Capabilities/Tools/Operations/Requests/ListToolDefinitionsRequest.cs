using HanyingAgent.Contracts.Management.Sessions.Models;
using HanyingAgent.Contracts.Management.Tasks.Models;

namespace HanyingAgent.Contracts.Agent.Capabilities.Tools.Operations.Requests;

public sealed record ListToolDefinitionsRequest(
    SessionId SessionId,
    TaskId TaskId,
    IReadOnlyList<string>? AllowedTools = null);
