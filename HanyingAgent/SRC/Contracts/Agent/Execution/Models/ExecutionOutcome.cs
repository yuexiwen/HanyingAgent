using System.Text.Json.Serialization;
using HanyingAgent.Contracts.Agent.LanguageModels.Models;

namespace HanyingAgent.Contracts.Agent.Execution.Models;

public sealed record ExecutionOutcome(
    [property: JsonRequired] ExecutionConclusion Conclusion,
    string? Text,
    [property: JsonRequired] ModelFinishReason FinishReason,
    ModelUsage? Usage = null,
    string? ProviderFinishReason = null);
