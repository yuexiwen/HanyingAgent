using HanyingAgent.Contracts.Context.Operations.Requests;
using HanyingAgent.Contracts.LanguageModels.Operations.Requests;

namespace HanyingAgent.Contracts.Context.Operations;

public interface IContextBuilder
{
    /// <summary>
    /// Builds a text-only invocation from trusted instructions, ordered messages and explicit memory owners.
    /// Does not call the model or modify conversation, task or memory state.
    /// </summary>
    Task<ModelInvocation> BuildAsync(
        BuildContextRequest request,
        CancellationToken cancellationToken);
}
