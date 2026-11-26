using HanyingAgent.Contracts.Agent.Capabilities.Prompts.Models;

namespace HanyingAgent.Contracts.Agent.Capabilities.Prompts.Operations;

public interface IPromptManager
{
    Task<IReadOnlyList<PromptDefinition>> ListDefinitionsAsync(CancellationToken cancellationToken);

    Task<RenderedPrompt> RenderAsync(
        string promptName, IReadOnlyDictionary<string, string> arguments, CancellationToken cancellationToken);
}
