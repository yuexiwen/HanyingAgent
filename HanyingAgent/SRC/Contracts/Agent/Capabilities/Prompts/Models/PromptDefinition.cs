namespace HanyingAgent.Contracts.Agent.Capabilities.Prompts.Models;

public sealed record PromptDefinition(
    string Name,
    string Description,
    IReadOnlyList<PromptArgumentDefinition> Arguments);
