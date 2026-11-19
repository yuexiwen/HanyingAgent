namespace HanyingAgent.Contracts.LanguageModels.Models;

public sealed record ModelTokenBudget(
    string ModelName,
    int ContextWindowTokens,
    int MaxOutputTokens,
    int SafetyMarginTokens,
    bool IsEstimate)
{
    public long MaxInputTokens => (long)ContextWindowTokens - MaxOutputTokens - SafetyMarginTokens;
}
