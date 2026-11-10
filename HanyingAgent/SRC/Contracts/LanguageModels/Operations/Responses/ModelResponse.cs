using System.Text.Json.Serialization;
using HanyingAgent.Contracts.LanguageModels.Models;
using HanyingAgent.Contracts.Tools.Operations.Requests;

namespace HanyingAgent.Contracts.LanguageModels.Operations.Responses;

public sealed record ModelResponse(
    string? Text,
    [property: JsonRequired] IReadOnlyList<ToolCallRequest> ToolCalls,
    [property: JsonRequired] ModelFinishReason FinishReason,
    ModelUsage? Usage = null,
    string? ProviderFinishReason = null);
