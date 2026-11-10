using HanyingAgent.Contracts.Tools.Models;
using HanyingAgent.Contracts.Tools.Operations.Requests;
using HanyingAgent.Contracts.Tools.Operations.Responses;

namespace HanyingAgent.Contracts.Tools.Operations;

public interface IToolManager
{
    /// <summary>
    /// Lists definitions permitted for the task, optionally narrowed by the requested names.
    /// </summary>
    Task<IReadOnlyList<ToolDefinition>> ListDefinitionsAsync(
        ListToolDefinitionsRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Waits for this invocation and returns a normalized observation, including unknown outcomes.
    /// </summary>
    Task<ToolObservation> InvokeAsync(
        InvokeToolRequest request,
        CancellationToken cancellationToken);
}
