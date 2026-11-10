using System.Text.Json.Serialization;
using HanyingAgent.Contracts.Tools.Operations.Requests;

namespace HanyingAgent.Contracts.LanguageModels.Models;

public sealed record ModelAssistantMessage(
    string? Text,
    [property: JsonRequired] IReadOnlyList<ToolCallRequest> ToolCalls) : ModelMessage;
