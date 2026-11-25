using HanyingAgent.Contracts.Agent.Capabilities.Tools.Models;
using HanyingAgent.Contracts.Agent.Capabilities.Tools.Operations.Requests;
using HanyingAgent.Contracts.Agent.Capabilities.Tools.Operations.Responses;

namespace HanyingAgent.Runtime.Agent.Capabilities.Tools;

public interface ILocalTool
{
    ToolDefinition Definition { get; }

    /// <summary>
    /// Validates arguments against this tool's declared schema before performing any action.
    /// Implementations must support concurrent calls and observe cancellation.
    /// </summary>
    Task<ToolResult> InvokeAsync(InvokeToolRequest request, CancellationToken cancellationToken);
}
