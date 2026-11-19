using HanyingAgent.Contracts.Context.Operations.Requests;
using HanyingAgent.Contracts.LanguageModels.Operations.Requests;

namespace HanyingAgent.Contracts.Context.Operations;

public interface IContextBuilder
{
    /// <summary>
    /// Builds a budgeted text-only invocation from trusted instructions, candidate history and memories.
    /// Preserves the current input and selects complete optional items under one model input budget.
    /// Validates and formats the supplied data without reading memory or conversation services,
    /// calling the model, or modifying conversation, task or memory state.
    /// </summary>
    Task<ModelInvocation> BuildAsync(
        BuildContextRequest request,
        CancellationToken cancellationToken);
}
