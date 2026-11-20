using HanyingAgent.Contracts.Agent.Capabilities.Tools.Models;

namespace HanyingAgent.Contracts.Agent.Capabilities.Tools.Operations.Requests;

public sealed record ToolCallRequest(
    ToolCallId Id,
    string ToolName,
    string ArgumentsJson);
