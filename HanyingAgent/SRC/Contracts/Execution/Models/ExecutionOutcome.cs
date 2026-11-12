using System.Text.Json.Serialization;
using HanyingAgent.Contracts.LanguageModels.Models;

namespace HanyingAgent.Contracts.Execution.Models;

public sealed record ExecutionOutcome(
    [property: JsonRequired] ExecutionConclusion Conclusion,
    string? Text,
    [property: JsonRequired] ModelFinishReason FinishReason,
    ModelUsage? Usage = null,
    string? ProviderFinishReason = null);
