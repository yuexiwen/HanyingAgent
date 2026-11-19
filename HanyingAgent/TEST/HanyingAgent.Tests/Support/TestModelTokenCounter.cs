using HanyingAgent.Contracts.LanguageModels.Models;
using HanyingAgent.Contracts.LanguageModels.Operations;

namespace HanyingAgent.Tests.Support;

internal sealed class TestModelTokenCounter(
    int contextWindowTokens = int.MaxValue,
    int defaultMaxOutputTokens = 256,
    int safetyMarginTokens = 0) : IModelTokenCounter
{
    public ModelTokenBudget GetBudget(ModelOptions? options) =>
        new(options?.ModelName ?? "test-model", contextWindowTokens,
            options?.MaxOutputTokens ?? defaultMaxOutputTokens, safetyMarginTokens, IsEstimate: false);

    public long CountInputTokens(IReadOnlyList<ModelMessage> messages) =>
        messages.Sum(message => (long)(message switch
        {
            ModelSystemMessage system => system.Content.Length,
            ModelUserMessage user => user.Content.Length,
            ModelAssistantMessage assistant => assistant.Text!.Length,
            _ => throw new NotSupportedException()
        }));
}
