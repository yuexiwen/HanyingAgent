using HanyingAgent.Contracts.Agent.LanguageModels.Operations.Requests;
using HanyingAgent.Contracts.Agent.LanguageModels.Operations.Responses;

namespace HanyingAgent.Contracts.Agent.LanguageModels.Operations;

public interface ILanguageModelRuntime
{
    /// <summary>
    /// Invokes the model once without executing tools or deciding whether the task is complete.
    /// </summary>
    Task<ModelResponse> InvokeAsync(
        ModelInvocation invocation,
        CancellationToken cancellationToken);
}
