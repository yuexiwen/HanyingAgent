using HanyingAgent.Contracts.Context.Operations.Requests;
using HanyingAgent.Contracts.LanguageModels.Operations.Requests;

namespace HanyingAgent.Contracts.Context.Operations;

public interface IContextBuilder
{
    /// <summary>
    /// Builds a text-only invocation from trusted instructions, ordered messages and supplied memories.
    /// Validates and formats the supplied data without reading memory or conversation services,
    /// calling the model, or modifying conversation, task or memory state.
    /// </summary>
    Task<ModelInvocation> BuildAsync(
        BuildContextRequest request,
        CancellationToken cancellationToken);
}
