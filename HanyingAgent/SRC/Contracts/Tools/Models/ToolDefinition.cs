namespace HanyingAgent.Contracts.Tools.Models;

public sealed record ToolDefinition(
    string Name,
    string Description,
    string InputJsonSchema,
    bool HasSideEffects = false,
    TimeSpan? Timeout = null);
