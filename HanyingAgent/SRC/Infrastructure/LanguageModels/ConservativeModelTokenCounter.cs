using System.Text;
using HanyingAgent.Contracts.LanguageModels.Models;
using HanyingAgent.Contracts.LanguageModels.Operations;

namespace HanyingAgent.Infrastructure.LanguageModels;

/// <summary>
/// Estimates one token per UTF-8 byte plus framing allowances. This is not a model tokenizer
/// or a guarantee for arbitrary chat templates; the safety margin reserves additional headroom.
/// </summary>
public sealed class ConservativeModelTokenCounter : IModelTokenCounter
{
    private readonly string _modelName;
    private readonly int _contextWindowTokens;
    private readonly int _defaultMaxOutputTokens;
    private readonly int _safetyMarginTokens;

    public ConservativeModelTokenCounter(
        string modelName, int contextWindowTokens, int defaultMaxOutputTokens, int safetyMarginTokens = 128)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(contextWindowTokens);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(defaultMaxOutputTokens);
        ArgumentOutOfRangeException.ThrowIfNegative(safetyMarginTokens);
        if ((long)defaultMaxOutputTokens + safetyMarginTokens >= contextWindowTokens)
        {
            throw new ArgumentException("Output reservation and safety margin must leave room for model input.");
        }
        _modelName = modelName;
        _contextWindowTokens = contextWindowTokens;
        _defaultMaxOutputTokens = defaultMaxOutputTokens;
        _safetyMarginTokens = safetyMarginTokens;
    }

    public ModelTokenBudget GetBudget(ModelOptions? options)
    {
        var modelName = options?.ModelName ?? _modelName;
        if (!string.Equals(modelName, _modelName, StringComparison.Ordinal))
        {
            throw new NotSupportedException("The requested model has no configured context budget.");
        }
        var output = options?.MaxOutputTokens ?? _defaultMaxOutputTokens;
        if (output <= 0 || (long)output + _safetyMarginTokens >= _contextWindowTokens)
        {
            throw new ArgumentOutOfRangeException(nameof(options),
                "Output reservation and safety margin must leave room for model input.");
        }
        return new ModelTokenBudget(modelName, _contextWindowTokens, output, _safetyMarginTokens, IsEstimate: true);
    }

    public long CountInputTokens(IReadOnlyList<ModelMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        long tokens = 8;
        foreach (var message in messages)
        {
            var text = message switch
            {
                ModelSystemMessage system => system.Content,
                ModelUserMessage user => user.Content,
                ModelAssistantMessage { ToolCalls.Count: 0, Text: { } content } => content,
                _ => throw new NotSupportedException("Token estimation supports only text-only chat messages.")
            };
            tokens += 8L + Encoding.UTF8.GetByteCount(text);
        }
        return tokens;
    }
}
