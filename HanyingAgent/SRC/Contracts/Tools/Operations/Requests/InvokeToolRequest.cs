using HanyingAgent.Contracts.Sessions.Models;
using HanyingAgent.Contracts.Tasks.Models;

namespace HanyingAgent.Contracts.Tools.Operations.Requests;

public sealed record InvokeToolRequest(
    SessionId SessionId,
    TaskId TaskId,
    ToolCallRequest Call);
