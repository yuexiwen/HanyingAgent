namespace HanyingAgent.Contracts.Agent.LanguageModels.Models;

public sealed record ModelUsage(
    int? InputTokens,
    int? OutputTokens);
