using HanyingAgent.Contracts.LanguageModels.Models;

namespace HanyingAgent.Contracts.LanguageModels.Operations;

public interface IModelTokenCounter
{
    /// <summary>Resolves model-specific limits and the output reservation before selecting context.</summary>
    ModelTokenBudget GetBudget(ModelOptions? options);

    /// <summary>Counts or explicitly estimates the complete input, including message framing.</summary>
    long CountInputTokens(IReadOnlyList<ModelMessage> messages);
}
