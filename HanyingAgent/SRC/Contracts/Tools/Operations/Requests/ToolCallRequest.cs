using HanyingAgent.Contracts.Tools.Models;

namespace HanyingAgent.Contracts.Tools.Operations.Requests;

public sealed record ToolCallRequest(
    ToolCallId Id,
    string ToolName,
    string ArgumentsJson);
