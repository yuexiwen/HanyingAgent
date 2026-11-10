namespace HanyingAgent.Contracts.LanguageModels.Models;

public sealed record ModelOptions(
    string? ModelName = null,
    double? Temperature = null,
    int? MaxOutputTokens = null);
