using System.Text.Json.Serialization;
using HanyingAgent.Contracts.Agent.LanguageModels.Models;
using HanyingAgent.Contracts.Agent.Capabilities.Tools.Operations.Requests;

namespace HanyingAgent.Contracts.Agent.LanguageModels.Operations.Responses;

public sealed record ModelResponse(
    string? Text,
    [property: JsonRequired] IReadOnlyList<ToolCallRequest> ToolCalls,
    [property: JsonRequired] ModelFinishReason FinishReason,
    ModelUsage? Usage = null,
    string? ProviderFinishReason = null);
