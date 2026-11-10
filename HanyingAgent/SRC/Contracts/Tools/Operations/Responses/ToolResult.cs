using HanyingAgent.Contracts.Tools.Models;

namespace HanyingAgent.Contracts.Tools.Operations.Responses;

public sealed record ToolResult(
    ToolCallStatus Status,
    string? Content,
    string? Error = null);
