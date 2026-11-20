using HanyingAgent.Contracts.Management.Sessions.Models;
using HanyingAgent.Contracts.Management.Tasks.Models;

namespace HanyingAgent.Contracts.Agent.Capabilities.Tools.Operations.Requests;

public sealed record InvokeToolRequest(
    SessionId SessionId,
    TaskId TaskId,
    ToolCallRequest Call);
