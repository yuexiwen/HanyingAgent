namespace HanyingAgent.Contracts.LanguageModels.Models;

public sealed record ModelUsage(
    int? InputTokens,
    int? OutputTokens);
