using HanyingAgent.Contracts.Agent.Capabilities.Tools.Models;

namespace HanyingAgent.Contracts.Agent.Capabilities.Tools.Operations.Responses;

public sealed record ToolResult(
    ToolCallStatus Status,
    string? Content,
    string? Error = null);
